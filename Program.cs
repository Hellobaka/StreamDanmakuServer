using StreamDanmuku_Server.Data;
using System;

namespace StreamDanmuku_Server
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
                    case "getstreamcount":
                        Console.WriteLine(Online.StreamerUser.Count);
                        break;
                    default:
                        break;
                }
            }
        }
    }
}
