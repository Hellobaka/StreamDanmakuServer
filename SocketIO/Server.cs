using System;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebSocketSharp.Server;
using StreamDanmuku_Server.Data;
using WebSocketSharp;
using JWT.Exceptions;
using StreamDanmuku_Server.Enum;

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
                item.WebSocket.Emit(type, msg);
            }
        }

        public class MsgHandler : WebSocketBehavior
        {
            public User CurrentUser { get; set; } = null;

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
                if (Online.Users.Contains(CurrentUser))
                {
                    Online.Users.Remove(CurrentUser);
                    if (CurrentUser.CurrentRoom != null)
                    {
                        switch (CurrentUser.Status)
                        {
                            case UserStatus.Streaming:
                                RuntimeLog.WriteSystemLog("Room Removed", $"RoomRemoved, id={CurrentUser.Id}", true);
                                CurrentUser.CurrentRoom.RoomBoardCast("RoomVanish", new { roomID = CurrentUser.Id });
                                BoardCast("RoomRemove", new { roomID = CurrentUser.Id });
                                Online.Rooms.Remove(CurrentUser.CurrentRoom);
                                break;
                            case UserStatus.Client:
                                CurrentUser.CurrentRoom.Clients.Remove(this);
                                CurrentUser.CurrentRoom.RoomBoardCast("OnLeave", new {from = CurrentUser.Id});
                                break;
                        }
                    }
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
                        room.Server?.WebSocket.Emit(type, msg);
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
                    case "SendDanmuku":
                        Auth_Stream(socket, data, Room.Danmuku);
                        break;
                    case "GetRoomDanmuku":
                        Auth_Stream(socket, data, Room.GetRoomDanmuku);
                        break;
                    default:
                        break;
                }
            }
            catch (Exception e)
            {
                socket.Emit("Error", Helper.SetError(ErrorCode.ParamsFormatError));
                RuntimeLog.WriteSystemLog("WebSocketServer", $"Msg Parse Error, Msg: {e.Message}", false);
            }
        }

        private static void Auth_Stream(MsgHandler socket, JToken data, Action<MsgHandler, JToken, string, User> func)
        {
            Auth_Online(socket, data, func);
        }
        private static void Auth_Online(MsgHandler socket, JToken data, Action<MsgHandler, JToken, string, User> func)
        {            
            string onName = func.Method.Name;
            try
            {
                if (socket.CurrentUser!=null)
                {
                    func.Invoke(socket, data, onName, socket.CurrentUser);
                }
                else
                {
                    socket.Emit(onName, Helper.SetError(ErrorCode.InvalidUser));
                    RuntimeLog.WriteSystemLog(onName, $"{onName} error, msg: user is null", false);
                }
            }
            catch (Exception e)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.UnknownError));
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
                socket.Emit(onName, Helper.SetError(ErrorCode.UnknownError));
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
                        User user = User.GetUserByID((int)json["id"]);
                        if (user == null)
                        {
                            socket.Emit(resultName, Helper.SetError(ErrorCode.InvalidUser));
                            RuntimeLog.WriteSystemLog(onName, $"{onName} error, user is null", false);
                        }
                        else
                        {
                            socket.CurrentUser = user;
                            if (user.LastChange != (DateTime) json["statusChange"])
                            {
                                socket.Emit(resultName, Helper.SetError(ErrorCode.TokenExpired));
                                RuntimeLog.WriteSystemLog(onName, $"{onName} error, lastChange not match", false);
                                return;
                            }

                            if (Online.Users.Contains(user) is false)
                            {
                                Online.Users.Add(user);
                            }
                            user.WebSocket = socket;
                            // 保留
                            Thread.Sleep(300);
                            socket.Emit(resultName, Helper.SetOK("ok", user.WithoutSecret()));
                            RuntimeLog.WriteSystemLog(onName,
                                $"{onName} success, user: id={user.Id}, name={user.NickName}", true);
                        }
                    }
                    catch (TokenExpiredException)
                    {
                        socket.Emit(resultName, Helper.SetError(ErrorCode.TokenExpired));
                        RuntimeLog.WriteSystemLog(onName, $"{onName} error, TokenExpired", false);
                    }
                    catch (SignatureVerificationException)
                    {
                        socket.Emit(resultName, Helper.SetError(ErrorCode.SignInvalid));
                        RuntimeLog.WriteSystemLog(onName, $"{onName} error, Signature Verification Fail", false);
                    }
                    catch (Exception ex)
                    {
                        socket.Emit(resultName, Helper.SetError(ErrorCode.UnknownError));
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
                socket.Emit(onName, Helper.SetError(ErrorCode.ParamsFormatError));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, msg: {ex.Message}", false);
            }
            finally
            {
                BoardCast("OnlineUserChange", new {count = Online.Users.Count});
            }
        }
    }
}