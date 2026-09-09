using System.Diagnostics;
using System.Text;

namespace SurfOS2;

internal static class KernelPanic
{
    public static void ShowAndHandle(
        Exception exception,
        string moduleName,
        string recoverySuggestion)
    {
        string crashId = GenerateCrashId();
        DateTime timestamp = DateTime.Now;
        string stopCode = CreateStopCode(exception);
        string reportPath = SaveCrashReport(
            exception,
            moduleName,
            recoverySuggestion,
            crashId,
            stopCode,
            timestamp);

        KernelLog.Error(
            "panic",
            $"{stopCode} in {moduleName}; crash ID {crashId}; report {reportPath}");

        DrawPanicScreen(
            exception,
            moduleName,
            recoverySuggestion,
            crashId,
            stopCode,
            timestamp,
            reportPath);

        while (true)
        {
            ConsoleKey key = Console.ReadKey(intercept: true).Key;
            if (key == ConsoleKey.Enter)
            {
                KernelLog.Warning("panic", $"reboot requested from crash screen {crashId}");
                Console.ResetColor();
                Console.Clear();
#pragma warning disable CA1416
                Program.Main();
#pragma warning restore CA1416
                Environment.Exit(0);
            }

            if (key == ConsoleKey.Escape)
            {
                KernelLog.Warning("panic", $"shutdown requested from crash screen {crashId}");
                Console.ResetColor();
                Console.Clear();
                RetroConsole.ShutdownSequence();
                Environment.Exit(1);
            }
        }
    }

    private static void DrawPanicScreen(
        Exception exception,
        string moduleName,
        string recoverySuggestion,
        string crashId,
        string stopCode,
        DateTime timestamp,
        string reportPath)
    {
        Console.Clear();
        Console.CursorVisible = false;
        Console.BackgroundColor = ConsoleColor.DarkRed;
        Console.ForegroundColor = ConsoleColor.White;

        int width = Math.Clamp(Console.WindowWidth - 2, 76, 118);
        string horizontal = new('=', width);

        Console.WriteLine(horizontal);
        WriteCentered("SURFOS KERNEL PANIC", width);
        Console.WriteLine(horizontal);
        Console.WriteLine();
        WriteField("STOP CODE", stopCode);
        WriteField("Exception type", exception.GetType().FullName ?? exception.GetType().Name);
        WriteField("Module/file", moduleName);
        WriteField("Message", exception.Message);
        WriteField("Timestamp", timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
        WriteField("Recovery", recoverySuggestion);
        WriteField("Crash ID", crashId);
        WriteField("Report", reportPath);
        Console.WriteLine();
        Console.WriteLine(horizontal);
        Console.WriteLine("Press ENTER to reboot SurfOS, or ESC to shutdown.");
        Console.WriteLine(horizontal);
        Console.ResetColor();
    }

    private static string SaveCrashReport(
        Exception exception,
        string moduleName,
        string recoverySuggestion,
        string crashId,
        string stopCode,
        DateTime timestamp)
    {
        string root = string.IsNullOrWhiteSpace(Import.Variables.installPath)
            ? Environment.CurrentDirectory
            : Import.Variables.installPath;

        string crashDirectory = Path.Combine(root, "logs", "crashes");
        Directory.CreateDirectory(crashDirectory);

        string reportPath = Path.Combine(crashDirectory, $"{crashId}.log");
        StringBuilder report = new();
        report.AppendLine("SurfOS Kernel Panic Report");
        report.AppendLine("==========================");
        report.AppendLine($"Crash ID           : {crashId}");
        report.AppendLine($"Timestamp          : {timestamp:O}");
        report.AppendLine($"STOP CODE          : {stopCode}");
        report.AppendLine($"Exception type     : {exception.GetType().FullName}");
        report.AppendLine($"Module/file        : {moduleName}");
        report.AppendLine($"Message            : {exception.Message}");
        report.AppendLine($"Recovery suggestion: {recoverySuggestion}");
        report.AppendLine($"User               : {Import.Variables.userName}");
        report.AppendLine($"Install path       : {Import.Variables.installPath}");
        report.AppendLine();
        report.AppendLine("Stack trace");
        report.AppendLine("-----------");
        report.AppendLine(exception.ToString());

        File.WriteAllText(reportPath, report.ToString());
        return reportPath;
    }

    private static string CreateStopCode(Exception exception)
    {
        string typeName = exception.GetType().Name
            .Replace("Exception", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        return string.IsNullOrWhiteSpace(typeName)
            ? "KERNEL_FAULT"
            : $"KERNEL_{typeName}_FAULT";
    }

    private static string GenerateCrashId()
    {
        return $"KP-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..31].ToUpperInvariant();
    }

    private static void WriteCentered(string text, int width)
    {
        int leftPadding = Math.Max(0, (width - text.Length) / 2);
        Console.WriteLine(new string(' ', leftPadding) + text);
    }

    private static void WriteField(string label, string value)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"{label,-16}: ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(value);
    }
}
