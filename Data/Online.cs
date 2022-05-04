using System.Collections.Generic;

namespace StreamDanmaku_Server.Data
{
    /// <summary>
    /// 在线相关
    /// </summary>
    public static class Online
    {
        /// <summary>
        /// 在线用户
        /// </summary>
        public static List<User> Users { get; set; } = new();
        /// <summary>
        /// 在线后台
        /// </summary>
        public static List<SocketIO.Server.MsgHandler> Admins { get; set; } = new();
        /// <summary>
        /// 所有房间
        /// </summary>
        public static List<Room> Rooms { get; set; } = new();
        /// <summary>
        /// 邮箱验证码
        /// </summary>
        public static Dictionary<string, Captcha> Captcha { get; set; } = new();
    }
}