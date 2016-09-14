using System;

namespace Test
{
    internal static class Program
    {
        private static void Main()
        {
            Console.CancelKeyPress += (o, e) =>
            {
                Console.WriteLine("Ctrl+C has been pressed");

                while (true) { }
            };

            while (true) { }
        }
    }
}
