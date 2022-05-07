using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using StreamDanmaku_Server.Enum;
using static StreamDanmaku_Server.SocketIO.Server;
using System.IO;
using TencentCloud.Common;
using TencentCloud.Common.Profile;
using TencentCloud.Live.V20180801;
using TencentCloud.Live.V20180801.Models;

namespace StreamDanmaku_Server.Data
{
    [JsonObject(MemberSerialization.OptOut)]
    public class Room : ICloneable
    {
        /// <summary>
        /// 直播间标题
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// 直播间ID, 通常认为这个ID与创建直播间的用户ID相同
        /// </summary>
        public int RoomID { get; set; }

        /// <summary>
        /// 创建直播间的用户昵称
        /// </summary>
        public string CreatorName { get; set; }

        /// <summary>
        /// 房间是否公开 - 是否可被搜索/在房间列表展示
        /// </summary>
        public bool IsPublic { get; set; }

        /// <summary>
        /// 进入房间是否需要密码
        /// </summary>
        public bool PasswordNeeded => !string.IsNullOrWhiteSpace(Password);

        /// <summary>
        /// 进入房间所需要的密码
        /// </summary>
        [JsonIgnore]
        public string Password { get; set; }

        /// <summary>
        /// 邀请码
        /// </summary>
        [JsonIgnore]
        public string InviteCode { get; set; }

        /// <summary>
        /// 房间最大容纳人数
        /// </summary>
        public int Max { get; set; }

        /// <summary>
        /// 房间创建时间
        /// </summary>
        public DateTime CreateTime { get; set; }

        /// <summary>
        /// 房间内观众对应的WebSocket(或许应当改成User对象
        /// </summary>
        [JsonIgnore]
        public List<MsgHandler> Clients { get; set; } = new();

        /// <summary>
        /// 房间人数
        /// </summary>
        public int ClientCount => Clients.Count;

        /// <summary>
        /// 房间是否可加入
        /// </summary>
        public bool Enterable { get; set; }

        /// <summary>
        /// 房主User对象
        /// </summary>
        [JsonIgnore]
        public User Server { get; set; }

        /// <summary>
        /// 房间弹幕列表
        /// </summary>
        public List<Danmaku> DanmakuList { get; set; } = new();

        public object Clone() => MemberwiseClone();

        /// <summary>
        /// 获取脱敏数据
        /// </summary>
        /// <returns></returns>
        public object WithoutSecret()
        {
            var c = (Room) Clone();
            return new
            {
                c.Title, c.RoomID, c.CreatorName, c.PasswordNeeded, c.IsPublic, c.Max, c.CreateTime, c.ClientCount,
                c.InviteCode
            };
        }

        #region WebSocket逻辑

