using Newtonsoft.Json.Linq;
using StreamDanmaku_Server.Data;
using System;
using System.IO;

namespace StreamDanmaku_Server
{
    public static class Config
    {
        public static string ConfigFileName = @"Config.json";
        public static T GetConfig<T>(string sectionName)
        {
            if(File.Exists(ConfigFileName) is false)
                throw new FileNotFoundException();
            var o =JObject.Parse(File.ReadAllText(ConfigFileName));
            if (o.ContainsKey(sectionName))
                return o[sectionName].ToObject<T>();
            else
            {
                RuntimeLog.WriteSystemLog("Config", $"配置文件不包含 {sectionName} 键", false);
                if (typeof(T) == typeof(string))
                    return (T)(object)"";
                else if (typeof(T) == typeof(int))
                    return (T)(object)0;
                else if (typeof(T) == typeof(bool))
                    return (T)(object)false;
                else if (typeof(T) == typeof(object))
                    return (T)(object)new { };
                else
                    throw new Exception("无法默认返回");
            }
        }
    }
}
