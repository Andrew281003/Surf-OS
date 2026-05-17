using System.Text.Json;

namespace SurfOS2
{
    internal class Time_Manager
    {
        // 🌟 Calculates the exact time based on your setup choice
        public static DateTime GetCurrentTime()
        {
            if (Import.Variables.timeZone == "Local") return DateTime.Now;

            try
            {
                string tzId = "UTC";
                if (Import.Variables.timeZone == "CET") tzId = "Central European Standard Time";
                else if (Import.Variables.timeZone == "EST") tzId = "Eastern Standard Time";

                TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            }
            catch
            {
                return DateTime.Now; // Failsafe
            }
        }

        public static void ShowClock()
        {
            DateTime now = GetCurrentTime();
            Console.WriteLine($"\n🕰️  Current Date & Time: {now.ToString("F")}");
            Console.WriteLine($"🌐  Active Timezone: {Import.Variables.timeZone}");
        }

        public static void ShowCalendar()
        {
            DateTime now = GetCurrentTime();
            Console.WriteLine($"\n--- 📅 Calendar: {now.ToString("MMMM yyyy")} ---");
            Console.WriteLine(" Su Mo Tu We Th Fr Sa");

            DateTime firstDay = new DateTime(now.Year, now.Month, 1);
            int daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
            int startDayOfWeek = (int)firstDay.DayOfWeek;

            for (int i = 0; i < startDayOfWeek; i++) Console.Write("   ");

            for (int day = 1; day <= daysInMonth; day++)
            {
                if (day == now.Day)
                {
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.BackgroundColor = ConsoleColor.White;
                }

                Console.Write($"{day,3}");
                Screen_Print.ResetColors(); // Fix background right after

                if ((day + startDayOfWeek) % 7 == 0) Console.WriteLine();
            }
            Console.WriteLine();
        }

        public static void ManageAlarm(string[] args, int cmdIndex)
        {
            string alarmsPath = Path.Combine(Import.Variables.installPath, "alarms.json");

            if (File.Exists(alarmsPath))
            {
                string json = File.ReadAllText(alarmsPath);
                Import.Variables.alarms = JsonSerializer.Deserialize<System.Collections.Generic.List<Import.AlarmRecord>>(json) ?? new System.Collections.Generic.List<Import.AlarmRecord>();
            }

            string action = args.Length > cmdIndex + 1 ? args[cmdIndex + 1].ToLower() : "list";

            if (action == "add" && args.Length > cmdIndex + 2)
            {
                string timeStr = args[cmdIndex + 2]; // e.g., "14:30"
                string message = args.Length > cmdIndex + 3 ? string.Join(" ", args, cmdIndex + 3, args.Length - (cmdIndex + 3)) : "Alarm!";

                int nextId = Import.Variables.alarms.Count > 0 ? Import.Variables.alarms[^1].ID + 1 : 1;
                Import.Variables.alarms.Add(new Import.AlarmRecord { ID = nextId, Time = timeStr, Message = message, IsActive = true });

                File.WriteAllText(alarmsPath, JsonSerializer.Serialize(Import.Variables.alarms, new JsonSerializerOptions { WriteIndented = true }));
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ Alarm set for {timeStr}: {message}");
            }
            else if (action == "remove" && args.Length > cmdIndex + 2)
            {
                if (int.TryParse(args[cmdIndex + 2], out int id))
                {
                    Import.Variables.alarms.RemoveAll(a => a.ID == id);
                    File.WriteAllText(alarmsPath, JsonSerializer.Serialize(Import.Variables.alarms, new JsonSerializerOptions { WriteIndented = true }));
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"🗑️ Alarm {id} removed.");
                }
            }
            else
            {
                Console.WriteLine("\n--- ⏰ Active Alarms ---");
                if (Import.Variables.alarms.Count == 0) Console.WriteLine("No alarms set.");
                foreach (var a in Import.Variables.alarms)
                {
                    string status = a.IsActive ? "[ON] " : "[OFF]";
                    Console.WriteLine($"{a.ID}. {status} {a.Time} - {a.Message}");
                }
            }
        }

        // =========================================================================
        // 🌟 FIX: Tell the compiler this method uses Windows-specific audio features
        // =========================================================================
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public static void StartAlarmDaemon()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    DateTime now = GetCurrentTime();
                    string currentMinute = now.ToString("HH:mm");

                    foreach (var alarm in Import.Variables.alarms)
                    {
                        if (alarm.IsActive && alarm.Time == currentMinute)
                        {
                            // Trigger the alarm! No more platform warnings! 🎉
                            Console.Beep(800, 300);
                            Console.Beep(1000, 300);
                            Console.Beep(800, 300);

                            alarm.IsActive = false;
                            string alarmsPath = Path.Combine(Import.Variables.installPath, "alarms.json");
                            File.WriteAllText(alarmsPath, JsonSerializer.Serialize(Import.Variables.alarms, new JsonSerializerOptions { WriteIndented = true }));
                        }
                    }
                    Thread.Sleep(30000); // Check every 30 seconds
                }
            });
        }
    }
}