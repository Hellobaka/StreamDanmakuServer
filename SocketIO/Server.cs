using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp.Server;
using StreamDanmuku_Server.Data;
using WebSocketSharp;
using JWT.Exceptions;
using System.Threading;

namespace StreamDanmuku_Server.SocketIO
{
    public class Server
    {
        WebSocketServer Instance;
        ushort port;
        public Server(ushort Port)
        {
            port = Port;
            Instance = new(port);
            Instance.AddWebSocketService<MsgHandler>("/main");
        }
        public void StartServer()
        {
            Instance.Start();
            Console.WriteLine($"Server URL: ws://127.0.0.1:{port}/main");
            RuntimeLog.WriteSystemLog("WebSocketServer", $"Start Running on Port: {port}...", true);
        }
        public void StopServer()
        {
            Instance.Stop();
            RuntimeLog.WriteSystemLog("WebSocketServer", $"Shut down Server...", true);
        }
        public static void BoardCast(string type, object msg)
        {
            foreach (var item in Online.Users)
            {
                item.Value.WebSocket.Emit(type, msg);
            }
        }

        public class MsgHandler : WebSocketBehavior
        {
            protected override void OnMessage(MessageEventArgs e)
            {
                HandleMessage(this, e.Data);
            }
            protected override void OnOpen()
            {
                RuntimeLog.WriteSystemLog("WebSocketServer", $"Client Connected, id={ID}", true);
            }
            protected override void OnClose(CloseEventArgs e)
            {
                //TODO: 房主掉线房间关闭
                if (Online.Users.ContainsKey(ID))
                    Online.Users.Remove(ID);
                if (Online.StreamerUser.ContainsKey(ID))
                {
                    var client = Online.StreamerUser[ID];
                    if (client.Status == User.UserStatus.Client)
                    {
                        var room = Online.Rooms.FirstOrDefault(x => x.RoomID == client.StreamRoom);
                        if (room != null)
                        {
                            room.Clients.Remove(this);
                            Online.Users.First(x => x.Value.Id == client.Id).Value.Status = User.UserStatus.StandBy;
                            var server = Online.StreamerUser.FirstOrDefault(x => x.Value.Id == room.RoomID).Value;
                            if (server != null)
                                server.WebSocket.Emit("Leave", new { from = client.Id });
                        }
                    }
                    else if (client.Status == User.UserStatus.Streaming)
                    {
                        RoomBoardCast(client.Id, "RoomVanish", new { roomID = client.Id });
                        BoardCast("RoomRemove", new { roomID = client.Id });
                        Online.Rooms.Remove(Online.Rooms.First(x => x.RoomID == client.Id));
                    }
                    Online.StreamerUser.Remove(ID);
                }
                RuntimeLog.WriteSystemLog("WebSocketServer", $"Client Disconnected, id={ID}", true);
            }
            public void Emit(string type, object msg)
            {
                Send((new { type, data = new { msg, timestamp = Helper.TimeStamp } }).ToJson());
            }
            public static void RoomBoardCast(int roomID, string type, object msg)
            {
                var room = Online.Rooms.Find(x => x.RoomID == roomID);
                try
                {
                    if (room != null)
                    {
                        RuntimeLog.WriteSystemLog("BoardCast", $"BoardCast start, type: {type}", true);
                        room.Clients.ForEach(x => x.Emit(type, msg));
                        int boardCastCount = room.ClientCount;
                        var server = Online.StreamerUser.First(x => x.Value.Id == roomID).Value;
                        if (server != null)
                        {
                            server.WebSocket.Emit(type, msg);
                            boardCastCount++;
                        }
                        RuntimeLog.WriteSystemLog("BoardCast", $"BoardCast success, total {boardCastCount} msgs", true);
                    }
                    else
                    {
                        RuntimeLog.WriteSystemLog("BoardCast", $"BoardCast error, room is null", false);
                    }
                }
                catch (Exception e)
                {
                    RuntimeLog.WriteSystemLog("BoardCast", $"BoardCast error, {e.Message}", false);
                }
            }
        }
        public static void HandleMessage(MsgHandler socket, string Data)
        {
            try
            {
                JObject json = JObject.Parse(Data);
                switch (json["type"].ToString())
                {
                    case "GetInfo":
                        GetInfo(socket, json["data"]);
                        break;
                    case "Login":
                        Login(socket, json["data"]);
                        break;
                    case "Register":
                        Register(socket, json["data"]);
                        break;
                    case "HeartBeat":
                        socket.Emit("HeartBeat", "##HEARTBEAT##");
                        break;
                    case "CreateRoom":
                        CreateRoom(socket, json["data"]);
                        break;
                    case "ChangeNickName":
                        ChangeNickName(socket, json["data"]);
                        break;
                    case "ChangeEmail":
                        ChangeEmail(socket, json["data"]);
                        break;
                    case "ChangePassword":
                        ChangePassword(socket, json["data"]);
                        break;
                    case "GetEmailCaptcha":
                        GetEmailCaptcha(socket, json["data"]);
                        break;
                    case "VerifyEmailCaptcha":
                        VerifyEmailCaptcha(socket, json["data"]);
                        break;
                    case "RoomEntered":
                        RoomEntered(socket, json["data"]);
                        break;
                    case "EnterRoom":
                        EnterRoom(socket, json["data"]);
                        break;
                    case "RoomList":
                        RoomList(socket, json["data"]);
                        break;
                    case "VerifyRoomPassword":
                        VerifyRoomPassword(socket, json["data"]);
                        break;
                    case "Offer":
                        OnOffer(socket, json["data"]);
                        break;
                    case "Answer":
                        OnAnswer(socket, json["data"]);
                        break;
                    case "Candidate":
                        OnCandidate(socket, json["data"]);
                        break;
                    case "Leave":
                        OnLeave(socket, json["data"]);
                        break;
                    default:
                        break;
                }
            }
            catch (Exception e)
            {
                socket.Emit("Error", Helper.SetError(401));
                RuntimeLog.WriteSystemLog("WebSocketServer", $"Msg Parse Error, Msg: {e.Message}", false);
            }
        }

