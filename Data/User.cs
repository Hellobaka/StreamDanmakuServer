using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SqlSugar;
using StreamDanmaku_Server.Enum;
using static StreamDanmaku_Server.SocketIO.Server;

namespace StreamDanmaku_Server.Data
{
    /// <summary>
    /// 用户相关类
    /// </summary>
    [JsonObject(MemberSerialization.OptOut)]
    public class User : ICloneable
    {
        /// <summary>
        /// 用户ID
        /// </summary>
        [SugarColumn(IsIdentity = true, IsPrimaryKey = true)]
        public int Id { get; set; }

        /// <summary>
        /// 邮箱
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
        /// 注册日期
        /// </summary>
        public DateTime LastLoginTime { get; set; }
        /// <summary>
        /// 是否可直播
        /// </summary>
        public bool CanStream { get; set; } = true;
        /// <summary>
        /// 是否可发送弹幕
        /// </summary>
        public bool CanSendDanmaku { get; set; } = true;

        /// <summary>
        /// 用户状态
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public UserStatus Status { get; set; } = UserStatus.OffLine;
        /// <summary>
        /// 对应连接
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        [JsonIgnore]
        public MsgHandler WebSocket { get; set; }

        /// <summary>
        /// 加密通信使用的密钥，暂时搁置
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public string XorKey { get; set; }
        /// <summary>
        /// 用户所在房间
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        [JsonIgnore]
        public Room CurrentRoom { get; set; }

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
            db.Updateable<User>().Where(x => x.Id == id).SetColumns(x => new User() {NickName = nickName})
                .ExecuteCommand();
        }

        /// <summary>
        /// 按用户ID更新邮箱
        /// </summary>
        /// <param name="id">用户ID</param>
        /// <param name="email">需更改的邮箱</param>
        public static void UpdateEmailByID(int id, string email)
        {
            var db = SQLHelper.GetInstance();
            db.Updateable<User>().Where(x => x.Id == id).SetColumns(x => new User() {Email = email}).ExecuteCommand();
        }

        /// <summary>
        /// 按用户ID查询用户
        /// </summary>
        /// <param name="id">用户ID</param>
        /// <returns>查询结果</returns>
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
        /// <param name="duplicate">是否重复</param>
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
        /// <param name="onName">操作名称</param>
        public static void GetEmailCaptcha(MsgHandler socket, JToken data, string onName)
        {
            string email = data["email"].ToString();
            if (Online.Captcha.ContainsKey(email)) // 之前申请过，覆盖旧信息
            {
                if (Online.Captcha[email].ExpiredTimeCount > Captcha.RefreshTime)
                    Online.Captcha[email].RemoveCaptcha();
                else
                {
                    RuntimeLog.WriteSystemLog(onName, $"申请验证码失败, 仍在刷新冷却", false);
                    socket.Emit(onName, Helper.SetError(ErrorCode.CaptchaCoolDown));
                    return;
                }
            }

            Captcha captcha = new() {Email = email, EmailCaptcha = Helper.GenCaptcha(6, false)};
            Online.Captcha.Add(email, captcha);
            RuntimeLog.WriteSystemLog(onName, $"申请验证码成功, 验证码={captcha}, 邮箱={email}", true);
            socket.Emit(onName, Helper.SetOK());
        }

