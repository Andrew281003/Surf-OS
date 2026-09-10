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

    public static void ShowClock(string[] args, int commandIndex)
    {
        bool utc = args.Skip(commandIndex + 1).Contains("-u");
        bool iso = args.Skip(commandIndex + 1).Contains("--iso");
        if (args.Skip(commandIndex + 1)
            .Any(argument => argument is not "-u" and not "--iso"))
        {
            Console.WriteLine("Usage: clock [-u|--iso]");
            return;
        }

        RetroConsole.Spinner("READING REAL-TIME CLOCK", 180, ConsoleColor.Cyan);
        DateTime now = utc ? DateTime.UtcNow : GetCurrentTime();
        if (iso)
        {
            Console.WriteLine(now.ToString("O"));
            return;
        }

        Console.WriteLine($"\n🕰️  Current Date & Time: {now:F}");
        Console.WriteLine($"🌐  Active Timezone: {(utc ? "UTC" : Import.Variables.timeZone)}");
    }

    public static void ShowCalendar(string[] args, int commandIndex)
    {
        DateTime current = GetCurrentTime();
        int month = current.Month;
        int year = current.Year;
        int operandCount = args.Length - commandIndex - 1;
        if (operandCount > 2 ||
            (operandCount >= 1 && !int.TryParse(args[commandIndex + 1], out month)) ||
            (operandCount == 2 && !int.TryParse(args[commandIndex + 2], out year)) ||
            month is < 1 or > 12 ||
            year is < 1 or > 9999)
        {
            Console.WriteLine("Usage: calendar [month] [year]");
            return;
        }

        RetroConsole.Spinner("CALCULATING DATE TABLE", 180, ConsoleColor.Cyan);
        DateTime shown = new(year, month, 1);
        Console.WriteLine($"\n--- 📅 Calendar: {shown:MMMM yyyy} ---");
        Console.WriteLine(" Su Mo Tu We Th Fr Sa");

        int daysInMonth = DateTime.DaysInMonth(year, month);
        int startDayOfWeek = (int)shown.DayOfWeek;

        Console.Write(new string(' ', startDayOfWeek * 3));

        for (int day = 1; day <= daysInMonth; day++)
        {
            if (day == current.Day && month == current.Month && year == current.Year)
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
                int removed = Import.Variables.alarms.RemoveAll(alarm => alarm.ID == id);
                if (removed == 0)
                {
                    Console.WriteLine($"Alarm {id} was not found.");
                }
                else
                {
                    SaveAlarms(alarmsPath);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"🗑️ Alarm {id} removed.");
                }
            }
            else if (action is "enable" or "disable" &&
                     args.Length > commandIndex + 2 &&
                     int.TryParse(args[commandIndex + 2], out int toggleId))
            {
                Import.AlarmRecord? alarm =
                    Import.Variables.alarms.FirstOrDefault(item => item.ID == toggleId);
                if (alarm is null)
                {
                    Console.WriteLine($"Alarm {toggleId} was not found.");
                    return;
                }

                alarm.IsActive = action == "enable";
                SaveAlarms(alarmsPath);
                Console.WriteLine($"Alarm {toggleId} {action}d.");
            }
            else if (action == "list")
            {
                ListAlarms();
            }
            else
            {
                Console.WriteLine(
                    "Usage: alarm [list|add <HH:mm> [message]|remove <id>|enable <id>|disable <id>]");
            }
        }
    }

    private static void AddAlarm(string[] args, int commandIndex, string alarmsPath)
    {
        string time = args[commandIndex + 2];
        if (!TimeOnly.TryParseExact(time, "HH:mm", out _))
        {
            Console.WriteLine("alarm: time must use 24-hour HH:mm format.");
            return;
        }
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

    public static void TriggerDueAlarms()
    {
        string alarmsPath = GetAlarmsPath();
        EnsureAlarmsLoaded(alarmsPath);
        TriggerDueAlarms(alarmsPath);
    }

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
                    if (OperatingSystem.IsWindows())
                    {
                        Console.Beep(800, 300);
                        Console.Beep(1000, 300);
                        Console.Beep(800, 300);
                    }
                    else
                    {
                        Console.Write('\a');
                    }
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
