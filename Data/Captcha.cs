using System.Threading;

namespace StreamDanmaku_Server.Data
{
    /// <summary>
    /// 邮箱验证码类
    /// </summary>
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
        /// <summary>
        /// 所属的邮箱
        /// </summary>
        public string Email { get; set; }
        /// <summary>
        /// 目标邮箱验证码
        /// </summary>
        public string EmailCaptcha { get; set; }
        /// <summary>
        /// 过期时间累计
        /// </summary>
        public int ExpiredTimeCount { get; set; }
        /// <summary>
        /// 能否继续执行标识
        /// </summary>
        private bool Continued { get; set; } = true;
        public bool Verified { get; set; } = false;
        public string ActionName { get; set; }
        /// <summary>
        /// 构造函数
        /// </summary>
        public Captcha()
        {
            new Thread(() =>
            {
                while (Continued)
                {
                    Thread.Sleep(1000);
                    if (ExpiredTimeCount == ExpiredTime)// 达到销毁时
                    {
                        RemoveCaptcha();
                    }

                    ExpiredTimeCount++;
                }

                RemoveCaptcha();
            }).Start();
        }
        private bool called;
        /// <summary>
        /// 移除验证码
        /// </summary>
        public void RemoveCaptcha()
        {
            if (called) return;
            called = true;
            Online.Captcha.Remove(Email);
            Continued = false;
            RuntimeLog.WriteSystemLog("RemoveCaptcha", $"验证码销毁, 邮箱={Email}, 验证码={EmailCaptcha}", true);
        }
    }
}