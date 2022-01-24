using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreamDanmuku_Server.SocketIO;
using System;
using System.Collections.Generic;
using System.Linq;
using static StreamDanmuku_Server.SocketIO.Server;

namespace StreamDanmuku_Server.Data
{
    [JsonObject(MemberSerialization.OptOut)]
    public class Room : ICloneable
    {
        public enum StreamMode
        {
            TRTC,
            QuickLive,
            WebRTC,
            AudioChat
        }
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
        public StreamMode Mode { get; set; } = StreamMode.QuickLive;
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
                socket.Emit(onName, Helper.SetError(311));
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
            if (room != null)// 通过邀请码或ID查询到了房间, 返回房间ID以及密码要求
            {
                socket.Emit(onName, Helper.SetOK("ok", new { id = room.RoomID, passwordNeeded = room.PasswordNeeded }));
                RuntimeLog.WriteSystemLog(onName, $"{onName} success, RoomID={data["id"]}", true);
            }
            else// 检索失败
            {
                socket.Emit(onName, Helper.SetError(311));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, room is invalid", false);
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
                        RuntimeLog.WriteSystemLog(onName, $"{onName} success", true);
                    }
                    else// 超出房间规定最大人数
                    {
                        socket.Emit(onName, Helper.SetError(312));
                        RuntimeLog.WriteSystemLog(onName, $"{onName} error, room is full", false);
                    }
                }
                else
                {
                    socket.Emit(onName, Helper.SetError(310));
                    RuntimeLog.WriteSystemLog(onName, $"{onName} error, password is wrong", false);
                }
            }
            else
            {
                socket.Emit(onName, Helper.SetError(311));
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
                    socket.Emit(onName, Helper.SetError(313));
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
                        socket.Emit(onName, Helper.SetError(310));
                        RuntimeLog.WriteSystemLog(onName, $"{onName} error, password is incorrect", false);
                    }
                }
                else// 验证了不需要密码的房间
                {
                    socket.Emit(onName, Helper.SetError(401));
                    RuntimeLog.WriteSystemLog(onName, $"{onName} error, verify non password room", false);
                }
            }
            else
            {
                socket.Emit(onName, Helper.SetError(311));
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
            // 根据用户存储看直播的房间ID, 获取服务端连接
            var server = Online.StreamerUser.FirstOrDefault(x => x.Value.Id == user.StreamRoom).Value;
            server.WebSocket.Emit(onName, new { from = user.Id });
            RuntimeLog.WriteSystemLog(onName, $"{onName}, client {user.NickName} to server {server.NickName} leave", true);
        }
        /// <summary>
        /// 令牌传输
        /// </summary>
        /// <param name="socket">直播 WebSocket 连接</param>
        /// <param name="data">candidate: 令牌信息; to: 目标ID, 一般用于服务端向观众</param>
        public static void OnCandidate(MsgHandler socket, JToken data, string onName, User user)
        {
            switch (user.Status)
            {
                case User.UserStatus.Client:// 观众向服务端传输令牌
                {
                    var room = Online.Rooms.Find(x => x.RoomID == user.StreamRoom);
                    if (room != null)
                    {
                        var server = Online.StreamerUser.FirstOrDefault(x => x.Value.Id == user.StreamRoom).Value;
                        server.WebSocket.Emit(onName, new { data = data["candidate"].ToObject<object>(), from = user.Id });
                        RuntimeLog.WriteSystemLog(onName, $"{onName}, client {user.NickName} to server {server.NickName} Candidate", true);
                    }
                    else
                    {
                        Console.WriteLine("room is null");
                    }
                    break;
                }
                case User.UserStatus.Streaming:// 服务端向观众传输令牌
                {
                    var client = Online.StreamerUser.FirstOrDefault(x => x.Value.Id == (int)data["to"]).Value;
                    client.WebSocket.Emit(onName, new { data = data["candidate"].ToObject<object>(), from = user.Id });
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
            var server = Online.StreamerUser.FirstOrDefault(x => x.Value.Id == user.StreamRoom).Value;
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
                case User.UserStatus.Client:
                {
                    var room = Online.Rooms.Find(x => x.RoomID == user.StreamRoom);
                    if (room != null)
                    {
                        // 拉流端发送offer请求，之后服务器将请求转发给推流端，并携带此用户的ID
                        var server = Online.StreamerUser.FirstOrDefault(x => x.Value.Id == room.RoomID).Value;
                        server.WebSocket.Emit(onName, new { data = data["offer"].ToString(), from = user.Id });
                        RuntimeLog.WriteSystemLog(onName, $"{onName}, client {user.NickName} to server {server.NickName} offer", true);
                    }

                    break;
                }
                case User.UserStatus.Streaming:
                {
                    // 推流端应当在应答中添加ID
                    var client = Online.StreamerUser.FirstOrDefault(x => x.Value.Id == ((int)data["to"])).Value;
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
            // TODO: 禁止自己进入自己房间
            var room = Online.Rooms.Find(x => x.RoomID == (int)data["id"]);
            user.Status = User.UserStatus.Client;
            user.StreamRoom = room.RoomID;
            room.Clients.Add(socket);
            // Online.StreamerUser[socket.ID].StreamRoom = room.UserID;
            RuntimeLog.WriteSystemLog(onName, $"{onName}, user {user.NickName} enter room {room.RoomID}", true);
            socket.Emit(onName, Helper.SetOK("ok", new {roomInfo = room.WithoutSecret(),}));
        }
        /// <summary>
        /// 创建房间
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">Room表单</param>
        public static void CreateRoom(MsgHandler socket, JToken data, string onName, User user)
        {
            if (Online.Rooms.Any(x => x.RoomID == user.Id))
            {
                RuntimeLog.WriteSystemLog(onName, $"{onName} fail, msg: duplicate user room", false);
                socket.Emit(onName, Helper.SetError(308));
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
                socket.Emit(onName, Helper.SetError(401));
                return;
            }
            user.Status = User.UserStatus.Streaming;
            RuntimeLog.WriteSystemLog(onName, $"{onName} success, userID: {room.RoomID}, password: {room.Password}, title: {room.Title}, max: {room.Max}", true);
            Online.Rooms.Add(room);
            socket.Emit(onName, Helper.SetOK());
        }

        public static void GetPushUrl(MsgHandler socket, JToken data, string onName, User user)
        {
            var room = Online.Rooms.First(x => x.RoomID == user.Id);
            string pushUrl = room.GenLivePushURL();
            socket.Emit(onName, Helper.SetOK("ok", new {server="rtmp://livepush.hellobaka.xyz/StreamDanmuku/",key=pushUrl}));
            RuntimeLog.WriteSystemLog(onName, $"{onName} success, genPushUrl: {pushUrl}",true);
        }
        public static void GetPullUrl(MsgHandler socket, JToken data, string onName, User user)
        {            
            var room = Online.Rooms.First(x => x.RoomID == user.StreamRoom);
            string pullUrl = room.GenLivePullURL();
            socket.Emit(onName, Helper.SetOK("ok", new {server="webrtc://livepull.hellobaka.xyz/StreamDanmuku/",key=pullUrl}));
            RuntimeLog.WriteSystemLog(onName, $"{onName} success, genPullUrl: {pullUrl}",true);
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
        #endregion

        private const string pushKey = "03d8e34e502182ed17f7a5ea8b2674de";
        private const string pullKey = "AChGA5ysw5zCWCArcrrb";
        public string GenLivePushURL()
        {
            long timestamp = Helper.TimeStamp + 60 * 60 * 12;
            return $"{InviteCode}?txSecret={GetTXSecret(InviteCode, pushKey, timestamp)}&txTime={timestamp:X}";
        }
        public string GenLivePullURL()
        {
            long timestamp = Helper.TimeStamp + 60 * 3;
            return $"{InviteCode}?txSecret={GetTXSecret(InviteCode, pullKey, timestamp)}&txTime={timestamp:X}";
        }

        public void RoomBoardCast(string type, object msg)
        {
            Clients.ForEach(x=>x.Emit(type, msg));
            var server = Online.StreamerUser.First(x => x.Value.Id == RoomID).Value;
            server?.WebSocket.Emit(type, msg);
        }
        public static string GetTXSecret(string streamName, string key, long timestamp) =>
            Helper.MD5Encrypt(key + streamName + timestamp.ToString("X"), false).ToLower();

    }
}
