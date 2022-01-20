using JWT.Algorithms;
using JWT.Builder;
using Newtonsoft.Json;
using StreamDanmuku_Server.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace StreamDanmuku_Server
{
    public static class Helper
    {
        const string SALT = "I AM FW";
        public static string ToJson(this object json)=> JsonConvert.SerializeObject(json, Formatting.None);
        public static string MD5Encrypt(string msg)=>
            BitConverter.ToString(new MD5CryptoServiceProvider().ComputeHash(Encoding.UTF8.GetBytes(msg + SALT))).Replace("-","");

        static SocketIO.Server Server;
        public static void StartUp()
        {
            if (File.Exists(Config.ConfigFileName) is false)
                File.WriteAllText(Config.ConfigFileName, "{\"ServerPort\": 62353}");
            SQLHelper.Init();
            Server = new(Config.GetConfig<ushort>("ServerPort"));
            Server.StartServer();
        }
        public static FunctionResult SetOK(string msg="ok", object obj = null) => new() { code = 200, msg = msg,  data = obj };
        public static FunctionResult SetError(int code, string msg="err", object obj = null) => new() { code = code, msg = ErrorCode.Content[code],  data = obj };
        public static long TimeStamp => (long)(DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0)).TotalMilliseconds;
        public static string GetJWT(User user){
            return JwtBuilder.Create()
                      .WithAlgorithm(new HMACSHA256Algorithm()) // symmetric
                      .WithSecret(MD5Encrypt(SALT))
                      .AddClaim("exp", DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds())
                      .AddClaim("id", user.Id)
                      .AddClaim("statusChange", user.LastChange)
                      .Encode();
        }
        public static string ParseJWT(string jwt){
            return JwtBuilder.Create()
                     .WithAlgorithm(new HMACSHA256Algorithm()) // symmetric
                     .WithSecret(MD5Encrypt(SALT))
                     .MustVerifySignature()
                     .Decode(jwt); 
        }
        /// <summary>
        /// 生成验证码
        /// </summary>
        /// <returns>6位随机数字, 不保证不重复</returns>
        public static int GenCaptcha() => new Random().Next(100000, 999999);
    }
}
