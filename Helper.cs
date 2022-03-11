using JWT.Algorithms;
using JWT.Builder;
using Newtonsoft.Json;
using StreamDanmaku_Server.Data;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using StreamDanmaku_Server.Enum;

namespace StreamDanmaku_Server
{
    public static class Helper
    {
        private const string SALT = "I AM FW";

        static SocketIO.Server Server;

        public static void StartUp()
        {
            if (File.Exists(Config.ConfigFileName) is false)
                File.WriteAllText(Config.ConfigFileName, "{\"ServerPort\": 62353}");
            SQLHelper.Init();
            Server = new(Config.GetConfig<ushort>("ServerPort"));
            Server.StartServer();
        }

        public static FunctionResult SetOK(string msg = "ok", object obj = null) =>
            new() {code = 200, msg = msg, data = obj};

        public static FunctionResult SetError(ErrorCode code, string msg = "err", object obj = null) =>
            new() {code = (int)code, msg = ErrorCodeDict.Content[(int)code], data = obj};

        /// <summary>
        /// 扩展方法 快捷调用对象序列化
        /// </summary>
        /// <param name="json">需要序列化的对象</param>
        public static string ToJson(this object json) => JsonConvert.SerializeObject(json, Formatting.None);

        /// <summary>
        /// 对文件进行MD5处理，并返回32位大写字符串
        /// </summary>
        /// <param name="msg">待处理文本</param>
        public static string MD5Encrypt(string msg, bool salt = true) =>
            BitConverter.ToString(new MD5CryptoServiceProvider().ComputeHash(Encoding.UTF8.GetBytes(salt ? msg + SALT : msg)))
                .Replace("-", "");

        /// <summary>
        /// 毫秒级时间戳
        /// </summary>
        public static long TimeStampms => (long) (DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0)).TotalMilliseconds;
        /// <summary>
        /// 秒级时间戳
        /// </summary>
        public static long TimeStamp => (long) (DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds;

        /// <summary>
        /// 将对象转换为JWT字符串
        /// </summary>
        /// <param name="user"></param>
        public static string GetJWT(User user) => JwtBuilder.Create()
            .WithAlgorithm(new HMACSHA256Algorithm()) // symmetric
            .WithSecret(MD5Encrypt(SALT))
            .AddClaim("exp", DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds())
            .AddClaim("id", user.Id)
            .AddClaim("statusChange", user.LastChange)
            .Encode();

        /// <summary>
        /// 将JWT转换为json
        /// </summary>
        /// <param name="jwt">JWT字符串</param>
        public static string ParseJWT(string jwt) => JwtBuilder.Create()
            .WithAlgorithm(new HMACSHA256Algorithm()) // symmetric
            .WithSecret(MD5Encrypt(SALT))
            .MustVerifySignature()
            .Decode(jwt);

        /// <summary>
        /// 生成指定位数的随机数字字母文本，可指定是否有字母
        /// </summary>
        /// <param name="len">生成的位数</param>
        /// <param name="withAlpha">是否有大写字母</param>
        public static string GenCaptcha(int len, bool withAlpha)
        {
            string result = "";
            for (int i = 0; i < len; i++)
            {
                Random rd = new();
                if (withAlpha)
                    result += (char) (rd.Next(0, 2) == 0 ? rd.Next('0', '9' + 1) : rd.Next('A', 'Z' + 1));
                else
                    result += rd.Next(0, 10);
            }

            return result;
        }
    }
}