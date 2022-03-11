using StreamDanmaku_Server.Enum;

namespace StreamDanmaku_Server.Data
{
    public class Danmaku
    {
        public string Content { get; set; }
        public string Color { get; set; }
        public DanmakuPosition Position { get; set; }
        public long Time { get; set; }
        public string SenderUserName { get; set; }
        public int SenderUserID { get; set; }
        
    }
}