using System;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace StreamDanmaku_Server.Data
{
    public class Email
    {
        public string From { get; set; }//发件人地址
        public string Password { get; set; }//密码
        public string[] Address { get; set; }//收件人地址
        public string[] CC { get; set; }//抄送
        public string Subject { get; set; }//主题
        public string DisplayName { get; set; }//发件人名称
        public Encoding SubjectEncoding { get; set; }//编码
        public string Body { get; set; }//邮件内容
        public Encoding BodyEncoding { get; set; }//邮件内容编码
        public bool IsBodyHtml { get; set; }//是否HTML邮件
        public MailPriority Priority { get; set; }//邮件优先级
        public bool EnableSsl { get; set; }//是否ssl
        public bool UseDefaultCredentials { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        //https://www.cnblogs.com/xwcs/p/13508438.html
        /// <summary>
        /// SMTP发送邮件
        /// </summary>
        /// <param name="M">邮件对象</param>
        public static void Send(Email M)
        {
            try
            {
                MailMessage myMail = new();//发送电子邮件类

                foreach (string item in M.Address)//添加收件人
                {
                    myMail.To.Add(item);
                }
                foreach (string item in M.CC)//添加抄送
                {
                    myMail.CC.Add(item);
                }

                myMail.Subject = M.Subject;//邮件主题
                myMail.SubjectEncoding = M.SubjectEncoding;//邮件标题编码

                myMail.From = new MailAddress(M.From, M.DisplayName, M.SubjectEncoding);//发件信息


                myMail.Body = M.Body;//邮件内容
                myMail.BodyEncoding = M.BodyEncoding;//邮件内容编码
                myMail.IsBodyHtml = M.IsBodyHtml;//是否是HTML邮件
                myMail.Priority = M.Priority;//邮件优先级

                SmtpClient smtp = new();//SMTP协议

                smtp.EnableSsl = M.EnableSsl;//是否使用SSL安全加密 使用QQ邮箱必选
                smtp.UseDefaultCredentials = M.UseDefaultCredentials;

                smtp.Host = M.Host;//主机
                smtp.Port = M.Port;
                smtp.Credentials = new NetworkCredential(M.From, M.Password);//验证发件人信息

                smtp.Send(myMail);//发送

            }
            catch (Exception e)
            {
                Console.WriteLine($"[-] 发送邮件发生错误：{e.Message}");
            }
        }
        /// <summary>
        /// 初始化邮箱发送模板
        /// </summary>
        /// <param name="subject">标题</param>
        /// <param name="body">内容</param>
        /// <param name="address">发送地址</param>
        /// <returns></returns>
        public static Email GetTemplateMail(string subject, string body, string[] address) => new Email()
        {
            Address = address,
            Body = body,
            DisplayName = "StreamDanmaku",
            UseDefaultCredentials = false,
            SubjectEncoding = Encoding.UTF8,
            EnableSsl = true,
            Host = Config.GetConfig<string>("Smtp_Host"),
            Port = Config.GetConfig<int>("Smtp_Port"),
            IsBodyHtml = false,
            BodyEncoding = Encoding.UTF8,
            From = Config.GetConfig<string>("Smtp_Account"),
            Password = Config.GetConfig<string>("Smtp_Password"),
            Priority = MailPriority.Normal,
            CC = Array.Empty<string>(),
            Subject = subject
        };
        public static void SendEmail(string text, string title, string target)
        {
            var email = GetTemplateMail(title, text, new string[] { target });
            Send(email);
        }
    }
}
