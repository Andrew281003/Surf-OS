using System;

namespace SurfOS.Boot
{
    public static class KernelPanic
    {
        public static void Show(string message, Exception exception)
        {
            Console.BackgroundColor = ConsoleColor.DarkRed;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Clear();
            Console.WriteLine("SURF KERNEL PANIC");
            Console.WriteLine();
            Console.WriteLine(message);
            Console.WriteLine(exception == null ? "No diagnostic was supplied." : exception.Message);
            Console.WriteLine();
            Console.WriteLine("The CPU has been halted to protect the system.");
            Cosmos.Core.CPU.Halt();
        }
    }
}
