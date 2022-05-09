using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using JWT.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreamDanmaku_Server.Data;
using StreamDanmaku_Server.Enum;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace StreamDanmaku_Server.SocketIO
{
    /// <summary>
    /// WebSocket 连接器
    /// </summary>
    public class Server
    {
        /// <summary>
        /// WebSocket 实例
        /// </summary>
        private readonly WebSocketServer _instance;

        /// <summary>
        /// WebSocket 监听端口
        /// </summary>
        private readonly ushort _port;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="port">监听端口</param>
        public Server(ushort port)
        {
            _port = port;
            _instance = new WebSocketServer(_port);
            _instance.AddWebSocketService<MsgHandler>("/main");
        }

        /// <summary>
        /// 开启端口监听
        /// </summary>
        public void StartServer()
        {
            _instance.Start();
            Console.WriteLine($"WebSocket URL: ws://127.0.0.1:{_port}/main");
            RuntimeLog.WriteSystemLog("WebSocketServer", $"WebSocket服务器开启 监听端口: {_port}...", true);
        }

        /// <summary>
        /// 停止端口监听
        /// </summary>
        public void StopServer()
        {
            _instance.Stop();
            RuntimeLog.WriteSystemLog("WebSocketServer", "WebSocket服务器关闭...", true);
        }

        /// <summary>
        /// 服务器级广播
        /// </summary>
        /// <param name="type">广播类型</param>
        /// <param name="msg">广播内容</param>
        public static void BoardCast(string type, object msg)
        {
            // 向在线用户广播
            foreach (var item in Online.Users)
            {
                item.WebSocket.Emit(type, msg);
            }

            // 向在线后台广播
            foreach (var item in Online.Admins)
            {
                item.Emit(type, msg);
            }
        }

        /// <summary>
        /// 消息处理端
        /// </summary>
        public class MsgHandler : WebSocketBehavior
        {
            /// <summary>
            /// 连接中已登录用户
            /// </summary>
            public User CurrentUser { get; set; }

            /// <summary>
            /// 连接中已登录用户类型, 默认为拉流端
            /// </summary>
            public UserType UserType { get; set; } = UserType.Client;

            /// <summary>
            /// 连接对方IP
            /// </summary>
            public IPAddress ClientIP { get; set; }

            /// <summary>
            /// 后台连接是否已授权
            /// </summary>
            public bool Authed { get; set; }

            /// <summary>
            /// 后台连接监视弹幕房间
            /// </summary>
            public Room MonitoredDanmaku { get; set; }

            /// <summary>
            /// 触发消息
            /// </summary>
            /// <param name="e">消息事件</param>
            protected override void OnMessage(MessageEventArgs e)
            {
                HandleMessage(this, e.Data);
            }

            /// <summary>
            /// 连接建立
            /// </summary>
            protected override void OnOpen()
            {
                ClientIP = Context.UserEndPoint.Address;
                RuntimeLog.WriteSystemLog("WebSocketServer", $"连接已建立, id={ID}, ip={ClientIP}", true);
            }

            /// <summary>
            /// 连接断开 认为是异常断开 包含房间销毁判断
            /// </summary>
            /// <param name="e">断开事件</param>
            protected override void OnClose(CloseEventArgs e)
            {
                // 从在线列表移除后台连接
                if (Online.Admins.Contains(this)) Online.Admins.Remove(this);
                if (Online.Users.Contains(CurrentUser))
                {
                    // 从在线列表移除此用户
                    Online.Users.Remove(CurrentUser);
                    // 如果用户在某个直播间内, 对房间内用户数量判断, 整个房间为空则销毁
                    if (CurrentUser.CurrentRoom != null)
                    {
                        switch (CurrentUser.Status)
                        {
                            case UserStatus.Streaming:
                                CurrentUser.CurrentRoom.Server = null;
                                // 主播断线
                                CurrentUser.CurrentRoom.RoomBoardCast("StreamerOffline", new {from = CurrentUser.Id});
                                break;
                            case UserStatus.Client:
                                CurrentUser.CurrentRoom.Clients.Remove(this);
                                CurrentUser.CurrentRoom.RoomBoardCast("OnLeave", new {from = CurrentUser.Id});
                                break;
                        }

                        // 如果房间为空则销毁房间
                        if (CurrentUser.CurrentRoom.Clients.Count == 0 && CurrentUser.CurrentRoom.Server == null)
                        {
                            RuntimeLog.WriteSystemLog("RoomRemoved", $"房间销毁, id={CurrentUser.Id}", true);
                            CurrentUser.CurrentRoom.RoomBoardCast("RoomVanish", new {roomID = CurrentUser.Id});
                            BoardCast("RoomRemove", new {roomID = CurrentUser.Id});
                            Online.Rooms.Remove(CurrentUser.CurrentRoom);
                        }
                    }
                }

                // 广播在线人数变化
                BoardCast("OnlineUserChange", new {count = Online.Users.Count});
                RuntimeLog.WriteSystemLog("WebSocketServer", $"连接断开, id={ID}", true);
            }

            /// <summary>
            /// 发送消息 包含时间戳 可用于服务器延时
            /// </summary>
            /// <param name="type">消息类型</param>
            /// <param name="msg">消息内容</param>
            public void Emit(string type, object msg)
            {
                Send((new {type, data = new {msg, timestamp = Helper.TimeStampms}}).ToJson());
            }
            public void CloseConnection()
            {
                Context.WebSocket.Close(CloseStatusCode.Normal, "Reconnect.");
            }
        }

        /// <summary>
        /// 分发消息
        /// </summary>
        /// <param name="socket">WebSocket连接</param>
        /// <param name="jsonText">消息内容</param>
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
                        Auth_Non(socket, data, User.ChangePassword);
                        break;
                    case "ChangePasswordOnline":
                        Auth_Online(socket, data, User.ChangePasswordOnline);
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
                    case "GetPullUrl_Admin":
                        Auth_Admin(socket, data, Room.GetPullUrl_Admin);
                        break;
                    case "SwitchStream":
                        Auth_Stream(socket, data, Room.SwitchStream);
                        break;
                    case "SendDanmaku":
                        Auth_Stream(socket, data, Room.Danmaku);
                        break;
                    case "GetRoomDanmaku":
                        Auth_Stream(socket, data, Room.GetRoomDanmaku);
                        break;
                    case "ResumeRoom":
                        Auth_Online(socket, data, Room.ResumeRoom);
                        break;
                    case "UploadCapture":
                        Auth_Stream(socket, data, Room.UploadCapture);
                        break;
                    case "GetCaptures":
                        Auth_Admin(socket, data, Room.GetCaptures);
                        break;
                    case "StopStream_Admin":
                        Auth_Admin(socket, data, Room.StopStream_Admin);
                        break;
                    case "GetDanmaku_Admin":
                        Auth_Admin(socket, data, Room.GetDanmaku_Admin);
                        break;
                    case "RemoveMonitor_Admin":
                        Auth_Admin(socket, data, Room.RemoveMonitor_Admin);
                        break;
                    case "GetRoom_Admin":
                        Auth_Admin(socket, data, Room.GetRoom_Admin);
                        break;
                    case "GetUsers_Admin":
                        Auth_Admin(socket, data, User.GetUsers_Admin);
                        break;
                    case "ToggleSilent_Admin":
                        Auth_Admin(socket, data, User.ToggleSilent_Admin);
                        break;
                    case "ToggleStream_Admin":
                        Auth_Admin(socket, data, User.ToggleStream_Admin);
                        break;
                    case "EditUser_Admin":
                        Auth_Admin(socket, data, User.EditUser_Admin);
                        break;
                    case "GetLogs_Admin":
                        Auth_Admin(socket, data, GetLogs_Admin);
                        break;
                    case "SendDanmaku_Admin":
                        Auth_Admin(socket, data, Room.SendDanmaku_Admin);
                        break;
                    case "VerifyTXCaptcha":
                        Auth_Non(socket, data, User.VerifyTXCaptcha);
                        break;
                    case "CanCallCapture":
                        Auth_Non(socket, data, User.CanCallCapture);
                        break;
                    case "MuteUser":
                        Auth_Stream(socket, data, User.MuteUser);
                        break;
                    case "GetMuteList":
                        Auth_Stream(socket, data, User.GetMuteList);
                        break;
                    case "GetFriendList":
                        Auth_Online(socket, data, User.GetFriendList);
                        break;
                    case "RemoveFriend":
                        Auth_Online(socket, data, User.RemoveFriend);
                        break;
                    case "FindFriend":
                        Auth_Online(socket, data, User.FindFriend);
                        break;
                    case "QueryFriendRoom":
                        Auth_Online(socket, data, User.QueryFriendRoom);
                        break;
                    case "GetFriendRequestCount":
                        Auth_Online(socket, data, FriendRequest.GetFriendRequestCount);
                        break;
                    case "CreateFriendRequest":
                        Auth_Online(socket, data, FriendRequest.CreateFriendRequest);
                        break;
                    case "HandleFriendRequest":
                        Auth_Online(socket, data, FriendRequest.HandleFriendRequest);
                        break;
                    case "GetFriendRequestList":
                        Auth_Online(socket, data, FriendRequest.GetFriendRequestList);
                        break;
                    case "logout":
                        Logout(socket);
                        break;
                }
            }
            catch (Exception e)
            {
                socket.Emit("Error", Helper.SetError(ErrorCode.ParamsFormatError));
                RuntimeLog.WriteSystemLog("WebSocketServer", $"消息解析错误, 内容={e.Message}", false);
            }
        }

        private static void Logout(MsgHandler socket)
        {
            socket.CloseConnection();
        }

        /// <summary>
        /// 后台获取日志
        /// </summary>
        /// <param name="socket">后台 WebSocket 连接</param>
        /// <param name="data">日志筛选选项</param>
        /// <param name="onName">操作名称</param>
        private static void GetLogs_Admin(MsgHandler socket, JToken data, string onName)
        {
            using var db = SQLHelper.GetInstance();
            string search = data["search"].ToString(); // 粗匹配
            string logType = data["logType"]?.ToString(); // 日志操作类型匹配
            string userSearch = data["userSearch"]?.ToString(); // 筛选用户
            bool showSystemLog = (bool) data["showSystemLog"]; // 是否显示系统日志
            int pageSize = (int) data["itemsPerPage"]; // 每页多少条 -1则是显示所有日志
            if (pageSize == -1)
            {
                pageSize = int.MaxValue;
            }

            int pageIndex = (int) data["page"]; // 第多少页
            var arr = data["sortBy"] as JArray; // 排序选项, 内含排序的列名称
            string orderBy = string.Empty;
            bool orderByDesc = false;
            if (arr != null && arr.Count != 0)
            {
                orderBy = arr[0].ToString();
                orderByDesc = (bool) (data["sortDesc"] as JArray)![0];// 是否降序, 第一项为bool表示是否降序
            }

            List<DateTime> date = new();
            if (data["date"] is JArray array)
            {
                foreach (var item in array)// 日志时间筛选
                {
                    date.Add(DateTime.Parse(item.ToString()));
                }
            }
            // 按照上面的条件进行筛选
            List<RuntimeLog> r = db.Queryable<RuntimeLog>()
                .WhereIF(!string.IsNullOrWhiteSpace(logType), x => x.ActionName == logType)
                .WhereIF(!string.IsNullOrWhiteSpace(userSearch), x => x.Account == userSearch)
                .Where(x => showSystemLog || x.Account != "System")
                .WhereIF(date.Count != 0, x => x.Time >= date[0] && x.Time <= date[1].AddDays(1))
                .CustomOrderBy(orderBy, orderByDesc).ToList();
            // 假如粗匹配内容不为空, 则进行进一步过滤
            if (!string.IsNullOrWhiteSpace(search))
            {
                r = r.Where(x =>
                    x.RowID.ToString().Contains(search) || x.Account.Contains(search) ||
                    x.ActionName.Contains(search) || x.Action.Contains(search)).ToList();
            }
            // 按照前端要求发送数组与数组长度
            socket.Emit(onName,
                Helper.SetOK(new {data = r.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList(), count = r.Count}));
        }
        /// <summary>
        /// 直播时才能调用的方法
        /// </summary>
        /// <param name="socket">直播 WebSocket 连接</param>
        /// <param name="data">参数</param>
        /// <param name="func">想要调用的函数</param>
        private static void Auth_Stream(MsgHandler socket, JToken data, Action<MsgHandler, JToken, string, User> func)
        {
            Auth_Online(socket, data, func);
        }
        /// <summary>
        /// 在线就能调用的方法
        /// </summary>
        /// <param name="socket">在线 WebSocket 连接</param>
        /// <param name="data">参数</param>
        /// <param name="func">想要调用的函数</param>
        private static void Auth_Online(MsgHandler socket, JToken data, Action<MsgHandler, JToken, string, User> func)
        {
            string onName = func.Method.Name;
            try
            {
                if (socket.CurrentUser != null)
                {
                    func.Invoke(socket, data, onName, socket.CurrentUser);
                }
                else
                {
                    socket.Emit(onName, Helper.SetError(ErrorCode.InvalidUser));
                    RuntimeLog.WriteSystemLog(onName, "函数调用发生错误, 用户无效", false);
                }
            }
            catch (Exception e)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.UnknownError));
                RuntimeLog.WriteSystemLog(onName, $"函数调用发生错误, {e.Message}, {e.StackTrace}", false);
            }
        }
        /// <summary>
        /// 只有后台连接才能调用的方法
        /// </summary>
        /// <param name="socket">后台 WebSocket 连接</param>
        /// <param name="data">参数</param>
        /// <param name="func">想要调用的函数</param>
        private static void Auth_Admin(MsgHandler socket, JToken data, Action<MsgHandler, JToken, string> func)
        {
            string onName = func.Method.Name;
            if (socket.UserType != UserType.Admin)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.InvalidUser));
                RuntimeLog.WriteSystemLog(onName, "函数调用发生错误, 用户不是后台连接", false);
                return;
            }

            if (socket.Authed is false)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.NoAuth));
                RuntimeLog.WriteSystemLog(onName, "函数调用发生错误, 此连接未经过授权", false);
                return;
            }

            Auth_Non(socket, data, func);
        }
        /// <summary>
        /// 连接均可
        /// </summary>
        /// <param name="socket">普通 WebSocket 连接</param>
        /// <param name="data">参数</param>
        /// <param name="func">想要调用的函数</param>
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
                RuntimeLog.WriteSystemLog(onName, $"函数调用发生错误, {e.Message}, {e.StackTrace}", false);
            }
        }
        /// <summary>
        /// 进行连接授权
        /// </summary>
        /// <param name="socket">WebSocket 连接</param>
        /// <param name="data">内容</param>
        private static void GetInfo(MsgHandler socket, JToken data)
        {
            const string onName = "GetInfo";
            const string resultName = "GetInfoResult";
            RuntimeLog.WriteSystemLog(onName, $"进行连接授权, jwt={data.ToString(Formatting.None)}", true);
            try
            {
                string jwt = data["jwt"]?.ToString();
                switch (data["type"].ToString())
                {
                    case "admin":// 后台连接
                        socket.UserType = UserType.Admin;
                        if (!string.IsNullOrWhiteSpace(jwt))
                        {
                            try
                            {
                                Helper.ParseJWT(jwt);
                                socket.Authed = true;
                                if (!Online.Admins.Contains(socket)) Online.Admins.Add(socket);
                            }
                            catch
                            {
                                socket.Authed = false;
                                // jwt解析失败
                            }
                        }

                        socket.Emit(resultName, Helper.SetOK());
                        break;
                    case "client":// 客户端
                        try
                        {
                            if (string.IsNullOrWhiteSpace(jwt))
                            {
                                socket.Emit(resultName, Helper.SetOK());
                                return;
                            }
                            JObject json = JObject.Parse(Helper.ParseJWT(jwt));
                            User user = User.GetUserByID((int) json["id"]);
                            if (user == null)
                            {
                                socket.Emit(resultName, Helper.SetError(ErrorCode.InvalidUser));
                                RuntimeLog.WriteSystemLog(onName, $"jwt解析失败, 无法获取用户信息", false);
                            }
                            else// 如果jwt解析通过则可获取到用户ID
                            {
                                socket.CurrentUser = user;
                                if (user.LastChange != (DateTime) json["statusChange"])// 机密更改时间变化, 请求重新登录
                                {
                                    socket.Emit(resultName, Helper.SetError(ErrorCode.TokenExpired));
                                    RuntimeLog.WriteSystemLog(onName, $"机密变更时间不符, 请求重新登录", false);
                                    return;
                                }

                                if (Online.Users.Contains(user) is false)
                                {
                                    Online.Users.Add(user);
                                }

                                user.WebSocket = socket;
                                // 保留
                                Thread.Sleep(300);
                                socket.Emit(resultName, Helper.SetOK(user.WithoutSecret()));
                                RuntimeLog.WriteSystemLog(onName,
                                    $"连接授权成功, id={user.Id}, 昵称={user.NickName}", true);
                            }
                        }
                        catch (TokenExpiredException)// Token过期
                        {
                            socket.Emit(resultName, Helper.SetError(ErrorCode.TokenExpired));
                            RuntimeLog.WriteSystemLog(onName, $"连接授权失败, Token已过期", false);
                        }
                        catch (SignatureVerificationException)// 签名验证失败
                        {
                            socket.Emit(resultName, Helper.SetError(ErrorCode.SignInvalid));
                            RuntimeLog.WriteSystemLog(onName, $"连接授权失败, 签名验证失败", false);
                        }
                        catch (Exception ex)
                        {
                            socket.Emit(resultName, Helper.SetError(ErrorCode.UnknownError));
                            RuntimeLog.WriteSystemLog(onName, $"连接授权失败, {ex.Message}", false);
                        }

                        break;
                }
            }
            catch (Exception ex)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.ParamsFormatError));
                RuntimeLog.WriteSystemLog(onName, $"连接授权失败, {ex.Message}", false);
            }
            finally
            {
                BoardCast("OnlineUserChange", new {count = Online.Users.Count});
            }
        }
    }
}