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
        public static int packageOption;

        public static string defaultTheme = "HolySurf";
        public static string activePromptStyle = "Standard";
        public static string timeZone = "Local"; 

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
        public string UserName { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public int Uuid { get; set; }
        public int NumRun { get; set; }
        public int PackageOption { get; set; }
        public string DefaultTheme { get; set; } = "HolySurf"; 
        public string TimeZone { get; set; } = "Local";
    }

    public class DatabaseRecord
    {
        public int ID { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Admin { get; set; } = string.Empty;

        // 🌟 NEW ECONOMY & MAIL FEATURES
        public int SurfCoins { get; set; } = 0;
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