        private static void EnterRoom(MsgHandler socket, JToken data)
        {
            string OnName = "EnterRoom";
            try
            {
                if (Online.Users.ContainsKey(socket.ID))
                {
                    var room = Online.Rooms.Find(x => x.RoomID == ((int)data["id"]));
                    if (room != null)
                    {
                        if (room.Password == data["password"].ToString().Trim())
                        {
                            if (room.Max > room.ClientCount)
                            {
                                socket.Emit(OnName, Helper.SetOK());
                                RuntimeLog.WriteSystemLog(OnName, $"{OnName} success", true);
                            }
                            else
                            {
                                socket.Emit(OnName, Helper.SetError(312));
                                RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, room is full", false);
                            }
                        }
                        else
                        {
                            socket.Emit(OnName, Helper.SetError(310));
                            RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, password is wrong", false);
                        }
                    }
                    else
                    {
                        socket.Emit(OnName, Helper.SetError(311));
                        RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, room is null", false);
                    }
                }
                else
                {
                    socket.Emit(OnName, Helper.SetError(307));
                    RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, user is null", false);
                }
            }
            catch (Exception e)
            {
                socket.Emit(OnName, Helper.SetError(-100));
                RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, {e.Message}", false);
            }
        }

        private static void VerifyRoomPassword(MsgHandler socket, JToken data)
        {
            string OnName = "VerifyRoomPassword";
            try
            {
                if (Online.Users.ContainsKey(socket.ID))
                {
                    var room = Online.Rooms.Find(x => x.RoomID == ((int)data["id"]));
                    if (room != null)
                    {
                        if (room.PasswordNeeded)
                        {
                            if (room.Password == data["password"].ToString().Trim())
                            {
                                socket.Emit(OnName, Helper.SetOK());
                                RuntimeLog.WriteSystemLog(OnName, $"{OnName} success, RoomID={data["id"]}, password={data["password"]}", true);
                            }
                            else
                            {
                                socket.Emit(OnName, Helper.SetError(310));
                                RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, password is incorrect", false);
                            }
                        }
                        else
                        {
                            socket.Emit(OnName, Helper.SetError(401));
                            RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, verify non password room", false);
                        }
                    }
                    else
                    {
                        socket.Emit(OnName, Helper.SetError(311));
                        RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, room is invalid", false);
                    }
                }
                else
                {
                    socket.Emit(OnName, Helper.SetError(307));
                    RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, user is null", false);
                }
            }
            catch (Exception e)
            {
                socket.Emit(OnName, Helper.SetError(-100));
                RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, {e.Message}", false);
            }
        }

