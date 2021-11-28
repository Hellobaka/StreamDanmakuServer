using SqlSugar;
using StreamDanmuku_Server.SocketIO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StreamDanmuku_Server.Data
{
    public class Room
    {
        public string Title { get; set; }
        public int UserID { get; set; }
        public bool IsPublic { get; set; }
        public string Password { get; set; }
        public int Max { get; set; }
        public DateTime CreateTime { get; set; }
        public List<Server.MsgHandler> Clients {get;set;}
    }
}
