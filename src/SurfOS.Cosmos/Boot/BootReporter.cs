using System;
using SurfOS.Logging;

namespace SurfOS.Boot
{
    public sealed class BootReporter
    {
        private readonly IKernelLogger _logger;

        public BootReporter(IKernelLogger logger)
        {
            _logger = logger;
        }

        public void Heading()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("SurfOS Kernel v0.1");
            Console.ResetColor();
            Console.WriteLine();
        }

        public void Ok(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[ OK ] ");
            Console.ResetColor();
            Console.WriteLine(message);
            _logger.Info(message);
        }

        public void Fail(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("[FAIL] ");
            Console.ResetColor();
            Console.WriteLine(message);
            _logger.Error(message);
        }
    }
}