        private static void RoomList(MsgHandler socket, JToken jToken)
        {
            string OnName = "RoomList";
            try
            {
                if (Online.Users.ContainsKey(socket.ID))
                {
                    socket.Emit(OnName, Helper.SetOK("ok", Online.Rooms.Where(x => x.IsPublic).ToList()));
                    RuntimeLog.WriteSystemLog(OnName, $"{OnName} success", true);
                }
                else
                {
                    socket.Emit(OnName, Helper.SetError(307));
                    RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, user is null", false);
                }
            }
            catch (Exception e)
            {
                socket.Emit(OnName, Helper.SetError(-100));
                RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, {e.Message}", false);
            }
        }

        private static void OnLeave(MsgHandler socket, JToken data)
        {
            string OnName = "Leave";
            var user = Online.StreamerUser[socket.ID];
            var server = Online.StreamerUser.FirstOrDefault(x => x.Value.Id == user.StreamRoom).Value;
            server.WebSocket.Emit(OnName, new { from = user.Id });
            RuntimeLog.WriteSystemLog(OnName, $"{OnName}, client {user.NickName} to server {server.NickName} leave", true);
        }

        private static void OnCandidate(MsgHandler socket, JToken data)
        {
            string OnName = "Candidate";
            var user = Online.StreamerUser[socket.ID];
            if (user.Status == User.UserStatus.Client)
            {
                var room = Online.Rooms.Find(x => x.RoomID == user.StreamRoom);
                if (room != null)
                {
                    var server = Online.StreamerUser.FirstOrDefault(x => x.Value.Id == user.StreamRoom).Value;
                    server.WebSocket.Emit(OnName, new { data = data["candidate"].ToObject<object>(), from = user.Id });
                    RuntimeLog.WriteSystemLog(OnName, $"{OnName}, client {user.NickName} to server {server.NickName} Candidate", true);
                }
                else
                {
                    System.Console.WriteLine("room is null");
                }
            }
            else if (user.Status == User.UserStatus.Streaming)
            {
                var client = Online.StreamerUser.FirstOrDefault(x => x.Value.Id == ((int)data["to"])).Value;
                client.WebSocket.Emit(OnName, new { data = data["candidate"].ToObject<object>(), from = user.Id });
                RuntimeLog.WriteSystemLog(OnName, $"{OnName}, server {user.NickName} to client {client.NickName} Candidate", true);
            }
        }

        private static void OnAnswer(MsgHandler socket, JToken data)
        {
            // Client => Server: createAnswer
            string OnName = "Answer";
            var user = Online.StreamerUser[socket.ID];
            var server = Online.StreamerUser.FirstOrDefault(x => x.Value.Id == user.StreamRoom).Value;
            server.WebSocket.Emit(OnName, new { data = data["answer"].ToObject<object>(), from = user.Id });
            RuntimeLog.WriteSystemLog(OnName, $"{OnName}, user {user.NickName} to server {server.NickName} answer", true);
        }

        private static void OnOffer(MsgHandler socket, JToken data)
        {
            // Client => Server: open request
            // Server => Client: createOffer
            string OnName = "Offer";
            var user = Online.StreamerUser[socket.ID];
            if (user.Status == User.UserStatus.Client)
            {
                var room = Online.Rooms.Find(x => x.RoomID == user.StreamRoom);
                if (room != null)
                {
                    // 拉流端发送offer请求，之后服务器将请求转发给推流端，并携带此用户的ID
                    var server = Online.StreamerUser.FirstOrDefault(x => x.Value.Id == room.RoomID).Value;
                    server.WebSocket.Emit(OnName, new { data = data["offer"].ToString(), from = user.Id });
                    RuntimeLog.WriteSystemLog(OnName, $"{OnName}, client {user.NickName} to server {server.NickName} offer", true);
                }
            }
            else if (user.Status == User.UserStatus.Streaming)
            {
                // 推流端应当在应答中添加ID
                var client = Online.StreamerUser.FirstOrDefault(x => x.Value.Id == ((int)data["to"])).Value;
                client.WebSocket.Emit(OnName, new { data = data["offer"].ToObject<object>(), from = user.Id });
                RuntimeLog.WriteSystemLog(OnName, $"{OnName}, server {user.NickName} to client {client.NickName} offer", true);
            }
        }