        /// <summary>
        /// 直播用户获取自己的房间信息
        /// </summary>
        /// <param name="socket">直播 WebSocket 连接</param>
        /// <param name="data">不读取参数</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void RoomInfo(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = Online.Rooms.Find(x => x.RoomID == user.Id);
            if (room != null)
            {
                socket.Emit(onName, Helper.SetOK("ok", new {roomInfo = room.WithoutSecret()}));
                RuntimeLog.WriteUserLog(user, onName, $"拉取房间信息成功，{GetRoomLogText(room)}", true);
            }
            else
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.RoomNotExist));
                RuntimeLog.WriteUserLog(user, onName, $"拉取房间信息失败，房间ID={user.Id}，房间信息为空", false);
            }
        }

        /// <summary>
        /// 使用邀请码或房间ID来加入房间
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">query: 房间ID或邀请码</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void JoinRoom(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = Online.Rooms.Find(x =>
                x.InviteCode == data["query"]?.ToString() || x.RoomID == (int) data["query"]);
            if (room is {Enterable: true}) // 通过邀请码或ID查询到了房间, 返回房间ID以及密码要求
            {
                socket.Emit(onName, Helper.SetOK("ok", new {id = room.RoomID, passwordNeeded = room.PasswordNeeded}));
                RuntimeLog.WriteUserLog(user, onName, $"房间加入成功，{GetRoomLogText(room)}", true);
            }
            else // 检索失败
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.RoomNotExistOrUnenterable));
                RuntimeLog.WriteUserLog(user, onName, $"房间加入失败，筛选条件={data["query"]}，房间不存在或无法加入", false);
            }
        }

        /// <summary>
        /// 判断能否加入房间
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">id: 要加入的房间ID; password: 需要的时候再传密码</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void EnterRoom(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = GetRoomByIDOrInviteCode(data["id"].ToString(), socket, onName, user);
            if (room == null) return;
            if (room.Password == data["password"].ToString().Trim())
            {
                if (room.Max > room.ClientCount)
                {
                    socket.Emit(onName, Helper.SetOK());
                    user.Status = UserStatus.Client;
                    RuntimeLog.WriteUserLog(user, onName, $"加入房间判定成功，{GetRoomLogText(room)}", true);
                }
                else // 超出房间规定最大人数
                {
                    socket.Emit(onName, Helper.SetError(ErrorCode.RoomFull));
                    RuntimeLog.WriteUserLog(user, onName, $"加入房间判定失败，{GetRoomLogText(room)}，房间已满", false);
                }
            }
            else // 密码错误
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.WrongRoomPassword));
                RuntimeLog.WriteUserLog(user, onName, $"加入房间判定失败，{GetRoomLogText(room)}，密码错误", false);
            }
        }

        /// <summary>
        /// 获取公开房间列表
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">未读取内容</param>
        /// <param name="onName"></param>
        /// <param name="user"></param>
        public static void RoomList(MsgHandler socket, JToken data, string onName, User user)
        {
            socket.Emit(onName, Helper.SetOK("ok", Online.Rooms.Where(x => x.IsPublic && x.Enterable).ToList()));
            RuntimeLog.WriteUserLog(user, onName, $"拉取公开房间列表成功", true);
        }

        /// <summary>
        /// 验证房间密码是否正确
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">id: 待验证房间ID; password: 待验证密码</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void VerifyRoomPassword(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = GetRoomByIDOrInviteCode(data["id"].ToString(), socket, onName, user);
            if (room == null) return;

            if (room.Enterable is false)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.RoomUnenterable));
                RuntimeLog.WriteUserLog(user, onName, $"验证房间密码失败，{GetRoomLogText(room)}，房间不可加入", false);
                return;
            }

            if (room.PasswordNeeded)
            {
                if (room.Password == data["password"].ToString().Trim())
                {
                    socket.Emit(onName, Helper.SetOK());
                    RuntimeLog.WriteUserLog(user, onName, $"验证房间密码成功，{GetRoomLogText(room)}，使用密码={data["password"]}",
                        true);
                }
                else
                {
                    socket.Emit(onName, Helper.SetError(ErrorCode.WrongRoomPassword));
                    RuntimeLog.WriteUserLog(user, onName,
                        $"验证房间密码失败，{GetRoomLogText(room)}，使用密码={data["password"]}，密码错误", false);
                }
            }
            else // 验证了不需要密码的房间
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.ParamsFormatError));
                RuntimeLog.WriteUserLog(user, onName,
                    $"验证房间密码失败，{GetRoomLogText(room)}，使用密码={data["password"]}，房间不需要密码验证", false);
            }
        }

        /// <summary>
        /// 用户离开房间事件
        /// </summary>
        /// <param name="socket">直播 WebSocket 连接</param>
        /// <param name="data">未使用字段</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void OnLeave(MsgHandler socket, JToken data, string onName, User user)
        {
            // 
            switch (user.Status)
            {
                case UserStatus.Client:
                {
                    if (user.CurrentRoom != null)
                    {
                        user.CurrentRoom.Clients.Remove(socket);
                        user.Status = UserStatus.StandBy;
                        user.CurrentRoom.RoomBoardCast("OnLeave", new {from = user.Id});
                        if (user.CurrentRoom.Clients.Count == 0 && user.CurrentRoom.Server == null)
                        {
                            RuntimeLog.WriteSystemLog("RoomVanish", $"房间销毁成功，{GetRoomLogText(user.CurrentRoom)}", true);
                            user.CurrentRoom.RoomBoardCast("RoomVanish", new {roomID = user.Id});
                            BoardCast("RoomRemove", new {roomID = user.Id});
                            Online.Rooms.Remove(user.CurrentRoom);
                        }
                    }

                    break;
                }
                case UserStatus.Streaming:
                    // 房主主动离开直播间，应当销毁直播间
                    user.CurrentRoom.RoomBoardCast("RoomVanish", new {roomID = user.Id});
                    BoardCast("RoomRemove", new {roomID = user.Id});
                    Online.Rooms.Remove(user.CurrentRoom);
                    user.Status = UserStatus.StandBy;
                    RuntimeLog.WriteSystemLog("RoomVanish", $"房间销毁成功，{GetRoomLogText(user.CurrentRoom)}", true);
                    break;
            }

            RuntimeLog.WriteUserLog(user, onName, $"离开房间成功，用户ID={user.Id}，{GetRoomLogText(user.CurrentRoom)}", true);
            user.CurrentRoom = null;
        }

        /// <summary>
        /// 后台弹幕监视取消
        /// </summary>
        /// <param name="socket">后台 WebSocket 连接</param>
        /// <param name="data">invite_code: 需要操作的房间邀请码</param>
        /// <param name="onName">操作名称</param>
        public static void RemoveMonitor_Admin(MsgHandler socket, JToken data, string onName)
        {
            var room = GetRoomByIDOrInviteCode(data["invite_code"]?.ToString(), socket, onName, isAdmin: true);
            if (room == null) return;
            socket.MonitoredDanmaku = null;
            RuntimeLog.WriteUserLog("Admin", onName, $"后台弹幕监视取消，{GetRoomLogText(room)}", true);
        }

        /// <summary>
        /// 后台发送弹幕
        /// </summary>
        /// <param name="socket">后台 WebSocket 连接</param>
        /// <param name="data">invite_code: 需要操作的房间邀请码</param>
        /// <param name="onName">操作名称</param>
        public static void SendDanmaku_Admin(MsgHandler socket, JToken data, string onName)
        {
            var room = GetRoomByIDOrInviteCode(data["invite_code"]?.ToString(), socket, onName, isAdmin: true);
            if (room == null) return;
            var danmaku = new Danmaku()
            {
                Content = data["content"].ToString().Replace("\n", "").Replace("\r", "").Replace("\t", "").Trim(),
                Color = "#FFFFFF",
                Position = DanmakuPosition.Roll,
                SenderUserName = "Admin",
                SenderUserID = 0,
                Time = Helper.TimeStamp
            };
            room.DanmakuList.Add(danmaku);
            room.RoomBoardCast("OnDanmaku", danmaku);
            socket.Emit(onName, Helper.SetOK());
            RuntimeLog.WriteUserLog("Admin", onName, $"后台发送弹幕，{GetRoomLogText(room)}，内容={data["content"]}", true);
        }

        /// <summary>
        /// 后台拉取正在直播房间列表
        /// </summary>
        /// <param name="socket">后台 WebSocket 连接</param>
        /// <param name="data">未使用字段</param>
        /// <param name="onName">操作名称</param>
        public static void GetRoom_Admin(MsgHandler socket, JToken data, string onName)
        {
            List<object> r = new();
            foreach (var item in Online.Rooms)
            {
                r.Add(item.WithoutSecret());
            }

            socket.Emit(onName, Helper.SetOK("ok", r));
            RuntimeLog.WriteUserLog("Admin", onName, $"后台拉取正在直播房间列表成功", true);
        }

        /// <summary>
        /// 后台拉取房间所有弹幕 并启用监视
        /// </summary>
        /// <param name="socket">后台 WebSocket 连接</param>
        /// <param name="data">invite_code: 需要操作的房间邀请码</param>
        /// <param name="onName">操作名称</param>
        public static void GetDanmaku_Admin(MsgHandler socket, JToken data, string onName)
        {
            var room = GetRoomByIDOrInviteCode(data["invite_code"]?.ToString(), socket, onName, isAdmin: true);
            if (room == null) return;

            socket.MonitoredDanmaku = room;
            socket.Emit(onName, Helper.SetOK("ok", room.DanmakuList));
            RuntimeLog.WriteUserLog("Admin", onName, $"后台拉取房间弹幕列表成功，{GetRoomLogText(room)}", true);
        }

        /// <summary>
        /// 后台切断直播
        /// </summary>
        /// <param name="socket">后台 WebSocket 连接</param>
        /// <param name="data">invite_code: 需要操作的房间邀请码</param>
        /// <param name="onName">操作名称</param>
        public static void StopStream_Admin(MsgHandler socket, JToken data, string onName)
        {
            var room = GetRoomByIDOrInviteCode(data["invite_code"]?.ToString(), socket, onName, isAdmin: true);
            if (room == null) return;
            room.CallToDestroy();
            room.Enterable = false;
            RuntimeLog.WriteUserLog("Admin", onName, $"后台房间断流成功，{GetRoomLogText(room)}", true);
        }

        /// <summary>
        /// 主动断流直播
        /// </summary>
        private void CallToDestroy()
        {
            // 广播此条消息, 客户端收到此消息会自动卸载视频后台也会清空在线用户
            RoomBoardCast("Admin_CallRoomDestroy", "管理员切断了直播");
            Clients.ForEach(x => OnLeave(x, null, "OnLeave", x.CurrentUser));
            // 调用腾讯云官方API来切断直播，此时OBS会弹窗提示连接断开
            try
            {
                Credential cred = new()
                {
                    SecretId = Config.GetConfig<string>("TXCloud_SecretId"),
                    SecretKey = Config.GetConfig<string>("TXCloud_SecretKey")
                };

                ClientProfile clientProfile = new();
                HttpProfile httpProfile = new()
                {
                    Endpoint = ("live.tencentcloudapi.com")
                };
                clientProfile.HttpProfile = httpProfile;

                LiveClient client = new(cred, "", clientProfile);
                DropLiveStreamRequest req = new()
                {
                    StreamName = InviteCode,
                    DomainName = "http://livepull.hellobaka.xyz",
                    AppName = "StreamDanmaku"
                };
                client.DropLiveStreamSync(req);
                Enterable = false;
            }
            catch (Exception e)
            {
                RuntimeLog.WriteSystemLog("CutStream", $"Cut {InviteCode} room fail, ex: {e.Message}", false);
            }
        }

        /// <summary>
        /// 已加入房间
        /// </summary>
        /// <param name="socket">直播 WebSocket 连接</param>
        /// <param name="data">id: 房间ID; </param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void RoomEntered(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = Online.Rooms.Find(x => x.RoomID == (int) data["id"]);
            if (room is not {Enterable: true})
            {            
                RuntimeLog.WriteUserLog(user, onName, $"加入房间失败，筛选条件={data["id"]}，房间不存在或不可加入", false);
                socket.Emit(onName, Helper.SetError(ErrorCode.RoomNotExistOrUnenterable));
                return;
            }

            user.CurrentRoom = room;
            user.Status = UserStatus.Client;
            if (room.Clients.Contains(socket) is false)
            {
                room.Clients.Add(socket);
            }
            RuntimeLog.WriteUserLog(user, onName, $"加入房间成功，{GetRoomLogText(room)}", true);
            socket.Emit(onName, Helper.SetOK("ok", new {roomInfo = room.WithoutSecret()}));
            room.RoomBoardCast("OnEnter", user.Id);
        }

        /// <summary>
        /// 创建房间
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">Room表单</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void CreateRoom(MsgHandler socket, JToken data, string onName, User user)
        {
            if (Online.Rooms.Any(x => x.RoomID == user.Id))
            {
                RuntimeLog.WriteUserLog(user, onName, $"创建房间失败，当前用户已经创建了一个房间", false);
                socket.Emit(onName, Helper.SetError(ErrorCode.DuplicateRoom));
                return;
            }

            if (!user.CanStream)
            {
                RuntimeLog.WriteUserLog(user, onName, $"创建房间失败，当前用户被禁播", false);
                socket.Emit(onName, Helper.SetError(ErrorCode.UserCanNotStream));
                return;
            }

            Room room = new()
            {
                IsPublic = (bool) data["isPublic"],
                Max = (int) data["max"],
                Title = data["title"].ToString(),
                CreatorName = user.NickName,
                Password = data["password"].ToString(),
                RoomID = user.Id,
                CreateTime = DateTime.Now,
                InviteCode = Helper.GenCaptcha(6, true)
            };
            if (room.Max is < 2 or > 51) // 高级
            {
                RuntimeLog.WriteUserLog(user, onName, $"创建房间失败，非法参数", false);
                socket.Emit(onName, Helper.SetError(ErrorCode.ParamsFormatError));
                return;
            }

            user.Status = UserStatus.Streaming;
            user.CurrentRoom = room;
            room.Server = user;
            RuntimeLog.WriteUserLog(user, onName, $"创建房间成功，用户ID={room.RoomID}, 密码={room.Password}, 标题={room.Title}, 房间容量={room.Max}", true);
            Online.Rooms.Add(room);
            socket.Emit(onName, Helper.SetOK());
        }

        /// <summary>
        /// 主播掉线重连
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">未使用字段</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void ResumeRoom(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = GetRoomByIDOrInviteCode(user.Id.ToString(), socket, onName, user);
            if (room == null) return;
            user.Status = UserStatus.Streaming;
            user.CurrentRoom = room;
            room.Server = user;
            socket.Emit(onName, Helper.SetOK());
            RuntimeLog.WriteUserLog(user, onName, $"主播恢复直播成功，{GetRoomLogText(room)}", true);
            room.RoomBoardCast("StreamerReConnect", "");
        }

        /// <summary>
        /// 获取推流URL
        /// </summary>
        /// <param name="socket">直播在线 WebSocket 连接</param>
        /// <param name="data">未使用字段</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void GetPushUrl(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = GetRoomByIDOrInviteCode(user.Id.ToString(), socket, onName, user);
            if (room == null) return;
            string pushUrl = room.GenLivePushURL();
            // 似乎应当写进配置内
            socket.Emit(onName,
                Helper.SetOK("ok", new {server = "rtmp://livepush.hellobaka.xyz/StreamDanmaku/", key = pushUrl}));
            RuntimeLog.WriteUserLog(user, onName, $"获取推流URL成功，{GetRoomLogText(room)}，URL={pushUrl}", true);
        }

        /// <summary>
        /// 获取拉流URL
        /// </summary>
        /// <param name="socket">直播在线 WebSocket 连接</param>
        /// <param name="data">未使用字段</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void GetPullUrl(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = user.CurrentRoom;
            string pullUrl = room.GenLivePullURL(true);
            socket.Emit(onName,
                Helper.SetOK("ok",
                    new {server = "http://livepull.hellobaka.xyz/StreamDanmaku/", key = pullUrl}));

            RuntimeLog.WriteUserLog(user, onName, $"获取拉流URL成功，{GetRoomLogText(room)}，URL={pullUrl}", true);
        }

        /// <summary>
        /// 后台使用邀请码获取拉流URL
        /// </summary>
        /// <param name="socket">后台 WebSocket 连接</param>
        /// <param name="data">invite_code: 需要操作的房间邀请码</param>
        /// <param name="onName">操作名称</param>
        public static void GetPullUrl_Admin(MsgHandler socket, JToken data, string onName)
        {
            var room = GetRoomByIDOrInviteCode(data["invite_code"]?.ToString(), socket, onName, isAdmin:true);
            if (room == null) return;
            
            string pullUrl = room.GenLivePullURL(true);
            socket.Emit(onName,
                Helper.SetOK("ok",
                    new {server = "http://livepull.hellobaka.xyz/StreamDanmaku/", key = pullUrl}));

            RuntimeLog.WriteUserLog("Admin", onName, $"后台获取拉流URL成功，{GetRoomLogText(room)}，URL={pullUrl}", true);
        }

        /// <summary>
        /// 切换直播可用状态
        /// </summary>
        /// <param name="socket">直播在线 WebSocket 连接</param>
        /// <param name="data">flag: 想要切换到的状态</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void SwitchStream(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = Online.Rooms.First(x => x.RoomID == user.Id);
            room.Enterable = (bool) data["flag"];
            if (room.Enterable)
            {
                BoardCast("RoomAdd", room.WithoutSecret());
            }
            else
            {
                room.RoomBoardCast("RoomClose", room.RoomID);
            }

            socket.Emit(onName, Helper.SetOK());
            RuntimeLog.WriteUserLog(user, onName, $"切换直播可用状态成功，{GetRoomLogText(room)}，状态={room.Enterable}", true);
        }

        /// <summary>
        /// 发送弹幕
        /// </summary>
        /// <param name="socket">直播在线 WebSocket 连接</param>
        /// <param name="data">content: 弹幕内容; color: 颜色; position: 弹幕位置;</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void Danmaku(MsgHandler socket, JToken data, string onName, User user)
        {
            if (!user.CanSendDanmaku)
            {
                socket.Emit("SendDanmaku", Helper.SetError(ErrorCode.UserCanNotSendDanmaku));
                return;
            }

            var room = user.CurrentRoom;
            var danmaku = new Danmaku()
            {
                Content = data["content"].ToString().Replace("\n", "").Replace("\r", "").Replace("\t", "").Trim(),
                Color = data["color"].ToString(),
                Position = (DanmakuPosition) (int) data["position"],
                SenderUserName = user.NickName,
                SenderUserID = user.Id,
                Time = Helper.TimeStamp
            };
            room.DanmakuList.Add(danmaku);
            room.RoomBoardCast("OnDanmaku", danmaku);
            socket.Emit("SendDanmaku", Helper.SetOK("ok", danmaku));
            RuntimeLog.WriteUserLog(user, onName, $"发送弹幕成功，{GetRoomLogText(room)}，弹幕内容={danmaku.Content}", true);
        }

        /// <summary>
        /// 获取直播最后10条弹幕
        /// </summary>
        /// <param name="socket">直播在线 WebSocket 连接</param>
        /// <param name="data">未使用字段</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void GetRoomDanmaku(MsgHandler socket, JToken data, string onName, User user)
        {
            //TODO: 拉取条数可配置
            socket.Emit(onName, Helper.SetOK("ok", user.CurrentRoom.DanmakuList.TakeLast(10).ToList()));
            RuntimeLog.WriteUserLog(user, onName, $"获取房间历史弹幕成功，{GetRoomLogText(user.CurrentRoom)}", true);
        }

        /// <summary>
        /// 缩略图
        /// </summary>
        public List<object> Captures = new();

        /// <summary>
        /// 上传缩略图
        /// </summary>
        /// <param name="socket">直播在线 WebSocket 连接</param>
        /// <param name="data">base64: 图片</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void UploadCapture(MsgHandler socket, JToken data, string onName, User user)
        {
            //TODO: 文件夹使网页可读取, 日志
            var room = user.CurrentRoom;
            string base64 = data["base64"].ToString().Replace("data:image/png;base64,", "");
            string fileName = $"{Helper.TimeStamp}.png";
            Directory.CreateDirectory($"Capture\\{room.InviteCode}");
            File.WriteAllBytes($"Capture\\{room.InviteCode}\\{fileName}", Convert.FromBase64String(base64));
            room.Captures.Add(new {timestamp = Helper.TimeStamp, filename = $"Capture/{room.InviteCode}/{fileName}"});
            RuntimeLog.WriteUserLog(user, onName, $"缩略图上传成功，{GetRoomLogText(user.CurrentRoom)}", true);
        }

        /// <summary>
        /// 获取直播间缩略图
        /// </summary>
        /// <param name="socket">后台在线 WebSocket 连接</param>
        /// <param name="data">invite_code: 操作房间的邀请码</param>
        /// <param name="onName">操作名称</param>
        public static void GetCaptures(MsgHandler socket, JToken data, string onName)
        {
            var room = GetRoomByIDOrInviteCode(data["invite_code"]?.ToString(), socket, onName, isAdmin:true);
            if (room == null) return;
            var arr = room.Captures;
            socket.Emit(onName, Helper.SetOK("ok", arr));
            RuntimeLog.WriteUserLog("Admin", onName, $"后台拉取缩略图成功，{GetRoomLogText(room)}", true);
        }

        #endregion

        #region 腾讯云直播相关

        /// <summary>
        /// 推流Key
        /// </summary>
        private string pushKey = Config.GetConfig<string>("Live_PushKey");

        /// <summary>
        /// 拉流Key
        /// </summary>
        private string pullKey = Config.GetConfig<string>("Live_PullKey");

        /// <summary>
        /// 生成推流URL的参数部分
        /// </summary>
        /// <returns>推流URL的参数部分</returns>
        public string GenLivePushURL()
        {
            long timestamp = Helper.TimeStamp + 60 * 60 * 12;
            return $"{InviteCode}?txSecret={GetTXSecret(InviteCode, pushKey, timestamp)}&txTime={timestamp:X}";
        }

        /// <summary>
        /// 生成拉流URL的参数部分
        /// </summary>
        /// <param name="flv">是否使用flv</param>
        /// <returns>拉流URL的参数部分</returns>
        public string GenLivePullURL(bool flv = false)
        {
            long timestamp = Helper.TimeStamp + 60 * 3;
            return
                $"{InviteCode}{(flv ? ".flv" : "")}?txSecret={GetTXSecret(InviteCode, pullKey, timestamp)}&txTime={timestamp:X}";
        }

        /// <summary>
        /// 计算TXSecret
        /// </summary>
        /// <param name="streamName">流名称, 在此处为房间邀请码</param>
        /// <param name="key">拉流Key或推流Key</param>
        /// <param name="timestamp">时间戳</param>
        /// <returns></returns>
        public static string GetTXSecret(string streamName, string key, long timestamp) =>
            Helper.MD5Encrypt(key + streamName + timestamp.ToString("X"), false).ToLower();

        #endregion

        /// <summary>
        /// 房间内广播, 包括主播
        /// </summary>
        /// <param name="type">消息类型</param>
        /// <param name="msg">消息内容</param>
        public void RoomBoardCast(string type, object msg)
        {
            Clients.ForEach(x => x.Emit(type, msg));
            Server?.WebSocket.Emit(type, msg);
            Online.Admins.Where(x => x.MonitoredDanmaku?.InviteCode == InviteCode).ToList().ForEach(x => x.Emit(type, msg));
        }

        private static string GetRoomLogText(Room room) => $"房间ID={room?.RoomID}，邀请码={room?.InviteCode}";
        /// <summary>
        /// 通过房间ID或邀请码查询在线房间，未找到将返回房间不存在错误
        /// </summary>
        /// <param name="query">查询</param>
        /// <param name="socket">调用连接</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        /// <param name="isAdmin">是否为后台调用</param>
        /// <returns>查询到或null</returns>
        private static Room GetRoomByIDOrInviteCode(string query, MsgHandler socket, string onName, User user = null,
            bool isAdmin = false)
        {
            var room = Online.Rooms.FirstOrDefault(x => x.InviteCode == query || x.RoomID.ToString() == query);
            if (room == null)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.RoomNotExist));
                if (user == null) RuntimeLog.WriteSystemLog(onName, $"房间拉取失败，筛选条件={query}", false);
                else
                {
                    if (isAdmin) RuntimeLog.WriteUserLog("Admin", onName, $"房间拉取失败，筛选条件={query}", false);
                    else RuntimeLog.WriteUserLog(user, onName, $"房间拉取失败，筛选条件={query}", false);
                }
            }
            else
            {
                return room;
            }

            return null;
        }
    }
}