        /// <summary>
        /// 验证邮箱验证码
        /// </summary>
        /// <param name="socket">普通 WebSocket 连接</param>
        /// <param name="data">email: 目标邮箱; captcha: 验证码</param>
        /// <param name="onName">操作名称</param>
        public static void VerifyEmailCaptcha(MsgHandler socket, JToken data, string onName)
        {
            string email = data["email"].ToString();
            if (Online.Captcha.ContainsKey(email))
            {
                if (Online.Captcha[email].EmailCaptcha == data["captcha"].ToString())
                {
                    Online.Captcha.Remove(email);
                    RuntimeLog.WriteSystemLog(onName, $"验证验证码成功, 邮箱={email}", true);
                    socket.Emit(onName, Helper.SetOK());
                }
                else
                {
                    socket.Emit(onName, Helper.SetError(ErrorCode.CaptchaInvalidOrWrong));
                    RuntimeLog.WriteSystemLog(onName, $"验证验证码失败, 验证码不匹配", false);
                }
            }
            else
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.CaptchaInvalid));
                RuntimeLog.WriteSystemLog(onName, $"验证验证码失败, 验证码无效", false);
            }
        }

        /// <summary>
        /// 修改用户昵称
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">nickname: 新昵称</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">操作对象</param>
        public static void ChangeNickName(MsgHandler socket, JToken data, string onName, User user)
        {
            string newName = data["nickName"].ToString();
            if (VerifyNickName(newName, out bool formatError, out bool duplicate))
            {
                user.NickName = newName;
                UpdateNickNameByID(user.Id, newName);
                socket.Emit(onName, Helper.SetOK());
                RuntimeLog.WriteUserLog(user, onName, $"修改昵称成功, id={user.Id}, 昵称={user.NickName}", true);
            }
            else if (formatError)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.UserNameFormatError));
                RuntimeLog.WriteUserLog(user, onName,
                    $"修改昵称错误, 格式错误, id={user.Id}, 昵称={user.NickName}", false);
            }
            else if (duplicate)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.DuplicateUsername));
                RuntimeLog.WriteUserLog(user, onName,
                    $"修改昵称错误, 用户名重复, id={user.Id}, 昵称={user.NickName}", false);
            }
        }

        /// <summary>
        /// 修改密码
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">oldPassword: 旧密码; newPassword: 新密码</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void ChangePassword(MsgHandler socket, JToken data, string onName, User user)
        {
            string oldPassword = data["oldPassword"].ToString().ToUpper();
            string newPassword = data["newPassword"].ToString().ToUpper();
            if (user.PassWord.ToUpper() != oldPassword)
            {
                user.LastChange = DateTime.Now;
                user.PassWord = newPassword;
                user.UpdateUser();
                RuntimeLog.WriteUserLog(user, onName, $"密码修改成功, id={user.Id} 新密码={data["newPassword"]}", true);
            }
            else
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.OldPasswordEqualNewPassword));
                RuntimeLog.WriteUserLog(user, onName, $"密码修改失败, 旧密码与新密码相同", false);
            }
        }
        /// <summary>
        /// 后台切换批量用户可直播状态
        /// </summary>
        /// <param name="socket">后台 WebSocket 连接</param>
        /// <param name="data">uid: 用户ID数组；action: 想要切换到的bool</param>
        /// <param name="onName">操作名称</param>
        public static void ToggleStream_Admin(MsgHandler socket, JToken data, string onName)
        {
            using var db = SQLHelper.GetInstance();
            List<int> err = new();// 不存在的用户数组
            int count = 0;
            foreach (var item in (data["uid"] as JArray)!)
            {
                var user = db.Queryable<User>().Where(x => x.Id == (int) item).First();
                if (user != null)
                {
                    user.CanStream = (bool) data["action"];
                    user.UpdateUser();
                    count++;
                }
                else
                {
                    err.Add((int) item);
                }
            }

            if (err.Count == 0)
            {
                socket.Emit(onName, Helper.SetOK());
                RuntimeLog.WriteUserLog("Admin", onName, $"切换直播状态成功，数量={count}，操作={(bool) data["action"]}", true);
            }
            else
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.PartError, new {count = err.Count}));
                RuntimeLog.WriteUserLog("Admin",onName, $"切换直播状态失败，部分用户不存在, 失败数量={err.Count}", false);
            }
        }
        /// <summary>
        /// 后台编辑用户信息
        /// </summary>
        /// <param name="socket">后台 WebSocket 连接</param>
        /// <param name="data">表单</param>
        /// <param name="onName">操作名称</param>
        public static void EditUser_Admin(MsgHandler socket, JToken data, string onName)
        {
            int uid = (int) data["uid"];
            var db = SQLHelper.GetInstance();
            var user = db.Queryable<User>().Where(x => x.Id == uid).First();
            if (user != null)
            {
                user.NickName = data["nickname"].ToString();
                user.Email = data["email"].ToString();
                if (!string.IsNullOrWhiteSpace(data["pwd"].ToString()))
                {
                    user.PassWord = data["pwd"].ToString().ToUpper();
                }

                user.UpdateUser();
                socket.Emit(onName, Helper.SetOK());
                RuntimeLog.WriteUserLog("Admin",onName, $"编辑用户信息成功, id={user.Id}", true);
            }
            else
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.InvalidUser));
                RuntimeLog.WriteUserLog("Admin",onName, $"编辑用户失败, 目标用户不存在，id={data["uid"]}", false);
            }
        }
        /// <summary>
        /// 后台批量切换用户禁言状态
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="data"></param>
        /// <param name="onName"></param>
        public static void ToggleSilent_Admin(MsgHandler socket, JToken data, string onName)
        {
            using var db = SQLHelper.GetInstance();
            List<int> err = new();
            int count = 0;
            foreach (var item in (data["uid"] as JArray)!)
            {
                var user = db.Queryable<User>().Where(x => x.Id == (int) item).First();
                if (user != null)
                {
                    user.CanSendDanmaku = (bool) data["action"];
                    user.UpdateUser();
                    count++;
                }
                else
                {
                    err.Add((int) item);
                }
            }

            if (err.Count == 0)
            {
                socket.Emit(onName, Helper.SetOK());
                RuntimeLog.WriteUserLog("Admin",onName, $"切换禁言状态成功，数量={count}，操作={(bool) data["action"]}", true);
            }
            else
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.PartError, new {count = err.Count}));
                RuntimeLog.WriteUserLog("Admin",onName, $"切换禁言状态失败，部分用户不存在，失败数量={count}", false);
            }
        }
        /// <summary>
        /// 后台获取所有用户信息
        /// </summary>
        /// <param name="socket">后台 WebSocket 连接</param>
        /// <param name="data">未使用字段</param>
        /// <param name="onName">操作名称</param>
        public static void GetUsers_Admin(MsgHandler socket, JToken data, string onName)
        {
            var db = SQLHelper.GetInstance();
            var users = db.Queryable<User>().ToList();
            List<object> r = new();
            users.ForEach(x => r.Add(x.WithoutSecret()));
            socket.Emit(onName, Helper.SetOK("ok", r));
            RuntimeLog.WriteUserLog("Admin",onName, $"后台拉取用户列表成功", true);
        }

        /// <summary>
        /// 修改邮箱
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">newEmail: 需要修改的新邮箱; captcha: 旧邮箱的验证码</param>
        /// <param name="onName">操作名称</param>
        /// <param name="user">调用对象</param>
        public static void ChangeEmail(MsgHandler socket, JToken data, string onName, User user)
        {
            // TODO: 邮箱验证码
            string email = data["newEmail"].ToString();
            if (VerifyEmail(email, out bool formatError, out bool duplicate))
            {
                UpdateEmailByID(user.Id, email);
                socket.Emit(onName, Helper.SetOK());
                RuntimeLog.WriteSystemLog(onName,
                    $"修改邮箱成功, id={user.Id}, 旧邮箱={user.Email}, 新邮箱={email}", true);
                user.Email = data["newEmail"].ToString();
            }
            else if (formatError)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.EmailFormatError));
                RuntimeLog.WriteSystemLog(onName, $"修改邮箱失败, 目标={email} 格式错误", false);
            }
            else if (duplicate)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.DuplicateEmail));
                RuntimeLog.WriteSystemLog(onName, $"修改邮箱失败, 目标={email} 此邮箱已被使用", false);
            }
        }
        /// <summary>
        /// 用户登录
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">account: 账号；password: 未加密密码</param>
        /// <param name="onName">操作名称</param>
        public static void Login(MsgHandler socket, JToken data, string onName)
        {
            string account = data["account"]?.ToString();
            string password = data["password"].ToString();

            switch (socket.UserType)
            {
                case UserType.Client:
                    password = Helper.MD5Encrypt(data["password"].ToString());
                    var db = SQLHelper.GetInstance();
                    var user = db.Queryable<User>().First(x =>
                        (x.Email == account || x.NickName == account) &&
                        x.PassWord == password);
                    if (user == null)
                    {
                        socket.Emit(onName, Helper.SetError(ErrorCode.WrongUserNameOrPassword));
                        RuntimeLog.WriteSystemLog(onName,
                            $"用户登录失败 账号={data["account"]}, 密码={data["password"]}, IP={socket.ClientIP}",
                            false);
                    }
                    else
                    {
                        user.Status = UserStatus.StandBy;
                        user.WebSocket = socket;
                        user.LastLoginTime = DateTime.Now;
                        user.UpdateUser();
                        socket.Emit(onName, Helper.SetOK("ok", Helper.GetJWT(user)));
                        RuntimeLog.WriteUserLog(user, onName, $"用户登录成功, IP={socket.ClientIP}", true);
                    }

                    break;
                case UserType.Admin:// 后台登录，密码存放在Config.json内
                    if (string.IsNullOrWhiteSpace(password))
                    {
                        socket.Emit(onName, Helper.SetError(ErrorCode.WrongUserNameOrPassword));
                        RuntimeLog.WriteSystemLog(onName,
                            $"后台登录失败，密码为空，IP={socket.ClientIP}",
                            false);
                        return;
                    }

                    if (Config.GetConfig<string>("AdminPassword") == password)
                    {
                        socket.Emit(onName, Helper.SetOK("ok", Helper.GetJWT(new User {Id = 0})));
                        socket.Authed = true;
                        RuntimeLog.WriteUserLog("Admin", onName, $"后台登录成功, IP={socket.ClientIP}", true);
                        Online.Admins.Add(socket);
                    }
                    else
                    {
                        socket.Emit(onName, Helper.SetError(ErrorCode.WrongUserNameOrPassword));
                        RuntimeLog.WriteSystemLog(onName,
                            $"后台登录失败，密码错误，IP={socket.ClientIP}",
                            false);
                    }
                    break;
            }
        }
        /// <summary>
        /// 用户注册
        /// </summary>
        /// <param name="socket">普通在线 WebSocket 连接</param>
        /// <param name="data">注册表单</param>
        /// <param name="onName">操作名称</param>
        public static void Register(MsgHandler socket, JToken data, string onName)
        {
            string email = data["email"].ToString();
            string nickname = data["nickname"].ToString();
            string password = data["password"].ToString();

            var db = SQLHelper.GetInstance();
            VerifyEmail(email, out bool formatError, out bool duplicate);
            if (formatError)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.EmailFormatError));
                RuntimeLog.WriteSystemLog(onName, $"注册失败，邮箱格式错误，邮箱={email}", false);
                return;
            }
            else if (duplicate)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.DuplicateEmail));
                RuntimeLog.WriteSystemLog(onName, $"注册失败，邮箱重复，邮箱={email}", false);
                return;
            }

            VerifyNickName(nickname, out formatError, out duplicate);
            if (formatError)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.UserNameFormatError));
                RuntimeLog.WriteSystemLog(onName, $"注册失败，昵称格式错误，昵称={nickname}", false);
                return;
            }

            if (duplicate)
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.DuplicateUsername));
                RuntimeLog.WriteSystemLog(onName, $"注册失败，昵称重复，昵称={nickname}", false);
                return;
            }

            if (password.Length != 32) // 加密之后都是32位
            {
                socket.Emit(onName, Helper.SetError(ErrorCode.PasswordFormatError));
                RuntimeLog.WriteSystemLog(onName, $"注册失败，密码格式错误，密码={password}", false);
            }

            User u = new()
            {
                Email = email,
                PassWord = password.ToUpper(),
                NickName = nickname,
                CreateTime = DateTime.Now,
                LastChange = DateTime.Now,
                LastLoginTime = DateTime.Now,
                CanStream = true,
                CanSendDanmaku = true,
            };
            db.Insertable(u).ExecuteCommand();
            socket.Emit(onName, Helper.SetOK());
            RuntimeLog.WriteSystemLog(onName, $"注册成功，邮箱={email}", false);
        }

        #endregion

        public object Clone() => MemberwiseClone();

        /// <summary>
        /// 去除机密字段
        /// </summary>
        public object WithoutSecret()
        {
            var c = (User) Clone();
            return new
            {
                c.Id, c.NickName, c.LastChange, c.CreateTime, c.Email, c.Status, c.CanSendDanmaku, c.CanStream,
                c.LastLoginTime
            };
        }
    }
}