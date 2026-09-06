using System.Text.Json;

namespace SurfOS2;

internal sealed class BiosOptions
{
    public bool BootAnimationEnabled { get; set; } = true;
    public bool SafeModeDefault { get; set; }
    public bool StartupSoundEnabled { get; set; } = true;
    public string DiagnosticVerbosity { get; set; } = "Normal";
}

internal class BIOS
{
    private const int UiWidth = 96;
    private const int MainPaneWidth = 64;
    private const int HelpPaneWidth = UiWidth - MainPaneWidth - 1;

    private static readonly string[] MainSections =
    [
        "System Information",
        "Boot Options",
        "Console Settings",
        "Theme Verification",
        "Storage Diagnostics",
        "Reset SurfOS Settings",
        "Exit Saving Changes",
        "Exit Without Saving"
    ];

    private static BiosOptions _loadedOptions = new();
    private static string _biosPath = Path.Combine(Environment.CurrentDirectory, "bios.json");

    public static BiosOptions CurrentOptions => _loadedOptions;

    public static BiosOptions LoadOptions(string installPath)
    {
        _biosPath = Path.Combine(installPath, "bios.json");

        if (!File.Exists(_biosPath))
        {
            _loadedOptions = new BiosOptions();
            SaveOptions(_loadedOptions);
            return Clone(_loadedOptions);
        }

        try
        {
            _loadedOptions = JsonStorage.Read<BiosOptions>(_biosPath) ?? new BiosOptions();
            NormalizeOptions(_loadedOptions);
        }
        catch
        {
            _loadedOptions = new BiosOptions();
            SaveOptions(_loadedOptions);
        }

        return Clone(_loadedOptions);
    }

    public static void ShowSetupScreen()
    {
        BiosOptions workingOptions = Clone(_loadedOptions);
        int selectedIndex = 0;
        bool resetSystemSettings = false;

        while (true)
        {
            DrawMainScreen(selectedIndex, workingOptions);
            ConsoleKey key = Console.ReadKey(intercept: true).Key;

            switch (key)
            {
                case ConsoleKey.UpArrow:
                    selectedIndex = selectedIndex == 0
                        ? MainSections.Length - 1
                        : selectedIndex - 1;
                    break;

                case ConsoleKey.DownArrow:
                    selectedIndex = (selectedIndex + 1) % MainSections.Length;
                    break;

                case ConsoleKey.Enter:
                    if (RunSection(selectedIndex, workingOptions, ref resetSystemSettings))
                    {
                        return;
                    }

                    break;

                case ConsoleKey.Escape:
                    return;
            }
        }
    }

    public static void Boot(string error, int times)
    {
        if (error != null)
        {
            Console.Clear();
            Console.WriteLine("Please re-launch the OS");
        }
    }

    private static bool RunSection(
        int selectedIndex,
        BiosOptions workingOptions,
        ref bool resetSystemSettings)
    {
        switch (selectedIndex)
        {
            case 0:
                ShowSystemInformation();
                return false;

            case 1:
                EditBootOptions(workingOptions);
                return false;

            case 2:
                EditConsoleSettings(workingOptions);
                return false;

            case 3:
                ShowThemeVerification();
                return false;

            case 4:
                ShowStorageDiagnostics(workingOptions);
                return false;

            case 5:
                ResetSurfOsSettings(workingOptions, ref resetSystemSettings);
                return false;

            case 6:
                SaveOptions(workingOptions);
                if (resetSystemSettings)
                {
                    SaveSystemSettingsReset();
                }

                _loadedOptions = Clone(workingOptions);
                ApplyRuntimeOptions(_loadedOptions);
                KernelLog.Success("bios", "BIOS changes saved");
                return true;

            case 7:
                ApplyRuntimeOptions(_loadedOptions);
                KernelLog.Warning("bios", "BIOS exited without saving");
                return true;

            default:
                return false;
        }
    }

