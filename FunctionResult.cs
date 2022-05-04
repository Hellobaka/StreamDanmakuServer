namespace StreamDanmaku_Server
{
    /// <summary>
    /// API返回通用对象
    /// </summary>
    public class FunctionResult
    {
        /// <summary>
        /// 操作码
        /// </summary>
        public int code { get; set; }
        /// <summary>
        /// 错误文本
        /// </summary>
        public string msg { get; set; }
        /// <summary>
        /// 附加对象
        /// </summary>
        public object data { get; set; }
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool isSuccess => code == 200;
    }
}