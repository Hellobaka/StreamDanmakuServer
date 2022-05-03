using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StreamDanmaku_Server.Data
{
    internal class Online
    {
        public static List<User> Users { get; set; } = new();
        public static List<SocketIO.Server.MsgHandler> Admins { get; set; } = new();
        public static List<Room> Rooms { get; set; } = new();
        public static Dictionary<string, Captcha> Captcha { get; set; } = new();
    }
}
