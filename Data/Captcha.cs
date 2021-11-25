using System;
using System.Threading;

public class Captcha
{
    public int UserID { get; set; } = 0;
    public int oldCaptcha { get; set; } = 0;
    public int newCaptcha { get; set; } = 0;
    public void GenOldCaptcha() {
        oldCaptcha = new Random().Next(100000, 999999);
        new Thread(() =>
        {
            Thread.Sleep(1000 * 60 * 10);
            oldCaptcha = 0;
        }).Start();
    }
    public void GenNewCaptcha() {
        newCaptcha = new Random().Next(100000, 999999);
        new Thread(() =>
        {
            Thread.Sleep(1000 * 60 * 10);
            newCaptcha = 0;
        }).Start();
    }

}