namespace StreamDanmaku_Server
{
    public class FunctionResult
    {
        public int code { get; set; }
        public string msg { get; set; }
        public object data { get; set; }
        public bool isSuccess => code == 200;
    }
}
