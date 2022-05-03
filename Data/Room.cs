﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreamDanmaku_Server.SocketIO;
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
        /// 拉流地址
        /// </summary>
        public string StreamPullURL { get; set; }

        /// <summary>
        /// 推流地址
        /// </summary>
        public string StreamPushURL { get; set; }

        /// <summary>
        /// 房间是否可加入
        /// </summary>
        public bool Enterable { get; set; } = false;
        /// <summary>
        /// 直播类别
        /// </summary>
        public StreamMode Mode { get; set; } = StreamMode.QuickLive;

        [JsonIgnore]
        public User Server { get; set; }
        public List<Danmaku> DanmakuList { get; set; } = new();
        public object Clone() => MemberwiseClone();
        /// <summary>
        /// 获取脱敏数据
        /// </summary>
        /// <returns></returns>
        public object WithoutSecret()
        {
            var c = (Room)Clone();
            return new { c.Title, c.RoomID, c.CreatorName, c.PasswordNeeded, c.IsPublic, c.Max, c.CreateTime, c.ClientCount, c.InviteCode, c.Mode };
        }
        #region WebSocket逻辑

        /// <summary>
        /// 直播用户获取自己的房间信息
        /// </summary>
        /// <param name="socket">直播 WebSocket 连接</param>
        /// <param name="data">不读取参数</param>
        /// <param name="onName"></param>
        /// <param name="user"></param>
        public static void RoomInfo(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = Online.Rooms.Find(x => x.RoomID == user.Id);
            if (room != null)
            {   
                socket.Emit(onName, Helper.SetOK("ok", new {roomInfo = room.WithoutSecret()}));
                RuntimeLog.WriteSystemLog(onName, $"{onName} success, RoomID={room.RoomID}", true);
            }
            else
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.RoomNotExist));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, room is invalid", false);
            }
        }

        /// <summary>
        /// 使用邀请码或房间ID来加入房间
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">query: 房间ID或邀请码</param>
        /// <param name="onName"></param>
        /// <param name="user"></param>
        public static void JoinRoom(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = Online.Rooms.Find(x => x.InviteCode == data["query"].ToString() || x.RoomID == (int)data["query"]);
            if (room is {Enterable: true})// 通过邀请码或ID查询到了房间, 返回房间ID以及密码要求
            {
                socket.Emit(onName, Helper.SetOK("ok", new { id = room.RoomID, passwordNeeded = room.PasswordNeeded }));
                RuntimeLog.WriteSystemLog(onName, $"{onName} success, RoomID={data["id"]}", true);
            }
            else// 检索失败
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.RoomNotExistOrUnenterable));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, room is invalid or unenterable", false);
            }
        }

        /// <summary>
        /// 判断能否加入房间
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">id: 要加入的房间ID; password: 需要的时候再传密码</param>
        /// <param name="onName"></param>
        /// <param name="user"></param>
        public static void EnterRoom(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = Online.Rooms.Find(x => x.RoomID == (int)data["id"]);
            if (room != null)
            {
                if (room.Password == data["password"].ToString().Trim())
                {
                    if (room.Max > room.ClientCount)
                    {
                        socket.Emit(onName, Helper.SetOK());
                        user.Status = UserStatus.Client;

                        RuntimeLog.WriteSystemLog(onName, $"{onName} success", true);
                    }
                    else// 超出房间规定最大人数
                    {
                        socket.Emit(onName, Helper.SetError(ErrorCode.RoomFull));
                        RuntimeLog.WriteSystemLog(onName, $"{onName} error, room is full", false);
                    }
                }
                else
                {
                    socket.Emit(onName, Helper.SetError(ErrorCode.WrongRoomPassword));
                    RuntimeLog.WriteSystemLog(onName, $"{onName} error, password is wrong", false);
                }
            }
            else
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.RoomNotExist));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, room is null", false);
            }
        }
        /// <summary>
        /// 获取公开房间列表
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">未读取内容</param>
        public static void RoomList(MsgHandler socket, JToken data, string onName, User user)
        {
            socket.Emit(onName, Helper.SetOK("ok", Online.Rooms.Where(x => x.IsPublic && x.Enterable).ToList()));
            RuntimeLog.WriteSystemLog(onName, $"{onName} success", true);
        }
        /// <summary>
        /// 验证房间密码是否正确
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">id: 待验证房间ID; password: 待验证密码</param>
        public static void VerifyRoomPassword(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = Online.Rooms.Find(x => x.RoomID == (int)data["id"]);
            if (room != null)
            {
                if (room.Enterable is false)
                {
                    socket.Emit(onName, Helper.SetError(ErrorCode.RoomUnenterable));
                    RuntimeLog.WriteSystemLog(onName, $"{onName} error, enterable is false", false);
                    return;
                }
                if (room.PasswordNeeded)
                {
                    if (room.Password == data["password"].ToString().Trim())
                    {
                        socket.Emit(onName, Helper.SetOK());
                        RuntimeLog.WriteSystemLog(onName, $"{onName} success, RoomID={data["id"]}, password={data["password"]}", true);
                    }
                    else
                    {
                        socket.Emit(onName, Helper.SetError(ErrorCode.WrongRoomPassword));
                        RuntimeLog.WriteSystemLog(onName, $"{onName} error, password is incorrect", false);
                    }
                }
                else// 验证了不需要密码的房间
                {
                    socket.Emit(onName, Helper.SetError(ErrorCode.ParamsFormatError));
                    RuntimeLog.WriteSystemLog(onName, $"{onName} error, verify non password room", false);
                }
            }
            else
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.RoomNotExist));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, room is invalid", false);
            }
        }
        /// <summary>
        /// 用户离开房间事件
        /// </summary>
        /// <param name="socket">直播 WebSocket 连接</param>
        /// <param name="data">未使用字段</param>
        public static void OnLeave(MsgHandler socket, JToken data, string onName, User user)
        {
            switch (user.Status)
            {
                case UserStatus.Client:
                {
                    if (user.CurrentRoom != null)
                    {
                        user.CurrentRoom.Clients.Remove(socket);
                        user.Status = UserStatus.StandBy;
                        user.CurrentRoom.RoomBoardCast("OnLeave",new {from = user.Id});
                        if (user.CurrentRoom.Clients.Count == 0 && user.CurrentRoom.Server == null)
                        {                                
                            RuntimeLog.WriteSystemLog("Room Removed", $"RoomRemoved, id={user.Id}", true);
                            user.CurrentRoom.RoomBoardCast("RoomVanish", new { roomID = user.Id });
                            BoardCast("RoomRemove", new { roomID = user.Id });
                            Online.Rooms.Remove(user.CurrentRoom);
                        }
                    }
                    break;
                }
                case UserStatus.Streaming:
                    RuntimeLog.WriteSystemLog("Room Removed", $"RoomRemoved, id={user.Id}", true);
                    user.CurrentRoom.RoomBoardCast("RoomVanish", new {roomID = user.Id});
                    BoardCast("RoomRemove", new {roomID = user.Id});
                    Online.Rooms.Remove(user.CurrentRoom);
                    user.Status = UserStatus.StandBy;
                    break;
            }
            user.CurrentRoom = null;
            RuntimeLog.WriteSystemLog(onName, $"{onName}, client {user.NickName} leave", true);
        }

        public static void RemoveMonitor_Admin(MsgHandler socket, JToken data, string onName)
        {
            string invite = data["invite_code"]?.ToString();
            var room = Online.Rooms.FirstOrDefault(x => x.InviteCode == invite);
            if (room == null)
            {
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, room is null", false);
                return;
            }
            else
            {
                socket.MonitoredDanmaku = null;
            }
        }

        public static void SendDanmaku_Admin(MsgHandler socket, JToken data, string onName)
        {
            string invite = data["invite_code"]?.ToString();
            var room = Online.Rooms.FirstOrDefault(x => x.InviteCode == invite);
            if (room == null)
            {
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, room is null", false);
                return;
            }
            else
            {
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
            }
        }

        public static void GetRoom_Admin(MsgHandler socket, JToken data, string onName)
        {
            List<object> r = new();
            foreach(var item in Online.Rooms)
            {
                r.Add(new
                {
                    uid = item.RoomID,
                    nickname = item.CreatorName,
                    invite_code = item.InviteCode,
                    start_time = item.CreateTime,
                    cap = $"{item.Clients.Count}/{item.Max}"
                });
            }
            socket.Emit(onName, Helper.SetOK("ok", r));
        }

        public static void GetDanmaku_Admin(MsgHandler socket, JToken data, string onName)
        {
            string invite = data["invite_code"]?.ToString();
            var room = Online.Rooms.FirstOrDefault(x => x.InviteCode == invite);
            if (room == null)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.RoomNotExist));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, room is null", false);
                return;
            }
            else
            {
                socket.MonitoredDanmaku = room;
                socket.Emit(onName, room.DanmakuList);
            }
        }

        public static void StopStream_Admin(MsgHandler socket, JToken data, string onName)
        {
            string invite = data["invite_code"]?.ToString();
            var room = Online.Rooms.FirstOrDefault(x => x.InviteCode == invite);
            if(room == null)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.RoomNotExist));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, room is null", false);
                return;
            }
            else
            {
                room.CallToDestroy();
            }
        }

        public void CallToDestroy()
        {
            RoomBoardCast("Admin_CallRoomDestroy", "管理员切断了直播");
            Clients.ForEach(x => OnLeave(x, null, "OnLeave", x.CurrentUser));
            try
            {
                Credential cred = new()
                {
                    SecretId = Config.GetConfig<string>("TXCloud_SecretId"),
                    SecretKey = Config.GetConfig<string>("TXCloud_SecretKey")
                };

                ClientProfile clientProfile = new();
                HttpProfile httpProfile = new();
                httpProfile.Endpoint = ("live.tencentcloudapi.com");
                clientProfile.HttpProfile = httpProfile;

                LiveClient client = new(cred, "", clientProfile);
                DropLiveStreamRequest req = new()
                {
                    StreamName = InviteCode,
                    DomainName = "http://livepull.hellobaka.xyz",
                    AppName = "StreamDanmaku"
                };
                DropLiveStreamResponse resp = client.DropLiveStreamSync(req);
                Enterable = false;
                RuntimeLog.WriteSystemLog("CutStream", $"Cut {InviteCode} room success", true);
            }
            catch (Exception e)
            {
                RuntimeLog.WriteSystemLog("CutStream", $"Cut {InviteCode} room fail, ex: {e.Message}", false);
            }
        }

        /// <summary>
        /// 令牌传输
        /// </summary>
        /// <param name="socket">直播 WebSocket 连接</param>
        /// <param name="data">candidate: 令牌信息; to: 目标ID, 一般用于服务端向观众</param>
        public static void OnCandidate(MsgHandler socket, JToken data, string onName, User user)
        {
            var server = user.CurrentRoom.Server;
            switch (user.Status)
            {
                case UserStatus.Client:// 观众向服务端传输令牌
                {
                    if (user.CurrentRoom != null)
                    {
                        server?.WebSocket.Emit(onName, new { data = data["candidate"].ToObject<object>(), from = user.Id });
                        RuntimeLog.WriteSystemLog(onName, $"{onName}, client {user.NickName} to server {server.NickName} Candidate", true);
                    }
                    else
                    {
                        Console.WriteLine("room is null");
                    }
                    break;
                }
                case UserStatus.Streaming:// 服务端向观众传输令牌
                {
                    var client = Online.Users.FirstOrDefault(x => x.Id == (int) data["to"]);
                    client?.WebSocket.Emit(onName, new { data = data["candidate"].ToObject<object>(), from = user.Id });
                    RuntimeLog.WriteSystemLog(onName, $"{onName}, server {user.NickName} to client {client.NickName} Candidate", true);
                    break;
                }
            }
        }
        /// <summary>
        /// Answer信息传输
        /// </summary>
        /// <param name="socket">直播 WebSocket 连接</param>
        /// <param name="data">answer: Answer信息</param>
        public static void OnAnswer(MsgHandler socket, JToken data, string onName, User user)
        {
            // Client => Server: createAnswer
            var server = user.CurrentRoom.Server;
            server.WebSocket.Emit(onName, new { data = data["answer"].ToObject<object>(), from = user.Id });
            RuntimeLog.WriteSystemLog(onName, $"{onName}, user {user.NickName} to server {server.NickName} answer", true);
        }
        /// <summary>
        /// 传输Offer信息
        /// </summary>
        /// <param name="socket">直播 WebSocket 连接</param>
        /// <param name="data"></param>
        public static void OnOffer(MsgHandler socket, JToken data, string onName, User user)
        {
            // Client => Server: open request
            // Server => Client: createOffer
            switch (user.Status)
            {
                case UserStatus.Client:
                {
                    if (user.CurrentRoom != null)
                    {
                        // 拉流端发送offer请求，之后服务器将请求转发给推流端，并携带此用户的ID
                        var server = user.CurrentRoom.Server;
                        server.WebSocket.Emit(onName, new { data = data["offer"].ToString(), from = user.Id });
                        RuntimeLog.WriteSystemLog(onName, $"{onName}, client {user.NickName} to server {server.NickName} offer", true);
                    }

                    break;
                }
                case UserStatus.Streaming:
                {
                    // 推流端应当在应答中添加ID
                    var client = Online.Users.FirstOrDefault(x => x.Id == ((int)data["to"]));
                    client.WebSocket.Emit(onName, new { data = data["offer"].ToObject<object>(), from = user.Id });
                    RuntimeLog.WriteSystemLog(onName, $"{onName}, server {user.NickName} to client {client.NickName} offer", true);
                    break;
                }
            }
        }
        /// <summary>
        /// 已加入房间
        /// </summary>
        /// <param name="socket">直播 WebSocket 连接</param>
        /// <param name="data">id: 房间ID; </param>
        public static void RoomEntered(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = Online.Rooms.Find(x => x.RoomID == (int)data["id"]);
            if (room is not {Enterable: true})
            {
                RuntimeLog.WriteSystemLog(onName, $"{onName}, room[{(int)data["id"]}] is null", false);
                socket.Emit(onName, Helper.SetError(ErrorCode.RoomNotExist));
                return;
            }
            user.CurrentRoom = room;
            user.Status = UserStatus.Client;
            if(room.Clients.Contains(socket) is false)
            {
                room.Clients.Add(socket);
            }
            RuntimeLog.WriteSystemLog(onName, $"{onName}, user {user.NickName} enter room {room.RoomID}", true);
            socket.Emit(onName, Helper.SetOK("ok", new {roomInfo = room.WithoutSecret()}));
            room.RoomBoardCast("OnEnter", user.Id);
        }
        /// <summary>
        /// 创建房间
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">Room表单</param>
        public static void CreateRoom(MsgHandler socket, JToken data, string onName, User user)
        {
            // if (Online.Rooms.Any(x => x.RoomID == user.Id))
            // {
            //     RuntimeLog.WriteSystemLog(onName, $"{onName} fail, msg: duplicate user room", false);
            //     socket.Emit(onName, Helper.SetError(ErrorCode.DuplicateRoom));
            //     return;
            // }
            if(!user.CanStream)
            {
                RuntimeLog.WriteSystemLog(onName, $"{onName} fail, msg: user can not stream", false);
                socket.Emit(onName, Helper.SetError(ErrorCode.UserCanNotStream));
                return;
            }
            Room room = new()
            {
                IsPublic = (bool)data["isPublic"],
                Max = (int)data["max"],
                Title = data["title"].ToString(),
                CreatorName = user.NickName,
                Password = data["password"].ToString(),
                RoomID = user.Id,
                CreateTime = DateTime.Now,
                InviteCode = Helper.GenCaptcha(6, true),
                Mode = (StreamMode)(int)data["mode"]
            };
            if (room.Max is < 2 or > 51)// 高级
            {
                RuntimeLog.WriteSystemLog(onName, $"{onName} fail, msg: invalid args", false);
                socket.Emit(onName, Helper.SetError(ErrorCode.ParamsFormatError));
                return;
            }
            user.Status = UserStatus.Streaming;
            user.CurrentRoom = room;
            room.Server = user;
            RuntimeLog.WriteSystemLog(onName, $"{onName} success, userID: {room.RoomID}, password: {room.Password}, title: {room.Title}, max: {room.Max}", true);
            Online.Rooms.Add(room);
            socket.Emit(onName, Helper.SetOK());
        }

        public static void ResumeRoom(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = Online.Rooms.Find(x => x.RoomID == user.Id);
            if (room != null)
            {
                user.Status = UserStatus.Streaming;
                user.CurrentRoom = room;
                room.Server = user;
                socket.Emit(onName, Helper.SetOK());
                room.RoomBoardCast("StreamerReConnect", "");
            }
            else
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.RoomNotExist));
            }
        }
        public static void GetPushUrl(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = Online.Rooms.First(x => x.RoomID == user.Id);
            string pushUrl = room.GenLivePushURL();
            socket.Emit(onName, Helper.SetOK("ok", new {server="rtmp://livepush.hellobaka.xyz/StreamDanmaku/",key=pushUrl}));
            RuntimeLog.WriteSystemLog(onName, $"{onName} success, genPushUrl: {pushUrl}",true);
        }
        public static void GetPullUrl(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = user.CurrentRoom;
            string pullUrl = string.Empty;
            switch ((StreamType)(int)data["type"])
            {
                case StreamType.WebRTC:
                    pullUrl = room.GenLivePullURL(false);
                    socket.Emit(onName, Helper.SetOK("ok", new {server="webrtc://livepull.hellobaka.xyz/StreamDanmaku/",key=pullUrl}));
                    break;
                case StreamType.RTMP:
                    pullUrl = room.GenLivePullURL(true);
                    socket.Emit(onName, Helper.SetOK("ok", new {server="http://livepull.hellobaka.xyz/StreamDanmaku/",key=pullUrl}));
                    break;
                default:
                    break;
            }
            RuntimeLog.WriteSystemLog(onName, $"{onName} success, genPullUrl: {pullUrl}",true);
        }
        public static void GetPullUrl_Admin(MsgHandler socket, JToken data, string onName)
        {
            string invite = data["invite_code"]?.ToString();
            var room = Online.Rooms.FirstOrDefault(x => x.InviteCode == invite);
            if(room==null)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.RoomNotExist));
                RuntimeLog.WriteSystemLog(onName, $"Room not exists", false);
                return;
            }
            string pullUrl = string.Empty;
            switch ((StreamType)(int)data["type"])
            {
                case StreamType.WebRTC:
                    pullUrl = room.GenLivePullURL(false);
                    socket.Emit(onName, Helper.SetOK("ok", new { server = "webrtc://livepull.hellobaka.xyz/StreamDanmaku/", key = pullUrl }));
                    break;
                case StreamType.RTMP:
                    pullUrl = room.GenLivePullURL(true);
                    socket.Emit(onName, Helper.SetOK("ok", new { server = "http://livepull.hellobaka.xyz/StreamDanmaku/", key = pullUrl }));
                    break;
                default:
                    break;
            }
            RuntimeLog.WriteSystemLog(onName, $"{onName} success, genPullUrl: {pullUrl}", true);
        }
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
        }
        /// <summary>
        /// 发送弹幕
        /// </summary>
        /// <param name="socket">直播连接</param>
        /// <param name="data">content: 弹幕内容; color: 颜色; position: 弹幕位置;</param>
        public static void Danmaku(MsgHandler socket, JToken data, string onName, User user)
        {
            if(!user.CanSendDanmaku)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.UserCanNotSendDanmaku));
                return;
            }
            var room = user.CurrentRoom;
            var danmaku = new Danmaku()
            {
                Content = data["content"].ToString().Replace("\n", "").Replace("\r", "").Replace("\t", "").Trim(),
                Color = data["color"].ToString(),
                Position = (DanmakuPosition)(int)data["position"],
                SenderUserName = user.NickName,
                SenderUserID = user.Id,
                Time = Helper.TimeStamp
            };
            room.DanmakuList.Add(danmaku);
            room.RoomBoardCast("OnDanmaku", danmaku);
            Online.Admins.Where(x => x.MonitoredDanmaku == room).ToList().ForEach(x => x.Emit("OnDanmaku", danmaku));
            socket.Emit("SendDanmaku", Helper.SetOK("ok", danmaku));
            RuntimeLog.WriteSystemLog(onName, $"{onName} success, danmaku: {danmaku.Content}",true);
        }

        public static void GetRoomDanmaku(MsgHandler socket, JToken data, string onName, User user)
        {
            socket.Emit(onName, Helper.SetOK("ok", user.CurrentRoom.DanmakuList.TakeLast(10).ToList()));
        }
        #endregion

        private string pushKey = Config.GetConfig<string>("Live_PushKey");
        private string pullKey = Config.GetConfig<string>("Live_PullKey");
        public string GenLivePushURL()
        {
            long timestamp = Helper.TimeStamp + 60 * 60 * 12;
            return $"{InviteCode}?txSecret={GetTXSecret(InviteCode, pushKey, timestamp)}&txTime={timestamp:X}";
        }
        public string GenLivePullURL(bool flv = false)
        {
            long timestamp = Helper.TimeStamp + 60 * 3;
            return $"{InviteCode}{(flv ? ".flv" : "")}?txSecret={GetTXSecret(InviteCode, pullKey, timestamp)}&txTime={timestamp:X}";
        }

        public void RoomBoardCast(string type, object msg)
        {
            Clients.ForEach(x=>x.Emit(type, msg));
            Server?.WebSocket.Emit(type, msg);
        }
        public static string GetTXSecret(string streamName, string key, long timestamp) =>
            Helper.MD5Encrypt(key + streamName + timestamp.ToString("X"), false).ToLower();
        public List<object> Captures = new();
        public static void UploadCapture(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = user.CurrentRoom;
            string base64 = data["base64"].ToString();
            string fileName = $"{Helper.TimeStamp}.png";
            Directory.CreateDirectory($"Capture\\{room.InviteCode}");
            File.WriteAllBytes($"{room.InviteCode}\\{fileName}", Convert.FromBase64String(base64));
            room.Captures.Add(new { timestamp = Helper.TimeStamp, filename = $"Capture\\{room.InviteCode}\\{fileName}" });
            //RuntimeLog.WriteSystemLog(onName, "", true);
        }
        public static void GetCaptures(MsgHandler socket, JToken data, string onName)
        {
            string invite = data["invite_code"].ToString();
            var room = Online.Rooms.FirstOrDefault(x => x.InviteCode == invite);
            if (room != null)
            {
                var arr = room.Captures;
                socket.Emit(onName, Helper.SetOK("ok", arr));
            }
            else
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.RoomNotExist));
            }
        }
    }
}
