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
                Send((new {type, data = new {msg, timestamp = Helper.TimeStampms}}).ToJson());
            }

            private static void RoomBoardCast(int roomID, string type, object msg)
            {
                var room = Online.Rooms.Find(x => x.RoomID == roomID);
                try
                {
                    if (room != null)
                    {
                        room.Clients.ForEach(x => x.Emit(type, msg));
                        var server = Online.StreamerUser.First(x => x.Value.Id == roomID).Value;
                        server?.WebSocket.Emit(type, msg);
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

        private static void HandleMessage(MsgHandler socket, string jsonText)
        {
            try
            {
                JObject json = JObject.Parse(jsonText);
                JToken data = json["data"];
                switch (json["type"]!.ToString())
                {
                    case "GetInfo":
                        GetInfo(socket, json["data"]);
                        break;
                    case "Login":
                        Auth_Non(socket, data, User.Login);
                        break;
                    case "Register":
                        Auth_Non(socket, data, User.Register);
                        break;
                    case "HeartBeat":
                        socket.Emit("HeartBeat", "##HEARTBEAT##");
                        break;
                    case "CreateRoom":
                        Auth_Online(socket, data, Room.CreateRoom);
                        break;
                    case "ChangeNickName":
                        Auth_Online(socket, data, User.ChangeNickName);
                        break;
                    case "ChangeEmail":
                        Auth_Online(socket, data, User.ChangeEmail);
                        break;
                    case "ChangePassword":
                        Auth_Online(socket, data, User.ChangePassword);
                        break;
                    case "GetEmailCaptcha":
                        Auth_Non(socket, data, User.GetEmailCaptcha);
                        break;
                    case "VerifyEmailCaptcha":
                        Auth_Non(socket, data, User.VerifyEmailCaptcha);
                        break;
                    case "RoomEntered":
                        Auth_Stream(socket, data, Room.RoomEntered);
                        break;
                    case "EnterRoom":
                        Auth_Online(socket, data, Room.EnterRoom);
                        break;
                    case "RoomList":
                        Auth_Online(socket, data, Room.RoomList);
                        break;
                    case "VerifyRoomPassword":
                        Auth_Online(socket, data, Room.VerifyRoomPassword);
                        break;
                    case "Offer":
                        Auth_Stream(socket, data, Room.OnOffer);
                        break;
                    case "Answer":
                        Auth_Stream(socket, data, Room.OnAnswer);
                        break;
                    case "Candidate":
                        Auth_Stream(socket, data, Room.OnCandidate);
                        break;
                    case "Leave":
                        Auth_Stream(socket, data, Room.OnLeave);
                        break;
                    case "OnlineUserCount":
                        socket.Emit("OnlineUserCount", new {count = Online.Users.Count});
                        break;
                    case "JoinRoom":
                        Auth_Online(socket, data, Room.JoinRoom);
                        break;
                    case "RoomInfo":
                        Auth_Stream(socket, data, Room.RoomInfo);
                        break;
                    case "GetPushUrl":
                        Auth_Stream(socket, data, Room.GetPushUrl);
                        break;
                    case "GetPullUrl":
                        Auth_Stream(socket, data, Room.GetPullUrl);
                        break;
                    case "SwitchStream":
                        Auth_Stream(socket, data, Room.SwitchStream);
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

        private static void Auth_Stream(MsgHandler socket, JToken data, Action<MsgHandler, JToken, string, User> func)
        {
            string onName = func.Method.Name;
            try
            {
                if (Online.StreamerUser.ContainsKey(socket.ID))
                {
                    var stream = Online.StreamerUser[socket.ID];
                    func.Invoke(socket, data, onName, stream);
                }
                else
                {
                    socket.Emit(onName, Helper.SetError(307));
                    RuntimeLog.WriteSystemLog(onName, $"{onName} error, stream is null", false);
                }
            }
            catch (Exception e)
            {
                socket.Emit(onName, Helper.SetError(-100));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, {e.Message}", false);
            }
        }
        private static void Auth_Online(MsgHandler socket, JToken data, Action<MsgHandler, JToken, string, User> func)
        {            
            string onName = func.Method.Name;
            try
            {
                if (Online.Users.ContainsKey(socket.ID))
                {
                    var user = Online.Users[socket.ID];
                    func.Invoke(socket, data, onName, user);
                }
                else
                {
                    socket.Emit(onName, Helper.SetError(307));
                    RuntimeLog.WriteSystemLog(onName, $"{onName} error, msg: user is null", false);
                }
            }
            catch (Exception e)
            {
                socket.Emit(onName, Helper.SetError(-100));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, {e.Message}", false);
            }
        }

        private static void Auth_Non(MsgHandler socket, JToken data, Action<MsgHandler, JToken, string> func)
        {
            string onName = func.Method.Name;
            try
            {
                func.Invoke(socket, data, onName);
            }
            catch (Exception e)
            {
                socket.Emit(onName, Helper.SetError(-100));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, {e.Message}", false);
            }
        }
        private static void GetInfo(MsgHandler socket, JToken data)
        {
            const string onName = "GetInfo";
            const string resultName = "GetInfoResult";
            RuntimeLog.WriteSystemLog(onName, $"Receive {onName} Response, {data.ToString(Formatting.None)}", true);
            try
            {
                if ((bool) data["loginFlag"])
                {
                    try
                    {
                        JObject json = JObject.Parse(Helper.ParseJWT(data["jwt"].ToString()));
                        RuntimeLog.WriteSystemLog(onName, $"{onName}, {data["jwt"]}", true);
                        User user = User.GetUserByID((int) json["id"]);
                        if (user == null)
                        {
                            socket.Emit(resultName, Helper.SetError(503));
                            RuntimeLog.WriteSystemLog(onName, $"{onName} error, user is null", false);
                        }
                        else
                        {
                            if (user.LastChange != (DateTime) json["statusChange"])
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
                                    if (Online.StreamerUser.All(x => x.Value.Id != user.Id))
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
    }
}