using System.Runtime.Versioning;

namespace SurfOS2;

internal static class Install_Setup
{
    private const int GuidedSteps = 6;
    private const int AdvancedSteps = 11;

    private enum InstallerMode
    {
        Guided,
        Advanced
    }

    [SupportedOSPlatform("windows")]
    public static void Install_WizardP1()
    {
        InstallerMode? mode = SelectSetupMode();
        if (mode is null)
        {
            ShowCancelled();
            return;
        }

        Import.Variables.setupMode = mode == InstallerMode.Guided
            ? "Guided Setup"
            : "Advanced Setup";

        bool confirmed;
        if (mode == InstallerMode.Guided)
        {
            ApplyGuidedDefaults();
            confirmed = RunGuidedSetup();
        }
        else
        {
            confirmed = RunAdvancedSetup();
        }

        if (!confirmed)
        {
            ShowCancelled();
            return;
        }

        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        RetroConsole.TypeLine("CREATING SURFOS SYSTEM DRIVE", 4);
        Console.ResetColor();
        if (!Partition_Manager.ProvisionFixedVhdx(out string storageError))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nSurfOS could not create its system drive:\n{storageError}");
            Console.ResetColor();
            Console.WriteLine("No changes were made to the Windows partition table.");
            Console.WriteLine("Press any key to exit setup.");
            Console.ReadKey(intercept: true);
            return;
        }

