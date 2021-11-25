using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StreamDanmuku_Server.Data
{
    internal class Online
    {
        public static Dictionary<string, User> Users { get; set; } = new();
        public static List<Room> Rooms { get; set; } = new();
        public static Dictionary<object, int> Captcha { get; set; } = new();
    }
}
