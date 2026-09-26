using System.Collections.Generic;
using SurfOS.Logging;
using SurfOS.Storage;

namespace SurfOS.Config
{
    public sealed class ConfigurationService
    {
        private const string ConfigPath = "/Config/system.cfg";
        private readonly IFileSystemService _fileSystem;
        private readonly IKernelLogger _logger;

        public ConfigurationService(IFileSystemService fileSystem, IKernelLogger logger)
        {
            _fileSystem = fileSystem;
            _logger = logger;
        }

        public bool IsInstalled()
        {
            return _fileSystem.FileExists(ConfigPath) && _fileSystem.FileExists("/Users/users.db");
        }

        public SystemConfiguration Load()
        {
            Dictionary<string, string> values = Parse(_fileSystem.ReadAllText(ConfigPath));
            SystemConfiguration result = new SystemConfiguration();
            result.DeviceName = Get(values, "device", "surfos");
            result.Language = Get(values, "language", "en-US");
            result.KeyboardLayout = Get(values, "keyboard", "US");
            result.TimeZone = Get(values, "timezone", "UTC");
            result.Theme = Get(values, "theme", "SurfDark");
            return result;
        }

        public void Save(SystemConfiguration configuration)
        {
            string text = "version=1\r\n" +
                          "device=" + Safe(configuration.DeviceName) + "\r\n" +
                          "language=" + Safe(configuration.Language) + "\r\n" +
                          "keyboard=" + Safe(configuration.KeyboardLayout) + "\r\n" +
                          "timezone=" + Safe(configuration.TimeZone) + "\r\n" +
                          "theme=" + Safe(configuration.Theme) + "\r\n";
            _fileSystem.WriteAllText(ConfigPath, text);
            _logger.Info("System configuration saved.");
        }

        private static Dictionary<string, string> Parse(string text)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            string[] lines = text.Replace("\r", string.Empty).Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                int separator = lines[index].IndexOf('=');
                if (separator > 0)
                {
                    values[lines[index].Substring(0, separator)] = lines[index].Substring(separator + 1);
                }
            }
            return values;
        }

        private static string Get(Dictionary<string, string> values, string key, string fallback)
        {
            return values.ContainsKey(key) ? values[key] : fallback;
        }

        private static string Safe(string value)
        {
            return (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("=", "-");
        }
    }
}
