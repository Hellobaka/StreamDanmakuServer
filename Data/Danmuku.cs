using StreamDanmaku_Server.Enum;

namespace StreamDanmaku_Server.Data
{
    /// <summary>
    /// 弹幕对象
    /// </summary>
    public class Danmaku
    {
        /// <summary>
        /// 弹幕内容
        /// </summary>
        public string Content { get; set; }
        /// <summary>
        /// 弹幕颜色, #FFFFFF
        /// </summary>
        public string Color { get; set; }
        /// <summary>
        /// 弹幕表现样式, 滚动 顶部 底部
        /// </summary>
        public DanmakuPosition Position { get; set; }
        /// <summary>
        /// 弹幕发送时间
        /// </summary>
        public long Time { get; set; }
        /// <summary>
        /// 弹幕发送者昵称
        /// </summary>
        public string SenderUserName { get; set; }
        /// <summary>
        /// 弹幕发送者ID
        /// </summary>
        public int SenderUserID { get; set; }
        
    }
}