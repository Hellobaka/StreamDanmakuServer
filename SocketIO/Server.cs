using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp.Server;
using StreamDanmuku_Server.Data;
using WebSocketSharp;
using JWT.Exceptions;

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
                if (Online.Users.ContainsKey(ID))
                    Online.Users.Remove(ID);
                if (Online.StreamerUser.ContainsKey(ID))
                {
                    var client = Online.StreamerUser[ID];
                    RuntimeLog.WriteSystemLog("Room Removed", $"clientStatus={client.Status}", true);
                    if (client.Status == User.UserStatus.Client)
                    {
                        var room = Online.Rooms.FirstOrDefault(x => x.RoomID == client.StreamRoom);
                        if (room != null)
                        {
                            room.Clients.Remove(this);
                            Online.Users.First(x => x.Value.Id == client.Id).Value.Status = User.UserStatus.StandBy;
                            var server = Online.StreamerUser.FirstOrDefault(x => x.Value.Id == room.RoomID).Value;
                            if (server != null)
                                server.WebSocket.Emit("Leave", new {from = client.Id});
                        }
                    }
                    else if (client.Status == User.UserStatus.Streaming)
                    {
                        RuntimeLog.WriteSystemLog("Room Removed", $"RoomRemoved, id={client.Id}", true);
                        RoomBoardCast(client.Id, "RoomVanish", new {roomID = client.Id});
                        BoardCast("RoomRemove", new {roomID = client.Id});
                        Online.Rooms.Remove(Online.Rooms.First(x => x.RoomID == client.Id));
                        client.Status = User.UserStatus.StandBy;
                    }

                    Online.StreamerUser.Remove(ID);
                }

                BoardCast("OnlineUserChange", new {count = Online.Users.Count});
                RuntimeLog.WriteSystemLog("WebSocketServer", $"Client Disconnected, id={ID}", true);
            }

            public void Emit(string type, object msg)
            {
                Send((new {type, data = new {msg, timestamp = Helper.TimeStamp}}).ToJson());
            }

            public static void RoomBoardCast(int roomID, string type, object msg)
            {
                var room = Online.Rooms.Find(x => x.RoomID == roomID);
                try
                {
                    if (room != null)
                    {
                        room.Clients.ForEach(x => x.Emit(type, msg));
                        int boardCastCount = room.ClientCount;
                        var server = Online.StreamerUser.First(x => x.Value.Id == roomID).Value;
                        if (server != null)
                        {
                            server.WebSocket.Emit(type, msg);
                            boardCastCount++;
                        }
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
                        Room.CreateRoom(socket, json["data"]);
                        break;
                    case "ChangeNickName":
                        User.ChangeNickName(socket, json["data"]);
                        break;
                    case "ChangeEmail":
                        User.ChangeEmail(socket, json["data"]);
                        break;
                    case "ChangePassword":
                        User.ChangePassword(socket, json["data"]);
                        break;
                    case "GetEmailCaptcha":
                        User.GetEmailCaptcha(socket, json["data"]);
                        break;
                    case "VerifyEmailCaptcha":
                        User.VerifyEmailCaptcha(socket, json["data"]);
                        break;
                    case "RoomEntered":
                        Room.RoomEntered(socket, json["data"]);
                        break;
                    case "EnterRoom":
                        Room.EnterRoom(socket, json["data"]);
                        break;
                    case "RoomList":
                        Room.RoomList(socket, json["data"]);
                        break;
                    case "VerifyRoomPassword":
                        Room.VerifyRoomPassword(socket, json["data"]);
                        break;
                    case "Offer":
                        Room.OnOffer(socket, json["data"]);
                        break;
                    case "Answer":
                        Room.OnAnswer(socket, json["data"]);
                        break;
                    case "Candidate":
                        Room.OnCandidate(socket, json["data"]);
                        break;
                    case "Leave":
                        Room.OnLeave(socket, json["data"]);
                        break;
                    case "OnlineUserCount":
                        socket.Emit("OnlineUserCount", new {count = Online.Users.Count});
                        break;
                    case "JoinRoom":
                        Room.JoinRoom(socket, json["data"]);
                        break;
                    case "RoomInfo":
                        Room.RoomInfo(socket, json["data"]);
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

        private static void GetInfo(MsgHandler socket, JToken data)
        {
            string onName = "GetInfo";
            string resultName = "GetInfoResult";
            RuntimeLog.WriteSystemLog(onName, $"Receive {onName} Response, {data.ToString(Formatting.None)}", true);
            try
            {
                if (((bool) data["loginFlag"]))
                {
                    try
                    {
                        JObject json = JObject.Parse(Helper.ParseJWT(data["jwt"].ToString()));
                        RuntimeLog.WriteSystemLog(onName, $"{onName}, {data["jwt"]}", true);
                        User user = User.GetUserByID(((int) json["id"]));
                        if (user == null)
                        {
                            socket.Emit(resultName, Helper.SetError(503));
                            RuntimeLog.WriteSystemLog(onName, $"{onName} error, user is null", false);
                        }
                        else
                        {
                            if (user.LastChange != ((DateTime) json["statusChange"]))
                            {
                                socket.Emit(resultName, Helper.SetError(501));
                                RuntimeLog.WriteSystemLog(onName, $"{onName} error, lastChange not match", false);
                                return;
                            }

                            user.WebSocket = socket;
                            if (data["streamFlag"] != null && (bool) data["streamFlag"])
                            {
                                if (Online.Users.Any(x => x.Value.Id == user.Id))
                                {
                                    //user.Status = Online.Users.First(x => x.Value.Id == user.Id).Value.Status;
                                    user.Status = User.UserStatus.Streaming;
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

                            socket.Emit(resultName, Helper.SetOK("ok", user.WithoutSecret()));
                            RuntimeLog.WriteSystemLog(onName,
                                $"{onName} success, user: id={user.Id}, name={user.NickName}", true);
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
            finally
            {
                BoardCast("OnlineUserChange", new {count = Online.Users.Count});
            }
        }

        private static void Login(MsgHandler socket, JToken data)
        {
            try
            {
                var res = User.Login(data["account"].ToString(), data["password"].ToString());
                RuntimeLog.WriteSystemLog("Login", $"Try Login. Account: {data["account"]}, Pass: {data["password"]}",
                    true);
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
                RuntimeLog.WriteSystemLog("Login", $"Login Fail. Account: {data["account"]}, Pass: {data["password"]}",
                    false);
            }
            catch (Exception ex)
            {
                socket.Emit("Login", Helper.SetError(401));
                RuntimeLog.WriteSystemLog("Login", $"Login error, msg: {ex.Message}", false);
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
    }
}