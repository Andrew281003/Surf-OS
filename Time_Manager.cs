using System.Runtime.Versioning;

namespace SurfOS2;

internal static class Time_Manager
{
    private static readonly object AlarmLock = new();
    private static string? _cachedTimeZoneName;
    private static TimeZoneInfo? _cachedTimeZone;

    public static DateTime GetCurrentTime()
    {
        string timeZoneName = Import.Variables.timeZone;
        if (timeZoneName == "Local")
        {
            return DateTime.Now;
        }

        try
        {
            TimeZoneInfo timeZone = GetTimeZone(timeZoneName);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        }
        catch
        {
            return DateTime.Now;
        }
    }

    private static TimeZoneInfo GetTimeZone(string timeZoneName)
    {
        if (_cachedTimeZone is not null && _cachedTimeZoneName == timeZoneName)
        {
            return _cachedTimeZone;
        }

        string timeZoneId = timeZoneName switch
        {
            "CET" => "Central European Standard Time",
            "EST" => "Eastern Standard Time",
            _ => "UTC"
        };

        _cachedTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        _cachedTimeZoneName = timeZoneName;
        return _cachedTimeZone;
    }

    public static void ShowClock()
    {
        RetroConsole.Spinner("READING REAL-TIME CLOCK", 180, ConsoleColor.Cyan);
        DateTime now = GetCurrentTime();
        Console.WriteLine($"\n🕰️  Current Date & Time: {now:F}");
        Console.WriteLine($"🌐  Active Timezone: {Import.Variables.timeZone}");
    }

    public static void ShowCalendar()
    {
        RetroConsole.Spinner("CALCULATING DATE TABLE", 180, ConsoleColor.Cyan);
        DateTime now = GetCurrentTime();
        Console.WriteLine($"\n--- 📅 Calendar: {now:MMMM yyyy} ---");
        Console.WriteLine(" Su Mo Tu We Th Fr Sa");

        int daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        int startDayOfWeek = (int)new DateTime(now.Year, now.Month, 1).DayOfWeek;

        Console.Write(new string(' ', startDayOfWeek * 3));

        for (int day = 1; day <= daysInMonth; day++)
        {
            if (day == now.Day)
            {
                Console.ForegroundColor = ConsoleColor.Black;
                Console.BackgroundColor = ConsoleColor.White;
            }

            Console.Write($"{day,3}");
            Screen_Print.ResetColors();

            if ((day + startDayOfWeek) % 7 == 0)
            {
                Console.WriteLine();
            }
        }

        Console.WriteLine();
    }

    public static void ManageAlarm(string[] args, int commandIndex)
    {
        string alarmsPath = GetAlarmsPath();
        EnsureAlarmsLoaded(alarmsPath);

        string action = args.Length > commandIndex + 1
            ? args[commandIndex + 1].ToLowerInvariant()
            : "list";

        lock (AlarmLock)
        {
            if (action == "add" && args.Length > commandIndex + 2)
            {
                AddAlarm(args, commandIndex, alarmsPath);
            }
            else if (action == "remove" &&
                     args.Length > commandIndex + 2 &&
                     int.TryParse(args[commandIndex + 2], out int id))
            {
                Import.Variables.alarms.RemoveAll(alarm => alarm.ID == id);
                SaveAlarms(alarmsPath);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"🗑️ Alarm {id} removed.");
            }
            else
            {
                ListAlarms();
            }
        }
    }

    private static void AddAlarm(string[] args, int commandIndex, string alarmsPath)
    {
        string time = args[commandIndex + 2];
        string message = args.Length > commandIndex + 3
            ? string.Join(" ", args, commandIndex + 3, args.Length - commandIndex - 3)
            : "Alarm!";

        int nextId = Import.Variables.alarms.Count == 0
            ? 1
            : Import.Variables.alarms.Max(alarm => alarm.ID) + 1;

        Import.Variables.alarms.Add(new Import.AlarmRecord
        {
            ID = nextId,
            Time = time,
            Message = message,
            IsActive = true
        });

        SaveAlarms(alarmsPath);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✅ Alarm set for {time}: {message}");
    }

    private static void ListAlarms()
    {
        Console.WriteLine("\n--- ⏰ Active Alarms ---");
        if (Import.Variables.alarms.Count == 0)
        {
            Console.WriteLine("No alarms set.");
        }

        foreach (Import.AlarmRecord alarm in Import.Variables.alarms)
        {
            string status = alarm.IsActive ? "[ON] " : "[OFF]";
            Console.WriteLine($"{alarm.ID}. {status} {alarm.Time} - {alarm.Message}");
        }
    }

    [SupportedOSPlatform("windows")]
    public static void TriggerDueAlarms()
    {
        string alarmsPath = GetAlarmsPath();
        EnsureAlarmsLoaded(alarmsPath);
        TriggerDueAlarms(alarmsPath);
    }

    [SupportedOSPlatform("windows")]
    private static void TriggerDueAlarms(string alarmsPath)
    {
        string currentMinute = GetCurrentTime().ToString("HH:mm");

        lock (AlarmLock)
        {
            bool changed = false;
            foreach (Import.AlarmRecord alarm in Import.Variables.alarms)
            {
                if (!alarm.IsActive || alarm.Time != currentMinute)
                {
                    continue;
                }

                try
                {
                    Console.Beep(800, 300);
                    Console.Beep(1000, 300);
                    Console.Beep(800, 300);
                }
                catch
                {
                    // Audio is optional and may be unavailable in some terminals.
                }

                alarm.IsActive = false;
                changed = true;
            }

            if (changed)
            {
                SaveAlarms(alarmsPath);
            }
        }
    }

    private static void EnsureAlarmsLoaded(string alarmsPath)
    {
        lock (AlarmLock)
        {
            if (Import.Variables.alarms.Count > 0 || !File.Exists(alarmsPath))
            {
                return;
            }

            Import.Variables.alarms =
                JsonStorage.Read<List<Import.AlarmRecord>>(alarmsPath) ?? [];
        }
    }

    private static string GetAlarmsPath()
    {
        return Path.Combine(Import.Variables.installPath, "alarms.json");
    }

    private static void SaveAlarms(string alarmsPath)
    {
        JsonStorage.Write(alarmsPath, Import.Variables.alarms);
    }
}
