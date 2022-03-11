namespace StreamDanmaku_Server.Enum
{
    /// <summary>
    /// 用户当前状态
    /// </summary>
    public enum UserStatus
    {
        /// <summary>
        /// 直播中
        /// </summary>
        Streaming,
        /// <summary>
        /// 观看直播中
        /// </summary>
        Client,
        /// <summary>
        /// 在大厅
        /// </summary>
        StandBy,
        Banned,
        OffLine
    }

}