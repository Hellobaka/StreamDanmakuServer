using Newtonsoft.Json.Linq;
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
                throw new Exception("配置文件不包含此键");
        }
    }
}