    private static void DrawMainScreen(int selectedIndex, BiosOptions options)
    {
        BeginBiosScreen();
        DrawFirmwareHeader("Main");
        WriteSplitLine("", "Item Specific Help");
        WriteSplitLine("  SURF BIOS INFORMATION", new string('-', HelpPaneWidth - 4));
        WriteSplitLine("", "");

        for (int index = 0; index < MainSections.Length; index++)
        {
            bool selected = index == selectedIndex;
            string label = $"  {MainSections[index],-28} [ {GetSectionSummary(index, options)} ]";
            string help = GetSectionHelp(selectedIndex, index);
            WriteSelectableSplitLine(label, help, selected, index >= 6);
        }

        for (int row = MainSections.Length; row < 15; row++)
        {
            WriteSplitLine("", GetSectionHelp(selectedIndex, row));
        }

        DrawFirmwareFooter("[Up/Down] Choose Row    [Enter] Open / Confirm    [Esc] Exit Without Saving");
        Console.ResetColor();
    }

    private static string GetSectionHelp(int selectedIndex, int row)
    {
        string[] help = selectedIndex switch
        {
            0 => ["View machine, runtime,", "and operating system", "information."],
            1 => ["Configure startup", "animation and the", "default boot mode."],
            2 => ["Configure console", "sound and diagnostic", "display verbosity."],
            3 => ["Verify the active", "SurfOS theme package", "and signature."],
            4 => ["Inspect SurfOS files,", "storage access, and", "disk information."],
            5 => ["Stage a reset of", "non-account SurfOS", "settings."],
            6 => ["Commit firmware", "settings and return", "to the boot menu."],
            7 => ["Discard unsaved", "changes and return", "to the boot menu."],
            _ => []
        };

        int offset = row - selectedIndex;
        return offset >= 0 && offset < help.Length ? help[offset] : string.Empty;
    }

    private static string GetSectionSummary(int sectionIndex, BiosOptions options)
    {
        return sectionIndex switch
        {
            0 => $"{Import.Variables.machineName} / boot #{Import.Variables.numRun}",
            1 => $"Animation {OnOffShort(options.BootAnimationEnabled)}, safe default {OnOffShort(options.SafeModeDefault)}",
            2 => $"Startup beep {OnOffShort(options.StartupSoundEnabled)}",
            3 => $"{Import.Variables.defaultTheme}.json",
            4 => $"Verbosity {options.DiagnosticVerbosity}",
            5 => "Stage default settings reset",
            6 => "Write bios.json and return",
            7 => "Discard unsaved changes",
            _ => string.Empty
        };
    }

    private static void ShowSystemInformation()
    {
        DrawDetailScreen("SYSTEM INFORMATION");
        WriteDetail("Machine name", Import.Variables.machineName);
        WriteDetail("Install path", Import.Variables.installPath);
        WriteDetail("Default theme", Import.Variables.defaultTheme);
        WriteDetail("Time zone", Import.Variables.timeZone);
        WriteDetail("Run count", Import.Variables.numRun.ToString());
        WriteDetail("Logical CPUs", Environment.ProcessorCount.ToString());
        WriteDetail("OS version", Environment.OSVersion.ToString());
        WaitForKey();
    }

