using System;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SqlSugar;
using StreamDanmuku_Server.SocketIO;
using static StreamDanmuku_Server.SocketIO.Server;


namespace StreamDanmuku_Server.Data
{
    [JsonObject(MemberSerialization.OptOut)]
    public class User : ICloneable
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [SugarColumn(IsIdentity = true, IsPrimaryKey = true)]
        public int Id { get; set; }
        /// <summary>
        /// 注册时邮箱
        /// </summary>
        public string Email { get; set; }
        /// <summary>
        /// 显示的昵称
        /// </summary>
        public string NickName { get; set; }
        /// <summary>
        /// MD5后密码
        /// </summary>
        public string PassWord { get; set; }
        /// <summary>
        /// 机密状态变更最后时间
        /// </summary>
        public DateTime LastChange { get; set; }
        /// <summary>
        /// 注册日期
        /// </summary>
        public DateTime CreateTime { get; set; }
        /// <summary>
        /// 加入的房间
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public int StreamRoom { get; set; }
        /// <summary>
        /// 用户状态
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public UserStatus Status { get; set; } = UserStatus.StandBy;
        [SugarColumn(IsIgnore = true)]
        [JsonIgnore]
        public MsgHandler WebSocket { get; set; }
        /// <summary>
        /// 加密通信使用的密钥，暂时搁置
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public string XorKey { get; set; }
        /// <summary>
        /// 用户当前状态
        /// </summary>
        public enum UserStatus
        {
            /// <summary>
            /// 直播中
            /// </summary>
            Streaming,
            /// <summary>
            /// 观看直播中
            /// </summary>
            Client,
            /// <summary>
            /// 在大厅
            /// </summary>
            StandBy,
            Banned,
            OffLine
        }

        #region SQL逻辑
        /// <summary>
        /// 保存当前状态
        /// </summary>
        /// <returns>sql执行结果</returns>
        public bool UpdateUser()
        {
            var db = SQLHelper.GetInstance();
            return db.Updateable(this).ExecuteCommand() == 1;
        }
        /// <summary>
        /// 按用户ID更新昵称
        /// </summary>
        /// <param name="id">用户ID</param>
        /// <param name="nickName">需更改的昵称</param>
        public static void UpdateNickNameByID(int id, string nickName)
        {
            var db = SQLHelper.GetInstance();
            db.Updateable<User>().Where(x => x.Id == id).SetColumns(x => new User() { NickName = nickName }).ExecuteCommand();
        }
        /// <summary>
        /// 按用户ID更新邮箱
        /// </summary>
        /// <param name="id">用户ID</param>
        /// <param name="Email">需更改的邮箱</param>
        public static void UpdateEmailByID(int id, string Email)
        {
            var db = SQLHelper.GetInstance();
            db.Updateable<User>().Where(x => x.Id == id).SetColumns(x => new User() { Email = Email }).ExecuteCommand();
        }
        /// <summary>
        /// 按用户
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static User GetUserByID(int id)
        {
            var db = SQLHelper.GetInstance();
            return db.Queryable<User>().Where(x => x.Id == id).First();
        }
        #endregion

