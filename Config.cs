using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace StreamDanmuku_Server
{
    public static class Config
    {
        public static string ConfigFileName = @"Config.json";
        public static T GetConfig<T>(string SectionName)
        {
            if(File.Exists(ConfigFileName) is false)
                throw new FileNotFoundException();
            var o =JObject.Parse(File.ReadAllText(ConfigFileName));
            if (o.ContainsKey(SectionName))
                return o[SectionName].ToObject<T>();
            else
                throw new Exception("配置文件不包含此键");
        }
    }
}
