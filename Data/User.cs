using System;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SqlSugar;
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

        #region 工具函数

        /// <summary>
        /// 验证邮箱格式是否正确
        /// </summary>
        /// <param name="email">待验证邮箱</param>
        /// <param name="formatError">是否格式错误</param>
        /// <param name="duplicate">是否重复</param>
        /// <returns>是否通过</returns>
        public static bool VerifyEmail(string email, out bool formatError, out bool duplicate)
        {
            formatError = false;
            duplicate = false;
            email = email.Trim();
            if (Regex.IsMatch(email, "^[a-zA-Z0-9_-]+@[a-zA-Z0-9_-]+(.[a-zA-Z0-9_-]+)+$") is false)
            {
                formatError = true;
                return false;
            }
            duplicate = SQLHelper.GetInstance().Queryable<User>().Any(x => x.Email == email);
            return !duplicate;
        }
        /// <summary>
        /// 验证用户名是否符合格式
        /// </summary>
        /// <param name="nickname">待验证格式</param>
        /// <param name="formatError">是否格式错误</param>
        /// <returns>是否通过</returns>
        public static bool VerifyNickName(string nickname, out bool formatError, out bool duplicate)
        {            
            formatError = false;
            duplicate = false;
            nickname = nickname.Trim();
            if (nickname.Length < 3)
            {
                formatError = true;
                return false;
            }
            duplicate = SQLHelper.GetInstance().Queryable<User>().Any(x => x.NickName == nickname);
            return !duplicate;
        }
        #endregion

        #region WebSocket逻辑
        /// <summary>
        /// 生成邮箱验证码
        /// </summary>
        /// <param name="socket">未登录 Websocket 连接</param>
        /// <param name="data">email: 目标邮箱</param>
        public static void GetEmailCaptcha(MsgHandler socket, JToken data, string onName)
        {
            string email = data["email"].ToString();
            if (Online.Captcha.ContainsKey(email))// 之前申请过，覆盖旧信息
            {
                if(Online.Captcha[email].ExpiredTimeCount > Captcha.RefreshTime)
                    Online.Captcha[email].RemoveCaptcha();
                else
                {
                    RuntimeLog.WriteSystemLog(onName, $"{onName} error, captcha refresh in CD", false);
                    socket.Emit(onName, Helper.SetError(403));
                    return;
                }
            }
            Captcha captcha = new(){Email = email, EmailCaptcha = Helper.GenCaptcha(6, false)};
            Online.Captcha.Add(email, captcha);
            RuntimeLog.WriteSystemLog(onName, $"{onName} Success, captcha={captcha}, Email={email}", true);
            socket.Emit(onName, Helper.SetOK());
        }
        /// <summary>
        /// 验证邮箱验证码
        /// </summary>
        /// <param name="socket">未登录 WebSocket 连接</param>
        /// <param name="data">email: 目标邮箱; captcha: 验证码</param>
        public static void VerifyEmailCaptcha(MsgHandler socket, JToken data, string onName)
        {
            string email = data["email"].ToString();
            if (Online.Captcha.ContainsKey(email))
            {
                if (Online.Captcha[email].EmailCaptcha == data["captcha"].ToString())
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
        /// <summary>
        /// 修改用户昵称
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data"></param>
        public static void ChangeNickName(MsgHandler socket, JToken data, string onName, User user)
        {
            string newName = data["nickName"].ToString();
            if (VerifyNickName(newName, out bool formatError, out bool duplicate))
            {
                user.NickName = newName;
                UpdateNickNameByID(user.Id, newName);
                socket.Emit(onName, Helper.SetOK());
                RuntimeLog.WriteSystemLog(onName, $"{onName} success, id={user.Id}, nickname={user.NickName}", true);
            }
            else if (formatError)
            {
                socket.Emit(onName, Helper.SetError(309));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, formatError, id={user.Id}, nickname={user.NickName}", false);
            }
            else if (duplicate)
            {
                socket.Emit(onName, Helper.SetError(302));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, duplicate nickname, id={user.Id}, nickname={user.NickName}", false);
            }
        }
        /// <summary>
        /// 修改密码
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">oldPassword: 旧密码; newPassword: 新密码</param>
        public static void ChangePassword(MsgHandler socket, JToken data, string onName, User user)
        {
            string oldPassword = data["oldPassword"].ToString().ToUpper();
            string newPassword = data["newPassword"].ToString().ToUpper();
            var db = SQLHelper.GetInstance();
            if (user.PassWord.ToUpper() == oldPassword)
            {
                user.LastChange = DateTime.Now;
                user.PassWord = newPassword;
                user.UpdateUser();
                RuntimeLog.WriteSystemLog(onName, $"{onName} success, id={user.Id} newPwd={data["newPassword"]}", true);
            }
            else
            {
                socket.Emit(onName, Helper.SetError(310));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, oldPassword not equal to newPassword", false);
            }
        }
        /// <summary>
        /// 修改邮箱
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">newEmail: 需要修改的新邮箱; captcha: 旧邮箱的验证码</param>
        /// <param name="onName"></param>
        /// <param name="user"></param>
        public static void ChangeEmail(MsgHandler socket, JToken data, string onName, User user)
        {
            // TODO: 邮箱验证码
            string email = data["newEmail"].ToString();
            if (VerifyEmail(email, out bool formatError, out bool duplicate))
            {
                UpdateEmailByID(user.Id, email);
                socket.Emit(onName, Helper.SetOK());
                RuntimeLog.WriteSystemLog(onName, $"{onName} success, id={user.Id}, oldEmail={user.Email}, newEmail={email}", true);
                user.Email = data["newEmail"].ToString();
            }
            else if (formatError)
            {
                socket.Emit(onName, Helper.SetError(305));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, email={email} formatError", false);
            }
            else if (duplicate)
            {
                socket.Emit(onName, Helper.SetError(301));
                RuntimeLog.WriteSystemLog(onName, $"{onName} error, email={email} duplicate email", false);
            }
        }
        
        public static void Login(MsgHandler socket, JToken data, string onName)
        {            
            // TODO: 登录日志
            RuntimeLog.WriteSystemLog("Login", $"Try Login. Account: {data["account"]}, Pass: {data["password"]}",
                true);
            string password = Helper.MD5Encrypt(data["password"].ToString());
            var db = SQLHelper.GetInstance();
            var user = db.Queryable<User>().First(x =>
                (x.Email == data["account"].ToString() || x.NickName == data["account"].ToString()) &&
                x.PassWord == password);
            if (user == null)
            {
                socket.Emit(onName, Helper.SetError(303));
                RuntimeLog.WriteSystemLog(onName, $"Login Fail. Account: {data["account"]}, Pass: {data["password"]}",
                    false);
            }
            else
            {
                user.WebSocket = socket;
                if (Online.Users.ContainsKey(socket.ID))
                    Online.Users[socket.ID] = user;
                else
                    Online.Users.Add(socket.ID, user);
                socket.Emit(onName, Helper.SetOK("ok", Helper.GetJWT(user)));
                RuntimeLog.WriteUserLog(user.Email, onName, "Login Success.", true);
            }
        }

        public static void Register(MsgHandler socket, JToken data, string onName)
        {
            string email = data["email"].ToString();
            string nickname = data["nickname"].ToString();
            string password = data["password"].ToString();
            
            var db = SQLHelper.GetInstance();
            VerifyEmail(email, out bool formatError, out bool duplicate);
            if (formatError)
            {
                socket.Emit(onName, Helper.SetError(305));
                RuntimeLog.WriteSystemLog(onName, $"email format wrong", false);
                return;
            }
            else if(duplicate)
            {
                socket.Emit(onName, Helper.SetError(301));
                RuntimeLog.WriteSystemLog(onName, $"duplicate email", false);
                return;
            }

            VerifyNickName(nickname, out formatError, out duplicate);
            if (formatError)
            {
                socket.Emit(onName, Helper.SetError(306));
                RuntimeLog.WriteSystemLog(onName, $"nickname format wrong", false);
                return;
            }
            else if(duplicate)
            {
                socket.Emit(onName, Helper.SetError(302));
                RuntimeLog.WriteSystemLog(onName, $"nickname email", false);
                return;
            }
            
            if (password.Length != 32)
            {
                socket.Emit(onName, Helper.SetError(304));
                RuntimeLog.WriteSystemLog(onName, $"password format wrong", false);
            }
            User u = new()
            {
                Email = email,
                PassWord = password.ToUpper(),
                NickName = nickname,
                CreateTime = DateTime.Now,
                LastChange = DateTime.Now,
            };
            db.Insertable(u).ExecuteCommand();
            socket.Emit(onName, Helper.SetOK());
        }

        #endregion

        public object Clone() => MemberwiseClone();
        /// <summary>
        /// 去除机密字段
        /// </summary>
        public object WithoutSecret()
        {
            var c = (User)Clone();
            return new { c.Id, c.NickName, c.LastChange, c.CreateTime, c.Email, c.Status, c.XorKey };
        }
    }
}
