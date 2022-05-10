using JWT.Algorithms;
using JWT.Builder;
using Newtonsoft.Json;
using StreamDanmaku_Server.Data;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using StreamDanmaku_Server.Enum;
using SqlSugar;

namespace StreamDanmaku_Server
{
    /// <summary>
    /// 通用帮助类
    /// </summary>
    public static class Helper
    {
        /// <summary>
        /// MD5后加盐
        /// </summary>
        private const string SALT = "I AM FW";
        /// <summary>
        /// 连接保存对象
        /// </summary>
        static SocketIO.Server Server;
        /// <summary>
        /// 启动初始化
        /// </summary>
        public static void StartUp()
        {
            if (File.Exists(Config.ConfigFileName) is false)
                File.WriteAllText(Config.ConfigFileName, "{\"ServerPort\": 62353}");
            SQLHelper.Init();
            Server = new(Config.GetConfig<ushort>("ServerPort"));
            Server.StartServer();
        }
        /// <summary>
        /// 返回成功对象
        /// </summary>
        /// <param name="msg">附加文本 默认ok</param>
        /// <param name="obj">附加对象 默认空</param>
        /// <returns>通用成功对象</returns>
        public static FunctionResult SetOK(object obj = null, string msg = "ok") =>
            new() { code = 200, msg = msg, data = obj };

        /// <summary>
        /// 返回失败对象
        /// </summary>
        /// <param name="code">错误码 枚举类型</param>
        /// <param name="obj">附加对象</param>
        /// <returns>通用失败对象</returns>
        public static FunctionResult SetError(ErrorCode code, object obj = null) =>
            new() { code = (int)code, msg = ErrorCodeDict.Content[(int)code], data = obj };

        /// <summary>
        /// 扩展方法 快捷调用对象序列化
        /// </summary>
        /// <param name="json">需要序列化的对象</param>
        public static string ToJson(this object json) => JsonConvert.SerializeObject(json, Formatting.None);

        /// <summary>
        /// 对文件进行MD5处理，并返回32位大写字符串
        /// </summary>
        /// <param name="msg">待处理文本</param>
        /// <param name="salt">是否加盐</param>
        public static string MD5Encrypt(string msg, bool salt = true) =>
            BitConverter
                .ToString(new MD5CryptoServiceProvider().ComputeHash(Encoding.UTF8.GetBytes(salt ? msg + SALT : msg)))
                .Replace("-", "");

        /// <summary>
        /// 毫秒级时间戳
        /// </summary>
        public static long TimeStampms =>
            (long)(DateTime.Now.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0)).TotalMilliseconds;

        /// <summary>
        /// 秒级时间戳
        /// </summary>
        public static long TimeStamp => (long)(DateTime.Now.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds;

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
                    result += (char)(rd.Next(0, 2) == 0 ? rd.Next('0', '9' + 1) : rd.Next('A', 'Z' + 1));
                else
                    result += rd.Next(0, 10);
            }

            return result;
        }
        /// <summary>
        /// 日志键排序用
        /// </summary>
        /// <param name="arr">调用</param>
        /// <param name="key">需要排序的键</param>
        /// <param name="desc">是否降序</param>
        /// <typeparam name="T">调用的T</typeparam>
        public static ISugarQueryable<T> CustomOrderBy<T>(this ISugarQueryable<T> arr, string key, bool desc) =>
            arr.OrderByIF(!string.IsNullOrWhiteSpace(key), $"{key} {(desc ? "desc" : "asc")}");
        public static bool VersionCompare(string version1, string version2)
        {
            if (string.IsNullOrWhiteSpace(version1) || string.IsNullOrWhiteSpace(version2)) return false;
            var va = version1.Split('.');
            var vb = version2.Split('.');
            if (va.Length != vb.Length) return false;
            int va_value = 0, vb_value = 0;
            for (int i = 0; i < va.Length; i++)
            {
                va_value += Convert.ToInt32(va[i]) * (int)Math.Pow(10, 2 - i);
                vb_value += Convert.ToInt32(vb[i]) * (int)Math.Pow(10, 2 - i);
            }
            return va_value > vb_value;
        }
    }
}