    private static void EditBootOptions(BiosOptions options)
    {
        string[] labels =
        [
            "Boot animation",
            "Safe mode default"
        ];
        int selectedIndex = 0;

        while (true)
        {
            DrawOptionEditor(
                "BOOT OPTIONS",
                labels,
                selectedIndex,
                index => index == 0
                    ? OnOff(options.BootAnimationEnabled)
                    : OnOff(options.SafeModeDefault));

            ConsoleKey key = Console.ReadKey(intercept: true).Key;
            if (HandleEditorKey(key, labels.Length, ref selectedIndex))
            {
                return;
            }

            if (key is ConsoleKey.Enter or ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
            {
                if (selectedIndex == 0)
                {
                    options.BootAnimationEnabled = !options.BootAnimationEnabled;
                }
                else
                {
                    options.SafeModeDefault = !options.SafeModeDefault;
                }
            }
        }
    }

    private static void EditConsoleSettings(BiosOptions options)
    {
        string[] labels =
        [
            "Startup sound/beep",
            "Diagnostic verbosity"
        ];
        int selectedIndex = 0;

        while (true)
        {
            DrawOptionEditor(
                "CONSOLE SETTINGS",
                labels,
                selectedIndex,
                index => index == 0
                    ? OnOff(options.StartupSoundEnabled)
                    : options.DiagnosticVerbosity);

            ConsoleKey key = Console.ReadKey(intercept: true).Key;
            if (HandleEditorKey(key, labels.Length, ref selectedIndex))
            {
                return;
            }

            if (key is ConsoleKey.Enter or ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
            {
                if (selectedIndex == 0)
                {
                    options.StartupSoundEnabled = !options.StartupSoundEnabled;
                }
                else
                {
                    options.DiagnosticVerbosity =
                        options.DiagnosticVerbosity.Equals("Normal", StringComparison.OrdinalIgnoreCase)
                            ? "Verbose"
                            : "Normal";
                }
            }
        }
    }

    private static void ShowThemeVerification()
    {
        DrawDetailScreen("THEME VERIFICATION");

        if (string.IsNullOrEmpty(Import.Variables.defaultTheme))
        {
            WriteStatus("OK", "Default OS appearance is active; no theme package is required.");
            WaitForKey();
            return;
        }

        string themePath = Path.Combine(
            Import.Variables.installPath,
            "Packages",
            $"{Import.Variables.defaultTheme}.json");

        WriteDetail("Theme path", themePath);

        if (!File.Exists(themePath))
        {
            WriteStatus("WARN", "Theme package is missing.");
            WaitForKey();
            return;
        }

        try
        {
            Import.SurfTheme? theme =
                JsonSerializer.Deserialize<Import.SurfTheme>(File.ReadAllText(themePath));

            if (theme is null)
            {
                WriteStatus("WARN", "Theme file is empty or invalid.");
                WaitForKey();
                return;
            }

            bool legitimate = Core_Engine.IsThemeLegit(theme);
            WriteDetail("Theme name", theme.ThemeName);
            WriteDetail("Window title", theme.WindowTitle);
            WriteDetail("Foreground", theme.TargetColor);
            WriteDetail("Background", theme.BackgroundColor);
            WriteDetail("Prompt style", theme.PromptStyle);
            WriteStatus(
                legitimate ? "OK" : "WARN",
                legitimate
                    ? "Theme signature verified."
                    : "Theme signature does not match official SurfOS data.");
        }
        catch (Exception ex)
        {
            WriteStatus("WARN", ex.Message);
        }

        WaitForKey();
    }

    private static void ShowStorageDiagnostics(BiosOptions options)
    {
        DrawDetailScreen("STORAGE DIAGNOSTICS");

        string installPath = Import.Variables.installPath;
        WriteDetail("Install path", installPath);
        CheckPath("options.json", Path.Combine(installPath, "options.json"), isDirectory: false);
        CheckPath("database.json", Path.Combine(installPath, "database.json"), isDirectory: false);
        CheckPath("installer_feedback.json", Path.Combine(installPath, "installer_feedback.json"), isDirectory: false);
        CheckPath("Packages", Path.Combine(installPath, "Packages"), isDirectory: true);
        CheckPath("bios.json", _biosPath, isDirectory: false);

        try
        {
            string probePath = Path.Combine(installPath, $".surfos_bios_probe_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "ok");
            string probeResult = File.ReadAllText(probePath);
            File.Delete(probePath);
            WriteStatus(probeResult == "ok" ? "OK" : "WARN", "Read/write probe completed.");
        }
        catch (Exception ex)
        {
            WriteStatus("WARN", $"Read/write probe failed: {ex.Message}");
        }

        if (options.DiagnosticVerbosity.Equals("Verbose", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                DriveInfo drive = new(Path.GetPathRoot(installPath) ?? installPath);
                WriteDetail("Drive format", drive.DriveFormat);
                WriteDetail("Free space", $"{drive.AvailableFreeSpace / 1024 / 1024} MB");
                WriteDetail("Total size", $"{drive.TotalSize / 1024 / 1024} MB");
            }
            catch (Exception ex)
            {
                WriteStatus("WARN", $"Drive details unavailable: {ex.Message}");
            }
        }

        WaitForKey();
    }

    private static void ResetSurfOsSettings(
        BiosOptions workingOptions,
        ref bool resetSystemSettings)
    {
        DrawDetailScreen("RESET SURFOS SETTINGS");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("This stages BIOS options plus non-account SurfOS preferences for reset.");
        Console.WriteLine("User accounts, passwords, mail, and recovery codes are preserved.\n");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("Press R to stage reset, or any other key to cancel: ");

        if (Console.ReadKey(intercept: true).Key != ConsoleKey.R)
        {
            return;
        }

        workingOptions.BootAnimationEnabled = true;
        workingOptions.SafeModeDefault = false;
        workingOptions.StartupSoundEnabled = true;
        workingOptions.DiagnosticVerbosity = "Normal";
        resetSystemSettings = true;

        Import.Variables.defaultTheme = "HolySurf";
        Import.Variables.timeZone = "Local";
        Import.Variables.packageOption = 0;

        Console.WriteLine("\nReset staged. Choose Exit Saving Changes to persist it.");
        WaitForKey();
    }

    private static void SaveSystemSettingsReset()
    {
        string optionsPath = Path.Combine(Import.Variables.installPath, "options.json");
        if (File.Exists(optionsPath))
        {
            Import.SystemOptions? systemOptions = JsonStorage.Read<Import.SystemOptions>(optionsPath);
            if (systemOptions is not null)
            {
                systemOptions.DefaultTheme = "HolySurf";
                systemOptions.TimeZone = "Local";
                systemOptions.PackageOption = 0;
                JsonStorage.Write(optionsPath, systemOptions);

                Import.Variables.defaultTheme = systemOptions.DefaultTheme;
                Import.Variables.timeZone = systemOptions.TimeZone;
                Import.Variables.packageOption = systemOptions.PackageOption;
            }
        }
    }

    private static void DrawOptionEditor(
        string title,
        string[] labels,
        int selectedIndex,
        Func<int, string> valueProvider)
    {
        DrawDetailScreen(title);
        WriteBiosLine("  Use Up/Down to select. Enter or Left/Right changes the highlighted value.");
        WriteBiosLine("");

        for (int index = 0; index < labels.Length; index++)
        {
            bool selected = index == selectedIndex;
            string row = $"  {labels[index],-38} [ {valueProvider(index),-10} ]";
            WriteSelectableLine(row, selected);
        }

        FillBiosBody(10);
        DrawFirmwareFooter("[Up/Down] Choose Row    [Left/Right/Enter] Modify    [Esc] Return");
        Console.ResetColor();
    }

    private static bool HandleEditorKey(ConsoleKey key, int itemCount, ref int selectedIndex)
    {
        switch (key)
        {
            case ConsoleKey.UpArrow:
                selectedIndex = selectedIndex == 0 ? itemCount - 1 : selectedIndex - 1;
                return false;

            case ConsoleKey.DownArrow:
                selectedIndex = (selectedIndex + 1) % itemCount;
                return false;

            case ConsoleKey.Escape:
                return true;

            default:
                return false;
        }
    }

    private static void SaveOptions(BiosOptions options)
    {
        NormalizeOptions(options);
        Directory.CreateDirectory(Path.GetDirectoryName(_biosPath) ?? Environment.CurrentDirectory);
        JsonStorage.Write(_biosPath, options);
    }

    private static void ApplyRuntimeOptions(BiosOptions options)
    {
        Import.Variables.safeMode = options.SafeModeDefault;
        if (!options.BootAnimationEnabled || options.SafeModeDefault)
        {
            RetroConsole.AnimationsEnabled = false;
        }
    }

    private static void CheckPath(string label, string path, bool isDirectory)
    {
        bool exists = isDirectory ? Directory.Exists(path) : File.Exists(path);
        WriteStatus(exists ? "OK" : "WARN", $"{label,-24} {(exists ? path : "missing")}");
    }

    private static void DrawDetailScreen(string title)
    {
        BeginBiosScreen();
        DrawFirmwareHeader(GetTabForTitle(title));
        WriteBiosLine($"  {title}");
        WriteBiosLine(new string('-', UiWidth - 4));
        WriteBiosLine("");
    }

    private static void WriteDetail(string label, string value)
    {
        ResetBiosColors();
        Console.Write($"  {label,-25}: ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(Truncate(value, UiWidth - 31).PadRight(Math.Max(0, UiWidth - 31)));
    }

    private static void WriteStatus(string status, string message)
    {
        ResetBiosColors();
        Console.ForegroundColor = status == "OK" ? ConsoleColor.White : ConsoleColor.Yellow;
        Console.Write($"  [{status,-4}] ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(Truncate(message, UiWidth - 12).PadRight(Math.Max(0, UiWidth - 12)));
    }

    private static void WaitForKey()
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine();
        DrawFirmwareFooter("Press any key to return to the firmware menu");
        Console.ResetColor();
        Console.ReadKey(intercept: true);
    }

    private static void BeginBiosScreen()
    {
        ResetBiosColors();
        Console.Clear();
        Console.Title = "Surf BIOS Setup";
    }

    private static void ResetBiosColors()
    {
        Console.BackgroundColor = ConsoleColor.Blue;
        Console.ForegroundColor = ConsoleColor.White;
    }

    private static void DrawFirmwareHeader(string activeTab)
    {
        WriteBiosLine("Surf BIOS Setup Utility - Copyright (C) 2026 SurfOS Firmware, Inc.");
        WriteBiosLine(new string('-', UiWidth));

        string[] tabs = ["Main", "Advanced (Customization)", "Security & Privacy", "Boot", "Exit"];
        foreach (string tab in tabs)
        {
            bool active = tab.Equals(activeTab, StringComparison.OrdinalIgnoreCase);
            if (active)
            {
                Console.BackgroundColor = ConsoleColor.Gray;
                Console.ForegroundColor = ConsoleColor.Black;
            }
            else
            {
                ResetBiosColors();
            }

            Console.Write($" {tab} ");
        }

        ResetBiosColors();
        Console.WriteLine(new string(' ', Math.Max(0, UiWidth - Console.CursorLeft)));
        WriteBiosLine(new string('-', UiWidth));
    }

    private static string GetTabForTitle(string title)
    {
        return title switch
        {
            "BOOT OPTIONS" => "Boot",
            "CONSOLE SETTINGS" => "Advanced (Customization)",
            "THEME VERIFICATION" => "Security & Privacy",
            "RESET SURFOS SETTINGS" => "Exit",
            _ => "Main"
        };
    }

    private static void WriteSplitLine(string left, string right)
    {
        ResetBiosColors();
        Console.Write(Pad(left, MainPaneWidth));
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("|");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(Pad(right, HelpPaneWidth));
    }

    private static void WriteSelectableSplitLine(string left, string right, bool selected, bool warning)
    {
        if (selected)
        {
            Console.BackgroundColor = ConsoleColor.Gray;
            Console.ForegroundColor = ConsoleColor.Black;
        }
        else
        {
            ResetBiosColors();
            Console.ForegroundColor = warning ? ConsoleColor.Yellow : ConsoleColor.White;
        }

        Console.Write(Pad(left, MainPaneWidth));
        ResetBiosColors();
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write("|");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(Pad(right, HelpPaneWidth));
    }

    private static void WriteSelectableLine(string text, bool selected)
    {
        if (selected)
        {
            Console.BackgroundColor = ConsoleColor.Gray;
            Console.ForegroundColor = ConsoleColor.Black;
        }
        else
        {
            ResetBiosColors();
        }

        Console.WriteLine(Pad(text, UiWidth));
        ResetBiosColors();
    }

    private static void WriteBiosLine(string text)
    {
        ResetBiosColors();
        Console.WriteLine(Pad(text, UiWidth));
    }

    private static void FillBiosBody(int lines)
    {
        for (int index = 0; index < lines; index++)
        {
            WriteBiosLine("");
        }
    }

    private static void DrawFirmwareFooter(string navigation)
    {
        ResetBiosColors();
        WriteBiosLine(new string('-', UiWidth));
        WriteBiosLine($"  Navigate: {navigation}");
        WriteBiosLine("  Save: Select 'Exit Saving Changes'    Esc: Return / Exit Setup");
    }

    private static string Pad(string text, int width)
    {
        return Truncate(text, width).PadRight(width);
    }

    private static string Truncate(string text, int width)
    {
        return text.Length <= width ? text : text[..Math.Max(0, width - 3)] + "...";
    }

    private static string OnOff(bool enabled)
    {
        return enabled ? "Enabled" : "Disabled";
    }

    private static string OnOffShort(bool enabled)
    {
        return enabled ? "ON" : "OFF";
    }

    private static BiosOptions Clone(BiosOptions options)
    {
        return new BiosOptions
        {
            BootAnimationEnabled = options.BootAnimationEnabled,
            SafeModeDefault = options.SafeModeDefault,
            StartupSoundEnabled = options.StartupSoundEnabled,
            DiagnosticVerbosity = options.DiagnosticVerbosity
        };
    }

    private static void NormalizeOptions(BiosOptions options)
    {
        if (!options.DiagnosticVerbosity.Equals("Verbose", StringComparison.OrdinalIgnoreCase))
        {
            options.DiagnosticVerbosity = "Normal";
        }
        else
        {
            options.DiagnosticVerbosity = "Verbose";
        }
    }
}
