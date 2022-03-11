using System.Threading;

namespace StreamDanmaku_Server.Data
{
    public class Captcha
    {
        /// <summary>
        /// 验证码自动销毁时间，单位 秒
        /// </summary>
        public static int ExpiredTime = 60 * 10;
        /// <summary>
        /// 允许覆盖刷新验证码的时间，单位 秒
        /// </summary>
        public static int RefreshTime = 60 * 1;
        public string Email { get; set; }
        public string EmailCaptcha { get; set; }

        public int ExpiredTimeCount { get; set; }
        public bool Continued { get; set; } = true;

        public Captcha()
        {
            new Thread(() =>
            {
                while (Continued)
                {
                    Thread.Sleep(1000);
                    if (ExpiredTimeCount == ExpiredTime)
                    {
                        RemoveCaptcha();
                    }
                    ExpiredTimeCount++;
                }
                RemoveCaptcha();
            }).Start();
        }

        public void RemoveCaptcha()
        {
            Online.Captcha.Remove(Email);
            Continued = false;
            RuntimeLog.WriteSystemLog("RemoveCaptcha", $"Remove captcha Email={Email}", true);
        }
    }
}