        private static void RoomEntered(MsgHandler socket, JToken data)
        {
            //TODO: 禁止自己进入自己房间
            string OnName = "RoomEntered";
            var user = Online.StreamerUser[socket.ID];
            var room = Online.Rooms.Find(x => x.RoomID == ((int)data["id"]));
            user.Status = User.UserStatus.Client;
            user.StreamRoom = room.RoomID;
            room.Clients.Add(socket);
            // Online.StreamerUser[socket.ID].StreamRoom = room.UserID;
            RuntimeLog.WriteSystemLog(OnName, $"{OnName}, user {user.NickName} enter room {room.RoomID}", true);
            socket.Emit(OnName, Helper.SetOK());
            // if (room != null)
            // {
            //     room.Clients.Add(socket);
            //     socket.Emit(OnName, Helper.SetOK());
            // } else {
            //     socket.Emit(OnName, Helper.SetError(311));
            // }
        }

        private static void ChangePassword(MsgHandler socket, JToken data)
        {
            string OnName = "ChangePassword";
            try
            {
                if (Online.Users.ContainsKey(socket.ID))
                {
                    var user = Online.Users[socket.ID];
                    if (user == null)
                    {
                        socket.Emit(OnName, Helper.SetError(307));
                        RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, user is null", false);
                    }
                    else
                    {
                        var r = User.ChangePassword(user.Id, data["oldPassword"].ToString(), data["newPassword"].ToString());
                        socket.Emit(OnName, r);
                        if (r.isSuccess)
                        {
                            RuntimeLog.WriteSystemLog(OnName, $"{OnName} success, id={user.Id} newPwd={data["newPassword"]}", true);
                        }
                        else
                        {
                            RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, msg={r.msg}", false);
                        }
                    }
                }
                else
                {
                    socket.Emit(OnName, Helper.SetError(307));
                    RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, user is null", false);
                }

            }
            catch (Exception ex)
            {
                socket.Emit("Register", Helper.SetError(401));
                RuntimeLog.WriteSystemLog("Register", $"Register error, msg: {ex.Message}", false);
            }
        }

        private static void ChangeEmail(MsgHandler socket, JToken data)
        {
            string OnName = "ChangeEmail";
            try
            {
                if (Online.Users.ContainsKey(socket.ID))
                {
                    var user = Online.Users[socket.ID];
                    if (!string.IsNullOrWhiteSpace(data["newEmail"]?.ToString()))
                    {
                        string email = data["newEmail"].ToString();
                        if (User.VerifyEmail(email, out bool formatError) is false)
                        {
                            User.UpdateEmailByID(user.Id, email);
                            socket.Emit(OnName, Helper.SetOK());
                            RuntimeLog.WriteSystemLog(OnName, $"{OnName} success, id={user.Id}, oldEmail={user.Email}, newEmail={email}", true);
                            user.Email = data["newEmail"].ToString();
                        }
                        else if (formatError)
                        {
                            socket.Emit(OnName, Helper.SetError(305));
                            RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, email={email} formatError", false);
                        }
                        else
                        {
                            socket.Emit(OnName, Helper.SetError(301));
                            RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, email={email} duplicate email", false);
                        }
                    }
                    else
                    {
                        socket.Emit(OnName, Helper.SetError(401));
                        RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, user is null", false);
                    }
                }
                else
                {
                    socket.Emit(OnName, Helper.SetError(307));
                    RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, user is null", false);
                }
            }
            catch (Exception ex)
            {
                socket.Emit("Register", Helper.SetError(-100));
                RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, msg: {ex.Message}", false);
            }
        }

