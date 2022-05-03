using StreamDanmaku_Server.Data;
using System;

namespace StreamDanmaku_Server
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Helper.StartUp();
            while (true)
            {
                string cmd = Console.ReadLine();
                switch (cmd)
                {
                    case "clearroom":
                        Online.Rooms.Clear();
                        break;
                    case "pull":
                        Console.WriteLine(new Room().GenLivePullURL());
                        break;
                    default:
                        break;
                }
            }
        }
    }
}
