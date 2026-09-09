using System;
using SurfOS.Config;
using SurfOS.Storage;
using SurfOS.Users;

namespace SurfOS.Installer
{
    public sealed class FirstBootInstaller
    {
        private readonly IFileSystemService _fileSystem;
        private readonly ConfigurationService _configuration;
        private readonly UserService _users;

        public FirstBootInstaller(IFileSystemService fileSystem, ConfigurationService configuration, UserService users)
        {
            _fileSystem = fileSystem;
            _configuration = configuration;
            _users = users;
        }

        public void Run()
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("SurfOS first-boot setup");
            Console.ResetColor();
            Console.WriteLine("This milestone configures the device and first local account.");
            Console.WriteLine("Disk partitioning, networking, cloud, and telemetry are not enabled yet.");
            Console.WriteLine();

            string username = ReadRequired("Username: ");
            string device = ReadRequired("Device name [surfos]: ");
            if (device.Length == 0) { device = "surfos"; }

            string password;
            while (true)
            {
                Console.Write("Password (8+ characters): ");
                password = AuthenticationConsole.ReadSecret();
                Console.Write("Confirm password: ");
                string confirmation = AuthenticationConsole.ReadSecret();
                if (password.Length >= 8 && password == confirmation) { break; }
                Console.WriteLine("Passwords must match and contain at least 8 characters.");
            }

            _users.CreateAdministrator(username, password);
            SystemConfiguration settings = new SystemConfiguration();
            settings.DeviceName = device;
            settings.Language = "en-US";
            settings.KeyboardLayout = "US";
            settings.TimeZone = "UTC";
            settings.Theme = "SurfDark";
            _configuration.Save(settings);
            _fileSystem.WriteAllText("/System/version", "Surf Kernel 0.1\r\n");
            Console.WriteLine("Installation complete. You can now log in.");
            Console.WriteLine();
        }

        private static string ReadRequired(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                string value = Console.ReadLine().Trim();
                // Device name permits the documented default through an empty answer.
                if (prompt.StartsWith("Device") || value.Length > 0) { return value; }
                Console.WriteLine("A value is required.");
            }
        }
    }
}