        private static void Login(MsgHandler socket, JToken data)
        {
            try
            {
                var res = User.Login(data["account"].ToString(), data["password"].ToString());
                RuntimeLog.WriteSystemLog("Login", $"Try Login. Account: {data["account"]}, Pass: {data["password"]}", true);
                if (res.code == 200)
                {
                    User user = res.data as User;
                    user.PassWord = "***";
                    user.WebSocket = socket;
                    RuntimeLog.WriteUserLog(user.Email, "Login", "Login Success.", true);
                    if (Online.Users.ContainsKey(socket.ID))
                        Online.Users[socket.ID] = user;
                    else
                        Online.Users.Add(socket.ID, user);
                    socket.Emit("Login", Helper.SetOK("ok", Helper.GetJWT(user)));
                    return;
                }
                socket.Emit("Login", Helper.SetError(303));
                RuntimeLog.WriteSystemLog("Login", $"Login Fail. Account: {data["account"]}, Pass: {data["password"]}", false);
            }
            catch (Exception ex)
            {
                socket.Emit("Login", Helper.SetError(401));
                RuntimeLog.WriteSystemLog("Login", $"Login error, msg: {ex.Message}", false);
            }
        }
        private static void GetInfo(MsgHandler socket, JToken data)
        {
            string onName = "GetInfo";
            string resultName = "GetInfoResult";
            RuntimeLog.WriteSystemLog(onName, $"Receive {onName} Response, {data.ToString(Formatting.None)}", true);
            try
            {
                if (((bool)data["loginFlag"]))
                {
                    try
                    {
                        JObject json = JObject.Parse(Helper.ParseJWT(data["jwt"].ToString()));
                        RuntimeLog.WriteSystemLog(onName, $"{onName}, {data["jwt"]}", true);
                        User user = User.GetUserByID(((int)json["id"]));
                        if (user == null)
                        {
                            socket.Emit(resultName, Helper.SetError(503));
                            RuntimeLog.WriteSystemLog(onName, $"{onName} error, user is null", false);
                        }
                        else
                        {
                            if (user.LastChange != ((DateTime)json["statusChange"]))
                            {
                                socket.Emit(resultName, Helper.SetError(501));
                                RuntimeLog.WriteSystemLog(onName, $"{onName} error, lastChange not match", false);
                                return;
                            }
                            user.PassWord = "***";
                            user.WebSocket = socket;
                            socket.Emit(resultName, Helper.SetOK("ok", user));
                            if (data["streamFlag"] != null && (bool)data["streamFlag"])
                            {
                                if (Online.Users.Any(x => x.Value.Id == user.Id))
                                {
                                    user.Status = Online.Users.First(x => x.Value.Id == user.Id).Value.Status;
                                    if (!Online.StreamerUser.Any(x => x.Value.Id == user.Id))
                                        Online.StreamerUser.Add(socket.ID, user);
                                }
                                else
                                {
                                    Online.Users.Add(socket.ID, user);
                                }
                            }
                            else
                            {
                                Online.Users[socket.ID] = user;
                            }

                            RuntimeLog.WriteSystemLog(onName, $"{onName} success, user: id={user.Id}, name={user.NickName}", true);
                        }
                    }
                    catch (TokenExpiredException)
                    {
                        socket.Emit(resultName, Helper.SetError(501));
                        RuntimeLog.WriteSystemLog(onName, $"{onName} error, TokenExpired", false);
                    }
                    catch (SignatureVerificationException)
                    {
                        socket.Emit(resultName, Helper.SetError(502));
                        RuntimeLog.WriteSystemLog(onName, $"{onName} error, Signature Verification Fail", false);
                    }
                    catch (Exception ex)
                    {
                        socket.Emit(resultName, Helper.SetError(-100));
                        RuntimeLog.WriteSystemLog(onName, $"{onName} error, msg: {ex.Message}", false);
                    }
                }
                else
                {
                    socket.Emit(resultName, Helper.SetOK());
                }
            }
            catch (Exception ex)
            {
                socket.Emit(onName, Helper.SetError(401));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, msg: {ex.Message}", false);
            }
        }
        private static void Register(MsgHandler socket, JToken data)
        {
            try
            {
                var user = JsonConvert.DeserializeObject<User>(data.ToString());
                socket.Emit("Register", User.Register(user));
            }
            catch (Exception ex)
            {
                socket.Emit("Register", Helper.SetError(401));
                RuntimeLog.WriteSystemLog("Register", $"Register error, msg: {ex.Message}", false);
            }
        }
        private static void GetEmailCaptcha(MsgHandler socket, JToken data)
        {
            string OnName = "GetEmailCaptcha";
            try
            {
                string email = data["email"].ToString();
                if (Online.Captcha.ContainsKey(email))
                {
                    Online.Captcha.Remove(email);
                }
                int captcha = User.GenCaptcha();
                Online.Captcha.Add(email, captcha);
                int expiredTimeOut = 0;
                new Thread(() =>
                {
                    while (expiredTimeOut < 1000 * 30)
                    {
                        Thread.Sleep(1000);
                        expiredTimeOut += 1000;
                    }
                    if (Online.Captcha.ContainsKey(email))
                    {
                        Online.Captcha.Remove(email);
                        System.Console.WriteLine($"Remove captcha Email={email}");
                    }
                }).Start();
                RuntimeLog.WriteSystemLog(OnName, $"{OnName} Success, captcha={captcha}, Email={email}", true);
                socket.Emit(OnName, Helper.SetOK());
            }
            catch (Exception ex)
            {
                socket.Emit(OnName, Helper.SetError(-100));
                RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, msg: {ex.Message}", false);
            }
        }
        private static void VerifyEmailCaptcha(MsgHandler socket, JToken data)
        {
            string OnName = "VerifyEmailCaptcha";
            try
            {
                string email = data["email"].ToString();
                if (Online.Captcha.ContainsKey(email))
                {
                    if (Online.Captcha[email] == ((int)data["captcha"]))
                    {
                        Online.Captcha.Remove(email);
                        RuntimeLog.WriteSystemLog(OnName, $"{OnName} Success, Email={email}", true);
                        socket.Emit(OnName, Helper.SetOK());
                    }
                    else
                    {
                        socket.Emit(OnName, Helper.SetError(402));
                        RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, captcha is expired or invalid", false);
                    }
                }
                else
                {
                    socket.Emit(OnName, Helper.SetError(307));
                    RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, user is null", false);
                }
            }
            catch (Exception ex)
            {
                socket.Emit(OnName, Helper.SetError(-100));
                RuntimeLog.WriteSystemLog(OnName, $"{OnName} error, msg: {ex.Message}", false);
            }
        }
        private static void ChangeNickName(MsgHandler socket, JToken data)
        {
            string onName = "ChangeNickName";
            string newName = data["nickName"].ToString();
            try
            {
                var user = Online.Users[socket.ID];
                if (user != null)
                {
                    if (!User.VerifyNickName(newName, out bool formatError))
                    {
                        user.NickName = newName;
                        User.UpdateNickNameByID(user.Id, newName);
                        socket.Emit(onName, Helper.SetOK());
                        RuntimeLog.WriteSystemLog(onName, $"{onName} success, id={user.Id}, nickname={user.NickName}", true);
                    }
                    else
                    {
                        if (formatError)
                        {
                            socket.Emit(onName, Helper.SetError(309));
                            RuntimeLog.WriteSystemLog(onName, $"{onName} error, formatError, id={user.Id}, nickname={user.NickName}", false);
                        }
                        else
                        {
                            socket.Emit(onName, Helper.SetError(302));
                            RuntimeLog.WriteSystemLog(onName, $"{onName} error, duplicate nickname, id={user.Id}, nickname={user.NickName}", false);
                        }
                    }
                }
                else
                {
                    socket.Emit(onName, Helper.SetError(-100));
                    RuntimeLog.WriteSystemLog(onName, $"{onName} error, msg: user is null", false);
                }
            }
            catch (Exception ex)
            {
                socket.Emit(onName, Helper.SetError(-100));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, msg: {ex.Message}", false);
            }
        }
        private static void CreateRoom(MsgHandler socket, JToken data)
        {
            string onName = "CreateRoom";
            try
            {
                var user = Online.Users[socket.ID];
                if (user == null)
                {
                    RuntimeLog.WriteSystemLog(onName, $"{onName} fail, msg: not valid user", false);
                    socket.Emit(onName, Helper.SetError(307));
                    return;
                }
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
                };
                if (room.Max < 2 || room.Max > 51)
                {
                    RuntimeLog.WriteSystemLog(onName, $"{onName} fail, msg: invalid args", false);
                    socket.Emit(onName, Helper.SetError(401));
                    return;
                }
                user.Status = User.UserStatus.Streaming;
                RuntimeLog.WriteSystemLog(onName, $"{onName} success, userID: {room.RoomID}, password: {room.Password}, title: {room.Title}, max: {room.Max}", true);
                Online.Rooms.Add(room);
                socket.Emit(onName, Helper.SetOK());
                BoardCast("RoomAdd", room.WithoutSecret());
            }
            catch (Exception ex)
            {
                socket.Emit(onName, Helper.SetError(401));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, msg: {ex.Message}", false);
            }
        }
    }
}
