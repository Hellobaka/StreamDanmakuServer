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
                Console.ReadLine();
            }
        }
    }
}