        public static FunctionResult Login(string account, string password)
        {
            password = Helper.MD5Encrypt(password);
            var db = SQLHelper.GetInstance();
            var user = db.Queryable<User>().Where(x => (x.Email == account || x.NickName == account) && x.PassWord == password).First();
            if (user == null)
                return Helper.SetError(303);
            return Helper.SetOK("ok", user);
        }
        public static FunctionResult Register(User user) => Register(user.NickName, user.PassWord, user.Email);
        public static FunctionResult Register(string nickname, string password, string email)
        {
            var db = SQLHelper.GetInstance();
            if (VerifyEmail(email, out bool formatError))
                return Helper.SetError(formatError ? 305 : 301);
            if (VerifyNickName(nickname, out formatError))
                return Helper.SetError(formatError ? 306 : 302);
            if (password.Length != 32)
                return Helper.SetError(304);
            User u = new()
            {
                Email = email,
                PassWord = password.ToUpper(),
                NickName = nickname,
                CreateTime = DateTime.Now,
                LastChange = DateTime.Now,
            };
            db.Insertable(u).ExecuteCommand();
            return Helper.SetOK();
        }
        public static FunctionResult ChangePassword(int id, string oldPassword, string newPassword)
        {
            oldPassword = oldPassword.ToUpper();
            newPassword = newPassword.ToUpper();
            var db = SQLHelper.GetInstance();
            var user = db.Queryable<User>().First(x => x.Id == id);
            if (user == null)
            {
                return Helper.SetError(307);
            }
            else
            {
                if (user.PassWord.ToUpper() == oldPassword)
                {
                    user.LastChange = DateTime.Now;
                    user.PassWord = newPassword;
                    user.UpdateUser();
                    return Helper.SetOK();
                }
                else
                {
                    return Helper.SetError(310);
                }
            }
        }
        public static bool VerifyEmail(string Email, out bool formatError)
        {
            Email = Email.Trim();
            if (Regex.IsMatch(Email, "^[a-zA-Z0-9_-]+@[a-zA-Z0-9_-]+(.[a-zA-Z0-9_-]+)+$") is false) { formatError = true; return false; }
            formatError = false;
            return SQLHelper.GetInstance().Queryable<User>().Any(x => x.Email == Email);
        }
        public static bool VerifyNickName(string Nickname, out bool formatError)
        {
            Nickname = Nickname.Trim();
            if (Nickname.Length < 3) { formatError = true; return false; }
            formatError = false;
            return SQLHelper.GetInstance().Queryable<User>().Any(x => x.NickName == Nickname);
        }

