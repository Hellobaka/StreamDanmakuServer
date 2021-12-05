using System;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using SqlSugar;
using StreamDanmuku_Server.SocketIO;

namespace StreamDanmuku_Server.Data
{
    [JsonObject(MemberSerialization.OptOut)]
    public class User
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
        public UserStatus Status {get;set;} = UserStatus.StandBy;
        [SugarColumn(IsIgnore = true)]
        [JsonIgnore]
        public Server.MsgHandler WebSocket {get;set;}
        /// <summary>
        /// 加密通信使用的密钥，暂时搁置
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public string XorKey { get; set; }

        public enum UserStatus {
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
        public bool updateUser()
        {
            var db = SQLHelper.GetInstance();
            return db.Updateable(this).ExecuteCommand() == 1;
        }
        public static int GenCaptcha() => new Random().Next(100000, 999999);
        public static void UpdateNickNameByID(int id, string nickName)
        {
            var db = SQLHelper.GetInstance();
            db.Updateable<User>().Where(x => x.Id == id).SetColumns(x => new User() { NickName = nickName }).ExecuteCommand();
        }
        public static void UpdateEmailByID(int id, string Email)
        {
            var db = SQLHelper.GetInstance();
            db.Updateable<User>().Where(x => x.Id == id).SetColumns(x => new User() { Email = Email }).ExecuteCommand();
        }
        public static FunctionResult Login(string account, string password)
        {
            password = Helper.MD5Encrypt(password);
            var db = SQLHelper.GetInstance();
            var user = db.Queryable<User>().Where(x => (x.Email == account || x.NickName == account) && x.PassWord == password).First();
            if (user == null)
                return Helper.SetError(303);
            return Helper.SetOK("ok", user);
        }
        public static User GetUserByID(int id)
        {
            var db = SQLHelper.GetInstance();
            return db.Queryable<User>().Where(x => x.Id == id).First();
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
                    user.updateUser();
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
    }
}
