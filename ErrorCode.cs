using System.Collections.Generic;

namespace StreamDanmuku_Server
{
    internal class ErrorCode
    {
        public static Dictionary<int, string> Content = new()
        {
            { 200, "ok"},
            { 301, "邮箱重复"},
            { 302, "昵称重复"},
            { 303, "用户名或密码错误"},
            { 304, "密码格式错误"},
            { 305, "邮箱格式错误" },
            { 306, "昵称格式错误" },
            { 307, "用户无效" },
            { 308, "不可创建多个房间" },
            { 309, "昵称格式错误" },
            { 310, "密码不匹配" },
            { 311, "房间不存在" },
            { 401, "参数格式错误"},
            { 402, "验证码过期或错误"},
            { 403, "验证码冷却"},
            { 404, "验证码不存在"},
            { 501, "Token过期"},
            { 502, "签名无效"},
            { 503, "Token无效"},
            { -100, "未知错误"},
        };
    }
}