        #region WebSocket逻辑
        /// <summary>
        /// 生成邮箱验证码
        /// </summary>
        /// <param name="socket">未登录 Websocket 连接</param>
        /// <param name="data">email: 目标邮箱</param>
        public static void GetEmailCaptcha(MsgHandler socket, JToken data)
        {
            const string onName = "GetEmailCaptcha";
            try
            {
                // TODO: 防范重启程序刷新时间
                string email = data["email"].ToString();
                if (Online.Captcha.ContainsKey(email))// 之前申请过，覆盖旧信息
                {
                    Online.Captcha.Remove(email);
                }
                int captcha = Helper.GenCaptcha();
                Online.Captcha.Add(email, captcha);
                int expiredTimeOut = 0;
                // TODO: 覆盖邮箱时如何停止这个线程
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
                RuntimeLog.WriteSystemLog(onName, $"{onName} Success, captcha={captcha}, Email={email}", true);
                socket.Emit(onName, Helper.SetOK());
            }
            catch (Exception ex)
            {
                socket.Emit(onName, Helper.SetError(-100));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, msg: {ex.Message}", false);
            }
        }
        /// <summary>
        /// 验证邮箱验证码
        /// </summary>
        /// <param name="socket">未登录 WebSocket 连接</param>
        /// <param name="data">email: 目标邮箱</param>
        public static void VerifyEmailCaptcha(MsgHandler socket, JToken data)
        {
            const string onName = "VerifyEmailCaptcha";
            try
            {
                string email = data["email"].ToString();
                if (Online.Captcha.ContainsKey(email))
                {
                    if (Online.Captcha[email] == (int)data["captcha"])// 类型验证？
                    {
                        Online.Captcha.Remove(email);
                        RuntimeLog.WriteSystemLog(onName, $"{onName} Success, Email={email}", true);
                        socket.Emit(onName, Helper.SetOK());
                    }
                    else
                    {
                        socket.Emit(onName, Helper.SetError(402));
                        RuntimeLog.WriteSystemLog(onName, $"{onName} error, captcha is expired or invalid", false);
                    }
                }
                else
                {
                    socket.Emit(onName, Helper.SetError(307));
                    RuntimeLog.WriteSystemLog(onName, $"{onName} error, user is null", false);
                }
            }
            catch (Exception ex)
            {
                socket.Emit(onName, Helper.SetError(-100));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, msg: {ex.Message}", false);
            }
        }
        /// <summary>
        /// 修改用户昵称
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data"></param>
        public static void ChangeNickName(MsgHandler socket, JToken data)
        {
            const string onName = "ChangeNickName";
            string newName = data["nickName"].ToString();
            try
            {
                var user = Online.Users[socket.ID];
                if (user != null)
                {
                    if (!VerifyNickName(newName, out bool formatError))
                    {
                        user.NickName = newName;
                        UpdateNickNameByID(user.Id, newName);
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
        /// <summary>
        /// 修改密码
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">oldPassword: 旧密码; newPassword: 新密码</param>
        public static void ChangePassword(MsgHandler socket, JToken data)
        {
            const string onName = "ChangePassword";
            try
            {
                if (Online.Users.ContainsKey(socket.ID))
                {
                    var user = Online.Users[socket.ID];
                    if (user == null)
                    {
                        socket.Emit(onName, Helper.SetError(307));
                        RuntimeLog.WriteSystemLog(onName, $"{onName} error, user is null", false);
                    }
                    else
                    {
                        var r = User.ChangePassword(user.Id, data["oldPassword"].ToString(), data["newPassword"].ToString());
                        socket.Emit(onName, r);
                        if (r.isSuccess)
                        {
                            RuntimeLog.WriteSystemLog(onName, $"{onName} success, id={user.Id} newPwd={data["newPassword"]}", true);
                        }
                        else
                        {
                            RuntimeLog.WriteSystemLog(onName, $"{onName} error, msg={r.msg}", false);
                        }
                    }
                }
                else
                {
                    socket.Emit(onName, Helper.SetError(307));
                    RuntimeLog.WriteSystemLog(onName, $"{onName} error, user is null", false);
                }

            }
            catch (Exception ex)
            {
                socket.Emit("Register", Helper.SetError(401));
                RuntimeLog.WriteSystemLog("Register", $"Register error, msg: {ex.Message}", false);
            }
        }

        public static void ChangeEmail(MsgHandler socket, JToken data)
        {
            const string onName = "ChangeEmail";
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
                            socket.Emit(onName, Helper.SetOK());
                            RuntimeLog.WriteSystemLog(onName, $"{onName} success, id={user.Id}, oldEmail={user.Email}, newEmail={email}", true);
                            user.Email = data["newEmail"].ToString();
                        }
                        else if (formatError)
                        {
                            socket.Emit(onName, Helper.SetError(305));
                            RuntimeLog.WriteSystemLog(onName, $"{onName} error, email={email} formatError", false);
                        }
                        else
                        {
                            socket.Emit(onName, Helper.SetError(301));
                            RuntimeLog.WriteSystemLog(onName, $"{onName} error, email={email} duplicate email", false);
                        }
                    }
                    else
                    {
                        socket.Emit(onName, Helper.SetError(401));
                        RuntimeLog.WriteSystemLog(onName, $"{onName} error, user is null", false);
                    }
                }
                else
                {
                    socket.Emit(onName, Helper.SetError(307));
                    RuntimeLog.WriteSystemLog(onName, $"{onName} error, user is null", false);
                }
            }
            catch (Exception ex)
            {
                socket.Emit("Register", Helper.SetError(-100));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, msg: {ex.Message}", false);
            }
        }

        #endregion

        public object Clone() => MemberwiseClone();
        public object WithoutSecret()
        {
            var c = (User)Clone();
            return new { c.Id, c.NickName, c.LastChange, c.CreateTime, c.Email, c.Status, c.XorKey };
        }
    }
}
