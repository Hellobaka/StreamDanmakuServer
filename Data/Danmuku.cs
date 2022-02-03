using StreamDanmuku_Server.Enum;

namespace StreamDanmuku_Server.Data
{
    public class Danmuku
    {
        public string Content { get; set; }
        public string Color { get; set; }
        public DanmukuPosition Position { get; set; }
        public long Time { get; set; }
        public string SenderUserName { get; set; }
        public int SenderUserID { get; set; }
        
    }
}