        File_Setup_Manager.Main_Directory();
        Login_Manager.ShowLoginScreen();
    }

    private static bool RunGuidedSetup()
    {
        ConfigureRegion(1, GuidedSteps);
        ConfigureAccount(2, GuidedSteps);
        if (!SelectInstallDirectory(3, GuidedSteps))
        {
            return false;
        }

        EnsureGuidedPartitionFitsDrive();
        SelectTheme(4, GuidedSteps);
        ConfigureSurfCloud(5, GuidedSteps, includeNetworkProfile: false);
        return ConfirmInstallation(6, GuidedSteps, guided: true);
    }

    private static bool RunAdvancedSetup()
    {
        ConfigureRegion(1, AdvancedSteps);
        ConfigureAccount(2, AdvancedSteps);
        if (!SelectInstallDirectory(3, AdvancedSteps))
        {
            return false;
        }

        ConfigurePartition(4, AdvancedSteps);
        SelectTheme(5, AdvancedSteps);
        ConfigureSurfCloud(6, AdvancedSteps, includeNetworkProfile: true);
        ConfigureSecurity(7, AdvancedSteps);
        ConfigureUpdatesAndPrivacy(8, AdvancedSteps);
        ConfigurePerformance(9, AdvancedSteps);
        ConfigureOptionalComponents(10, AdvancedSteps);
        return ConfirmInstallation(11, AdvancedSteps, guided: false);
    }

    private static InstallerMode? SelectSetupMode()
    {
        int selected = 0;
        while (true)
        {
            DrawSetupModeScreen(selected);
            ConsoleKey key = Console.ReadKey(intercept: true).Key;
            switch (key)
            {
                case ConsoleKey.UpArrow:
                case ConsoleKey.DownArrow:
                    selected = selected == 0 ? 1 : 0;
                    break;
                case ConsoleKey.D1:
                case ConsoleKey.NumPad1:
                    return InstallerMode.Guided;
                case ConsoleKey.D2:
                case ConsoleKey.NumPad2:
                    return InstallerMode.Advanced;
                case ConsoleKey.Enter:
                    return selected == 0 ? InstallerMode.Guided : InstallerMode.Advanced;
                case ConsoleKey.Escape:
                case ConsoleKey.Q:
                    return null;
            }
        }
    }

    private static void DrawSetupModeScreen(int selected)
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("========================================");
        Console.WriteLine("        Welcome to SurfOS Setup");
        Console.WriteLine("========================================");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("Choose your installation experience.\n");

        DrawModeChoice(
            selected == 0,
            "(1) Guided Setup (Recommended)",
            "    A simplified installation using",
            "    recommended settings.");
        Console.WriteLine();
        DrawModeChoice(
            selected == 1,
            "(2) Advanced Setup",
            "    Full control over every option.");

        Console.WriteLine();
        Console.WriteLine("----------------------------------------");
        Console.WriteLine("Arrow Keys: Navigate");
        Console.WriteLine("Enter: Select");
        Console.WriteLine("Esc: Cancel");
    }

    private static void DrawModeChoice(bool selected, params string[] lines)
    {
        Console.ForegroundColor = selected ? ConsoleColor.Black : ConsoleColor.Gray;
        Console.BackgroundColor = selected ? ConsoleColor.Cyan : ConsoleColor.Black;
        Console.WriteLine($"{(selected ? "> " : "  ")}{lines[0]}");
        Console.ResetColor();

        foreach (string line in lines.Skip(1))
        {
            Console.WriteLine($"  {line}");
        }
    }

    private static void ApplyGuidedDefaults()
    {
        Import.Variables.partitionSizeGb = Partition_Manager.DefaultSizeGb;
        Import.Variables.partitionLabel = "SURFOS";
        Import.Variables.partitionFileSystem = "SurfFS";
        Import.Variables.swapSizeGb = 2;
        Import.Variables.partitionEncryption = false;
        Import.Variables.networkProfile = "Private";
        Import.Variables.cloudServicesEnabled = true;
        Import.Variables.surfCloudSignedIn = false;
        Import.Variables.surfCloudAccount = string.Empty;
        Import.Variables.firewallEnabled = true;
        Import.Variables.updateChannel = "Stable";
        Import.Variables.automaticUpdates = true;
        Import.Variables.telemetryLevel = "Basic";
        Import.Variables.telemetryEnabled = true;
        Import.Variables.crashReportsEnabled = true;
        Import.Variables.locationServicesEnabled = true;
        Import.Variables.performanceProfile = "Balanced";
        Import.Variables.systemFootprint = "Standard";
        Import.Variables.sleepTimeoutMinutes = 15;
        Import.Variables.developerToolsEnabled = false;
        Import.Variables.sampleContentEnabled = true;
    }

    private static void ConfigureRegion(int step, int totalSteps)
    {
        StepHeader(step, totalSteps, "REGION AND INPUT");
        Import.Variables.language = Choose(
            "Display language",
            ["English (US)", "English (UK)", "German", "Hungarian", "Spanish"], 0);
        Import.Variables.keyboardLayout = Choose(
            "Keyboard layout",
            ["US", "UK", "German QWERTZ", "Hungarian QWERTZ", "Spanish"], 0);
        Import.Variables.timeZone = Choose(
            "System timezone",
            ["Local", "UTC", "CET", "EET", "EST", "PST"], 0);
    }

    private static void ConfigureAccount(int step, int totalSteps)
    {
        StepHeader(step, totalSteps, "IDENTITY AND ACCOUNT");
        Import.Variables.userName = ReadText("SurfOS username", Environment.UserName, 32);
        Import.Variables.machineName = ReadText(
            "Device name", $"SURF-{Environment.MachineName}", 32);

        while (true)
        {
            string password = ReadSecret("Create password (4+ characters)");
            if (password.Length < 4)
            {
                WriteWarning("Password must contain at least 4 characters.");
                continue;
            }

            string confirmation = ReadSecret("Confirm password");
            if (password == confirmation)
            {
                Import.Variables.userPassword = password;
                return;
            }

            WriteWarning("Passwords do not match. Try again.");
        }
    }

    private static bool SelectInstallDirectory(int step, int totalSteps)
    {
        StepHeader(step, totalSteps, "INSTALLATION LOCATION");
        int option = ChooseIndex(
            "Location for the fixed SurfOS VHDX backing file",
            ["System storage (recommended)", "Desktop", "Documents", "Cancel setup"], 0);

        Import.Variables.vhdxHostDirectory = option switch
        {
            0 => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SurfOS"),
            1 => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "SurfOS Storage"),
            2 => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SurfOS Storage"),
            _ => string.Empty
        };

        if (option == 3)
        {
            return false;
        }

        try
        {
            Import.Variables.partitionDriveLetter = Partition_Manager.GetPreferredDriveLetter();
            Import.Variables.vhdxPath = Path.Combine(
                Import.Variables.vhdxHostDirectory,
                "SurfOS-System.vhdx");
            Import.Variables.installPath = $"{Import.Variables.partitionDriveLetter}:\\";
            return true;
        }
        catch (IOException ex)
        {
            WriteWarning(ex.Message);
            return false;
        }
    }

    private static void ConfigurePartition(int step, int totalSteps)
    {
        StepHeader(step, totalSteps, "DISK AND PARTITION");
        long freeBytes = Partition_Manager.GetAvailableDriveBytes(Import.Variables.vhdxHostDirectory);
        int freeGb = (int)Math.Min(int.MaxValue, freeBytes / Partition_Manager.BytesPerGb);
        Console.WriteLine($"Host free space: {freeGb} GB");
        Console.WriteLine(
            "SurfOS will create a fixed-size VHDX, format it as NTFS, and mount it as " +
            $"{Import.Variables.partitionDriveLetter}:.");
        Console.WriteLine("It will appear as a separate drive in This PC without changing the physical partition table.\n");

        int preset = ChooseIndex(
            "Partition capacity",
            ["8 GB (minimal)", "15 GB (recommended)", "32 GB", "64 GB", "Custom size"], 1);
        int requestedSize = preset switch
        {
            0 => 8,
            1 => Partition_Manager.DefaultSizeGb,
            2 => 32,
            3 => 64,
            _ => ReadPartitionSize(freeGb)
        };

        Import.Variables.partitionSizeGb = FitPartitionToDrive(requestedSize, freeGb);
        Import.Variables.partitionLabel = ReadPartitionLabel();
        Import.Variables.partitionFileSystem = Choose(
            "Filesystem",
            ["SurfFS", "SurfFS Journaled", "SurfFS Compact"], 0);

        int maxSwap = Math.Max(0, Import.Variables.partitionSizeGb - 3);
        int[] validSwaps = new[] { 0, 1, 2, 4, 8 }
            .Where(value => value <= maxSwap)
            .ToArray();
        string[] swapLabels = validSwaps
            .Select(value => value == 0 ? "Disabled" : $"{value} GB")
            .ToArray();
        int defaultSwap = Array.IndexOf(validSwaps, 2);
        Import.Variables.swapSizeGb = validSwaps[ChooseIndex(
            "Swap space", swapLabels, Math.Max(0, defaultSwap))];
    }

    private static void EnsureGuidedPartitionFitsDrive()
    {
        long freeBytes = Partition_Manager.GetAvailableDriveBytes(Import.Variables.vhdxHostDirectory);
        int freeGb = (int)Math.Min(int.MaxValue, freeBytes / Partition_Manager.BytesPerGb);
        Import.Variables.partitionSizeGb = FitPartitionToDrive(
            Partition_Manager.DefaultSizeGb, freeGb, showWarning: false);
        string[] supportedProfiles = SystemFootprint_Manager.GetSupportedProfiles(
            Import.Variables.partitionSizeGb,
            Import.Variables.swapSizeGb);
        if (!supportedProfiles.Contains("Standard", StringComparer.OrdinalIgnoreCase))
        {
            Import.Variables.systemFootprint = supportedProfiles[0];
        }
    }

    private static int FitPartitionToDrive(int requestedSize, int freeGb, bool showWarning = true)
    {
        int allowedByDrive = Math.Max(Partition_Manager.MinimumSizeGb, freeGb - 1);
        if (freeGb <= 0 || requestedSize <= allowedByDrive)
        {
            return requestedSize;
        }

        if (showWarning)
        {
            WriteWarning($"Only {freeGb} GB is free. Partition capacity was adjusted to {allowedByDrive} GB.");
        }

        return allowedByDrive;
    }

    private static void SelectTheme(int step, int totalSteps)
    {
        StepHeader(step, totalSteps, "APPEARANCE");
        int theme = ChooseIndex("Default system theme", ["HolySurf", "UnHolySurf"], 0);
        Import.Variables.packageOption = theme + 1;
        Import.Variables.defaultTheme = theme == 0 ? "HolySurf" : "UnHolySurf";
    }

    private static void ConfigureSurfCloud(int step, int totalSteps, bool includeNetworkProfile)
    {
        StepHeader(step, totalSteps, "NETWORK AND SURFCLOUD");
        if (includeNetworkProfile)
        {
            Import.Variables.networkProfile = Choose(
                "Default network profile", ["Private", "Public", "Offline"], 0);
        }

        if (Import.Variables.networkProfile == "Offline")
        {
            Import.Variables.cloudServicesEnabled = false;
            Import.Variables.surfCloudSignedIn = false;
            Import.Variables.surfCloudAccount = string.Empty;
            Console.WriteLine("SurfCloud is unavailable while the Offline network profile is selected.");
            return;
        }

        Import.Variables.cloudServicesEnabled = AskYesNo(
            "Enable SurfCloud package and media services?", true);
        if (!Import.Variables.cloudServicesEnabled)
        {
            Import.Variables.surfCloudSignedIn = false;
            Import.Variables.surfCloudAccount = string.Empty;
            return;
        }

        int accessMode = ChooseIndex(
            "SurfCloud sign in (optional)",
            ["Continue without an account", "Link a SurfCloud identity"], 0);
        Import.Variables.surfCloudSignedIn = accessMode == 1;
        Import.Variables.surfCloudAccount = accessMode == 1
            ? ReadText("SurfCloud account name or email", Import.Variables.userName, 80)
            : string.Empty;

        if (Import.Variables.surfCloudSignedIn)
        {
            Console.WriteLine("Identity saved locally. Online credential verification is not yet available.");
        }
    }

    private static void ConfigureSecurity(int step, int totalSteps)
    {
        StepHeader(step, totalSteps, "SECURITY");
        Import.Variables.firewallEnabled = AskYesNo("Enable the SurfOS firewall?", true);
        Import.Variables.partitionEncryption = AskYesNo(
            "Enable simulated partition encryption metadata?", false);
        Console.WriteLine("Note: SurfOS is a simulator; this setting does not encrypt the Windows host drive.");
    }

    private static void ConfigureUpdatesAndPrivacy(int step, int totalSteps)
    {
        StepHeader(step, totalSteps, "UPDATES AND PRIVACY");
        Import.Variables.updateChannel = Choose(
            "Update channel", ["Stable", "Preview", "Long-term support"], 0);
        Import.Variables.automaticUpdates = AskYesNo("Install package updates automatically?", true);
        Import.Variables.telemetryLevel = Choose(
            "Telemetry level", ["Off", "Basic", "Full"], 0);
        Import.Variables.telemetryEnabled = Import.Variables.telemetryLevel != "Off";
        Import.Variables.crashReportsEnabled = AskYesNo("Save local crash reports?", true);
        Import.Variables.locationServicesEnabled = AskYesNo("Enable location-aware features?", false);
    }

    private static void ConfigurePerformance(int step, int totalSteps)
    {
        StepHeader(step, totalSteps, "POWER AND PERFORMANCE");
        Import.Variables.performanceProfile = Choose(
            "Performance profile", ["Balanced", "Performance", "Power saver"], 0);
        string timeout = Choose(
            "Idle sleep timeout",
            ["Never", "10 minutes", "15 minutes", "30 minutes", "60 minutes"], 3);
        Import.Variables.sleepTimeoutMinutes = timeout switch
        {
            "Never" => 0,
            "10 minutes" => 10,
            "15 minutes" => 15,
            "60 minutes" => 60,
            _ => 30
        };
    }

    private static void ConfigureOptionalComponents(int step, int totalSteps)
    {
        StepHeader(step, totalSteps, "OPTIONAL COMPONENTS");
        string[] footprintOptions = SystemFootprint_Manager.GetSupportedProfiles(
            Import.Variables.partitionSizeGb,
            Import.Variables.swapSizeGb);
        Import.Variables.systemFootprint = Choose(
            "System footprint",
            footprintOptions,
            Math.Max(0, Array.IndexOf(footprintOptions, "Standard")));
        Import.Variables.developerToolsEnabled = AskYesNo(
            "Install developer directories and SurfCode integration?", false);
        Import.Variables.sampleContentEnabled = AskYesNo(
            "Install sample playlist and welcome content?", true);
    }

    private static bool ConfirmInstallation(int step, int totalSteps, bool guided)
    {
        StepHeader(step, totalSteps, "INSTALLATION SUMMARY");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Setup Mode\n");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(Import.Variables.setupMode);
        if (guided)
        {
            Console.WriteLine("Using recommended system defaults.");
        }

        Console.ResetColor();
        Console.WriteLine("\n----------------------------------------\n");
        Console.WriteLine($"Language:          {Import.Variables.language}");
        Console.WriteLine($"Keyboard:          {Import.Variables.keyboardLayout}");
        Console.WriteLine($"Timezone:          {Import.Variables.timeZone}");
        Console.WriteLine($"User / device:     {Import.Variables.userName} / {Import.Variables.machineName}");
        Console.WriteLine($"Windows drive:     SurfOS ({Import.Variables.partitionDriveLetter}:)");
        Console.WriteLine($"Install location:  {Import.Variables.installPath}");
        Console.WriteLine($"VHDX backing file: {Import.Variables.vhdxPath}");
        Console.WriteLine($"Theme:             {Import.Variables.defaultTheme}");
        Console.WriteLine($"Partition:         {Import.Variables.partitionSizeGb} GB ({Import.Variables.partitionLabel})");
        Console.WriteLine($"Filesystem:        {Import.Variables.partitionFileSystem} on NTFS");
        Console.WriteLine($"Swap:              {Import.Variables.swapSizeGb} GB");
        Console.WriteLine($"Encryption:        {EnabledDisabled(Import.Variables.partitionEncryption)}");
        Console.WriteLine($"SurfCloud:         {FormatSurfCloud()}");
        Console.WriteLine($"Firewall:          {EnabledDisabled(Import.Variables.firewallEnabled)}");
        Console.WriteLine($"Updates:           {Import.Variables.updateChannel}, automatic {OnOff(Import.Variables.automaticUpdates)}");
        Console.WriteLine($"Telemetry:         {Import.Variables.telemetryLevel}");
        Console.WriteLine($"Crash reports:     {EnabledDisabled(Import.Variables.crashReportsEnabled)}");
        Console.WriteLine($"Location services: {EnabledDisabled(Import.Variables.locationServicesEnabled)}");
        Console.WriteLine($"Performance:       {Import.Variables.performanceProfile}");
        Console.WriteLine($"System footprint:  {Import.Variables.systemFootprint} ({SystemFootprint_Manager.FormatEstimatedSize(Import.Variables.systemFootprint, Import.Variables.swapSizeGb)})");
        Console.WriteLine($"Idle timeout:      {FormatSleep()}");
        Console.WriteLine($"Developer tools:   {(Import.Variables.developerToolsEnabled ? "Installed" : "Not installed")}");
        Console.WriteLine($"Sample content:    {(Import.Variables.sampleContentEnabled ? "Installed" : "Not installed")}\n");
        return AskYesNo(
            $"Create the fixed {Import.Variables.partitionSizeGb} GB SurfOS drive and install now?",
            true);
    }

    private static int ReadPartitionSize(int freeGb)
    {
        int upperLimit = Partition_Manager.MaximumSizeGb;
        if (freeGb > 0)
        {
            upperLimit = Math.Min(
                upperLimit,
                Math.Max(Partition_Manager.MinimumSizeGb, freeGb - 1));
        }

        while (true)
        {
            Console.Write($"Custom size in GB ({Partition_Manager.MinimumSizeGb}-{upperLimit}): ");
            if (int.TryParse(Console.ReadLine(), out int value) &&
                value >= Partition_Manager.MinimumSizeGb && value <= upperLimit)
            {
                return value;
            }

            WriteWarning("Enter a whole number inside the displayed range.");
        }
    }

    private static string ReadPartitionLabel()
    {
        while (true)
        {
            string label = ReadText("Partition label", "SURFOS", 16);
            if (label.All(character => char.IsLetterOrDigit(character) || character is '-' or '_'))
            {
                return label.ToUpperInvariant();
            }

            WriteWarning("Use only letters, numbers, hyphens, and underscores.");
        }
    }

    private static string Choose(string prompt, string[] options, int defaultIndex) =>
        options[ChooseIndex(prompt, options, defaultIndex)];

    private static int ChooseIndex(string prompt, string[] options, int defaultIndex)
    {
        while (true)
        {
            Console.WriteLine($"\n{prompt}:");
            for (int index = 0; index < options.Length; index++)
            {
                string marker = index == defaultIndex ? " (default)" : string.Empty;
                Console.WriteLine($"  [{index + 1}] {options[index]}{marker}");
            }

            Console.Write("Choice: ");
            string input = (Console.ReadLine() ?? string.Empty).Trim();
            if (input.Length == 0)
            {
                return defaultIndex;
            }

            if (int.TryParse(input, out int selected) && selected >= 1 && selected <= options.Length)
            {
                return selected - 1;
            }

            WriteWarning($"Choose a number from 1 to {options.Length}.");
        }
    }

    private static bool AskYesNo(string prompt, bool defaultValue)
    {
        while (true)
        {
            Console.Write($"{prompt} [{(defaultValue ? "Y/n" : "y/N")}]: ");
            string answer = (Console.ReadLine() ?? string.Empty).Trim();
            if (answer.Length == 0)
            {
                return defaultValue;
            }

            if (answer.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                answer.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (answer.Equals("n", StringComparison.OrdinalIgnoreCase) ||
                answer.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            WriteWarning("Enter Y or N.");
        }
    }

    private static string ReadText(string prompt, string defaultValue, int maximumLength)
    {
        while (true)
        {
            Console.Write($"{prompt} [{defaultValue}]: ");
            string value = (Console.ReadLine() ?? string.Empty).Trim();
            value = value.Length == 0 ? defaultValue : value;
            if (value.Length <= maximumLength &&
                value is not "." and not ".." &&
                value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            {
                return value;
            }

            WriteWarning($"Value must be 1-{maximumLength} characters and contain no path symbols.");
        }
    }

    private static string ReadSecret(string prompt)
    {
        Console.Write($"{prompt}: ");
        List<char> characters = [];
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return new string(characters.ToArray());
            }

            if (key.Key == ConsoleKey.Backspace && characters.Count > 0)
            {
                characters.RemoveAt(characters.Count - 1);
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar) && characters.Count < 128)
            {
                characters.Add(key.KeyChar);
                Console.Write('*');
            }
        }
    }

    private static void StepHeader(int step, int totalSteps, string title)
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"SURFOS {Import.Variables.setupMode.ToUpperInvariant()}  [{step}/{totalSteps}]");
        Console.WriteLine(new string('=', 58));
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(title);
        Console.WriteLine(new string('-', 58));
        Console.ResetColor();
    }

    private static void WriteWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static string OnOff(bool value) => value ? "On" : "Off";

    private static string EnabledDisabled(bool value) => value ? "Enabled" : "Disabled";

    private static string FormatSleep() => Import.Variables.sleepTimeoutMinutes == 0
        ? "Never"
        : $"{Import.Variables.sleepTimeoutMinutes} minutes";

    private static string FormatSurfCloud()
    {
        if (!Import.Variables.cloudServicesEnabled)
        {
            return "Disabled";
        }

        return Import.Variables.surfCloudSignedIn
            ? $"Enabled, identity {Import.Variables.surfCloudAccount}"
            : "Enabled, not signed in";
    }

    private static void ShowCancelled()
    {
        Console.Clear();
        Console.WriteLine("SurfOS setup was cancelled. No partition was created.");
    }
}
