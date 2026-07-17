using System;
using System.Collections.Generic;

namespace Import
{
    internal class Variables
    {
        public static int numRun = 0;
        public static string userName = string.Empty;
        public static string userPassword = string.Empty;
        public static int uuid;
        public static string machineName = Environment.MachineName;
        public static string installPath = string.Empty;
        public static string vhdxHostDirectory = string.Empty;
        public static string vhdxPath = string.Empty;
        public static string partitionDriveLetter = "S";
        public static int packageOption;

        public static string defaultTheme = "HolySurf";
        public static string activePromptStyle = "Standard";
        public static string setupMode = "Advanced Setup";
        public static string timeZone = "Local";
        public static string language = "English (US)";
        public static string keyboardLayout = "US";
        public static string networkProfile = "Private";
        public static string updateChannel = "Stable";
        public static string performanceProfile = "Balanced";
        public static string systemFootprint = "Standard";
        public static string telemetryLevel = "Off";
        public static string surfCloudAccount = string.Empty;
        public static string partitionLabel = "SURFOS";
        public static string partitionFileSystem = "SurfFS";
        public static int partitionSizeGb = 15;
        public static int swapSizeGb = 2;
        public static int sleepTimeoutMinutes = 30;
        public static bool automaticUpdates = true;
        public static bool firewallEnabled = true;
        public static bool cloudServicesEnabled = true;
        public static bool surfCloudSignedIn = false;
        public static bool telemetryEnabled = false;
        public static bool crashReportsEnabled = true;
        public static bool locationServicesEnabled = false;
        public static bool partitionEncryption = false;
        public static bool developerToolsEnabled = false;
        public static bool sampleContentEnabled = true;
        public static bool safeMode = false;

        public static ConsoleColor activeForegroundColor = ConsoleColor.Gray;
        public static ConsoleColor activeBackgroundColor = ConsoleColor.Black;

        public static List<DatabaseRecord> userDatabase = new List<DatabaseRecord>();
        public static List<AlarmRecord> alarms = new List<AlarmRecord>();

        // 🌟 NEW: SessionStartTime tracks live OS uptime!
        public static DateTime sessionStartTime = DateTime.Now;
    }

    public class SystemOptions
    {
        public string InstallPath { get; set; } = string.Empty;
        public string VhdxHostDirectory { get; set; } = string.Empty;
        public string VhdxPath { get; set; } = string.Empty;
        public string PartitionDriveLetter { get; set; } = "S";
        public string UserName { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public int Uuid { get; set; }
        public int NumRun { get; set; }
        public int PackageOption { get; set; }
        public string DefaultTheme { get; set; } = "HolySurf"; 
        public string SetupMode { get; set; } = "Advanced Setup";
        public string TimeZone { get; set; } = "Local";
        public string Language { get; set; } = "English (US)";
        public string KeyboardLayout { get; set; } = "US";
        public string NetworkProfile { get; set; } = "Private";
        public string UpdateChannel { get; set; } = "Stable";
        public string PerformanceProfile { get; set; } = "Balanced";
        public string SystemFootprint { get; set; } = "Standard";
        public string TelemetryLevel { get; set; } = string.Empty;
        public string SurfCloudAccount { get; set; } = string.Empty;
        public string PartitionLabel { get; set; } = "SURFOS";
        public string PartitionFileSystem { get; set; } = "SurfFS";
        public int PartitionSizeGb { get; set; } = 15;
        public int SwapSizeGb { get; set; } = 2;
        public int SleepTimeoutMinutes { get; set; } = 30;
        public bool AutomaticUpdates { get; set; } = true;
        public bool FirewallEnabled { get; set; } = true;
        public bool CloudServicesEnabled { get; set; } = true;
        public bool SurfCloudSignedIn { get; set; }
        public bool TelemetryEnabled { get; set; }
        public bool CrashReportsEnabled { get; set; } = true;
        public bool LocationServicesEnabled { get; set; }
        public bool PartitionEncryption { get; set; }
        public bool DeveloperToolsEnabled { get; set; }
        public bool SampleContentEnabled { get; set; } = true;
        public string RecoveryCodeHash { get; set; } = string.Empty;
        public string RecoveryCodeSalt { get; set; } = string.Empty;
    }

    public class DatabaseRecord
    {
        public int ID { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Admin { get; set; } = string.Empty;

        // 🌟 NEW ECONOMY & MAIL FEATURES
        public string CurrentRank { get; set; } = "User";
        public List<MailMessage> Mailbox { get; set; } = new List<MailMessage>();
    }

    // 🌟 NEW: Mail Message Layout
    public class MailMessage
    {
        public string Sender { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string MessageText { get; set; } = string.Empty;
    }

    public class SurfTheme
    {
        public string ThemeName { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public string TargetColor { get; set; } = "Gray"; 
        public string BackgroundColor { get; set; } = "Black";
        public string FontName { get; set; } = "Consolas"; 
        public string UILayout { get; set; } = "Default";  
        public string WelcomeMessage { get; set; } = "System Ready";
        public string PromptStyle { get; set; } = "Standard";
        public string AsciiArt { get; set; } = string.Empty;
        public string AuthorSignature { get; set; } = string.Empty; 
    }

    public class TodoItem
    {
        public int ID { get; set; }
        public string Task { get; set; } = string.Empty;
        public bool Done { get; set; } = false;
    }

    public class AlarmRecord
    {
        public int ID { get; set; }
        public string Time { get; set; } = string.Empty; 
        public string Message { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}
