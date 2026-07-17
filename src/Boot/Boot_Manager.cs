using System.Net.NetworkInformation;
using System.Text.Json;

namespace SurfOS2;

internal enum BootMode
{
    BootSurfOs,
    SafeMode,
    RecoveryMode
}

internal static class Boot_Manager
{
    private static readonly string[] MenuItems =
    [
        "Boot SurfOS",
        "Safe Mode",
        "Recovery Mode",
        "BIOS Setup",
        "Diagnostics",
        "Shutdown"
    ];

    public static BootMode ShowBootMenu(string optionsPath)
    {
        int selectedIndex = BIOS.CurrentOptions.SafeModeDefault ? 1 : 0;

        while (true)
        {
            DrawMenu(selectedIndex);
            ConsoleKey key = Console.ReadKey(intercept: true).Key;

            switch (key)
            {
                case ConsoleKey.UpArrow:
                    selectedIndex = selectedIndex == 0
                        ? MenuItems.Length - 1
                        : selectedIndex - 1;
                    break;

                case ConsoleKey.DownArrow:
                    selectedIndex = (selectedIndex + 1) % MenuItems.Length;
                    break;

                case ConsoleKey.Enter:
                    BootMode? selectedMode = HandleSelection(selectedIndex, optionsPath);
                    if (selectedMode.HasValue)
                    {
                        return selectedMode.Value;
                    }

                    break;
            }
        }
    }

    private static void DrawMenu(int selectedIndex)
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("======================================");
        Console.WriteLine("          SURFOS BOOT MANAGER         ");
        Console.WriteLine("======================================");
        Console.ResetColor();
        Console.WriteLine("Use arrow keys to choose an option, then press Enter.\n");
        if (BIOS.CurrentOptions.SafeModeDefault)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("BIOS default: Safe Mode\n");
            Console.ResetColor();
        }

        for (int index = 0; index < MenuItems.Length; index++)
        {
            bool selected = index == selectedIndex;
            Console.ForegroundColor = selected ? ConsoleColor.Black : ConsoleColor.Gray;
            Console.BackgroundColor = selected ? ConsoleColor.Green : ConsoleColor.Black;
            Console.WriteLine($"{(selected ? ">" : " ")} {index + 1}. {MenuItems[index]}");
            Console.ResetColor();
        }
    }

    private static BootMode? HandleSelection(int selectedIndex, string optionsPath)
    {
        switch (selectedIndex)
        {
            case 0:
                Import.Variables.safeMode = false;
                KernelLog.Success("boot", "normal SurfOS boot selected");
                return BootMode.BootSurfOs;

            case 1:
                Import.Variables.safeMode = true;
                RetroConsole.AnimationsEnabled = false;
                KernelLog.Warning("boot", "safe mode boot selected");
                return BootMode.SafeMode;

            case 2:
                Import.Variables.safeMode = false;
                KernelLog.Warning("boot", "recovery mode selected");
                return BootMode.RecoveryMode;

            case 3:
                KernelLog.Info("boot", "BIOS setup opened");
                BIOS.ShowSetupScreen();
                return null;

            case 4:
                KernelLog.Info("diagnostics", "boot diagnostics opened");
                RunDiagnostics(optionsPath);
                return null;

            case 5:
                KernelLog.Warning("boot", "shutdown selected from boot manager");
                Console.Clear();
                ServiceManager.StopAll();
                RetroConsole.ShutdownSequence();
                Environment.Exit(0);
                return null;

            default:
                return null;
        }
    }

    private static void RunDiagnostics(string optionsPath)
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("======================================");
        Console.WriteLine("          SURFOS DIAGNOSTICS          ");
        Console.WriteLine("======================================\n");
        Console.ResetColor();

        PrintCheck("CPU", Environment.ProcessorCount > 0, $"{Environment.ProcessorCount} logical processor(s)");
        PrintCheck("Memory", CheckMemory(out string memoryDetail), memoryDetail);
        PrintCheck("Keyboard", CheckKeyboard(out string keyboardDetail), keyboardDetail);
        PrintCheck("Network adapter", CheckNetworkAdapter(out string networkDetail), networkDetail);
        PrintCheck("System storage", CheckStorage(optionsPath, out string storageDetail), storageDetail);
        PrintCheck("Theme package", CheckTheme(out string themeDetail), themeDetail);
        KernelLog.Success("diagnostics", "boot diagnostics completed");

        Console.WriteLine("\nPress any key to return to the boot manager...");
        Console.ReadKey(intercept: true);
    }

    private static bool CheckMemory(out string detail)
    {
        try
        {
            long availableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            bool passing = availableMemory <= 0 || availableMemory >= 64L * 1024L * 1024L;
            detail = availableMemory <= 0
                ? "available memory not reported"
                : $"{availableMemory / 1024 / 1024} MB available to runtime";
            return passing;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    private static bool CheckKeyboard(out string detail)
    {
        try
        {
            _ = Console.KeyAvailable;
            detail = Console.IsInputRedirected ? "input redirected" : "console input available";
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    private static bool CheckNetworkAdapter(out string detail)
    {
        try
        {
            bool online = NetworkInterface.GetAllNetworkInterfaces()
                .Any(networkInterface =>
                    networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    networkInterface.OperationalStatus == OperationalStatus.Up);

            detail = online ? "active adapter detected" : "no active adapter detected";
            return online;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    private static bool CheckStorage(string optionsPath, out string detail)
    {
        string installPath = Path.GetDirectoryName(optionsPath) ?? Import.Variables.installPath;
        string[] requiredPaths =
        [
            Path.Combine(installPath, "options.json"),
            Path.Combine(installPath, "database.json"),
            Path.Combine(installPath, "installer_feedback.json"),
            Path.Combine(installPath, "Packages")
        ];

        string? missingPath = requiredPaths.FirstOrDefault(path =>
            !File.Exists(path) && !Directory.Exists(path));

        if (missingPath is not null)
        {
            detail = $"missing {Path.GetFileName(missingPath)}";
            return false;
        }

        try
        {
            string probePath = Path.Combine(installPath, $".surfos_diag_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
            detail = installPath;
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    private static bool CheckTheme(out string detail)
    {
        string themePath = Path.Combine(
            Import.Variables.installPath,
            "Packages",
            $"{Import.Variables.defaultTheme}.json");

        if (!File.Exists(themePath))
        {
            detail = $"missing {Import.Variables.defaultTheme}.json";
            return false;
        }

        try
        {
            string json = File.ReadAllText(themePath);
            Import.SurfTheme? theme = JsonSerializer.Deserialize<Import.SurfTheme>(json);
            if (theme is null)
            {
                detail = "theme JSON was empty";
                return false;
            }

            bool legitimate = Core_Engine.IsThemeLegit(theme);
            detail = legitimate ? $"{theme.ThemeName} signature OK" : $"{theme.ThemeName} signature warning";
            return legitimate;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    private static void PrintCheck(string label, bool passed, string detail)
    {
        Console.ForegroundColor = passed ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.Write(passed ? "[OK]   " : "[WARN] ");
        Console.ResetColor();
        Console.WriteLine($"{label,-16} {detail}");
    }
}
