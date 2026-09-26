using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace SurfOS2;

internal static class SurfOsApplication
{
    internal static bool RebootRequested { get; set; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(
        IntPtr windowHandle,
        string text,
        string caption,
        uint type);

    public static void Run()
    {
        do
        {
            RebootRequested = false;
            RunBoot();
        } while (RebootRequested);
    }

    private static void RunBoot()
    {
        try
        {
            InitializeBootstrapLogging();
            Console.Title = "SurfOS - BIOS";
            Console.OutputEncoding = Encoding.UTF8;
            RetroConsole.Initialize();
            KernelLog.Info("boot", "SurfOS process started");
            Core_Engine.MaximizeWindow();
            KernelLog.Success("boot", "console initialized");

            string defaultPath = Partition_Manager.DefaultDirectoryInstallPath;
            string desktopPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "SurfOS");
            string documentsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "SurfOS");

            string? vhdxInstallPath = null;
            if (OperatingSystem.IsWindows() && Partition_Manager.HasMountConfiguration)
            {
                vhdxInstallPath = Partition_Manager.TryMountConfiguredVhdx(out string mountError);
                if (vhdxInstallPath is null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("SurfOS could not mount its system drive.");
                    Console.WriteLine(mountError);
                    Console.ResetColor();
                    Console.WriteLine("The host partition table was not modified.");
                    return;
                }
            }

            string? targetPath = new string?[]
                { vhdxInstallPath, defaultPath, desktopPath, documentsPath,
                    OperatingSystem.IsWindows() ? @"C:\SurfOS" : null }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .FirstOrDefault(IsSurfOsInstallation);

            if (targetPath is null)
            {
                KernelLog.Warning("boot", "no existing SurfOS installation found; launching installer");
                RetroConsole.BootSequence();
                Install_Setup.Install_WizardP1();
                return;
            }

            Import.Variables.installPath = targetPath;
            KernelLog.Initialize(targetPath);
            KernelLog.Info("boot", $"installation located at {targetPath}");

            BiosOptions biosOptions = BIOS.LoadOptions(targetPath);
            KernelLog.Info("boot", "BIOS options loaded");
            if (biosOptions.BootAnimationEnabled)
            {
                KernelLog.Info("boot", "running boot animation sequence");
                RetroConsole.BootSequence();
            }
            else
            {
                KernelLog.Info("boot", "boot animation disabled by BIOS");
                Console.Clear();
            }

            LoadSettings(Path.Combine(targetPath, "options.json"));
        }
        catch (Exception ex)
        {
            try
            {
                KernelLog.Error("crash", ex.ToString());
                KernelPanic.ShowAndHandle(
                    ex,
                    "src/SurfOS.Core/Application/SurfOsApplication.cs",
                    "Reboot SurfOS. If the panic repeats, start Safe Mode and inspect dmesg errors.");
            }
            catch (Exception panicError)
            {
                ReportUnrenderableStartupCrash(ex, panicError);
            }
        }
    }

    private static void InitializeBootstrapLogging()
    {
        string bootstrapRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SurfOS");
        KernelLog.Initialize(bootstrapRoot);
    }

    private static void ReportUnrenderableStartupCrash(
        Exception startupError,
        Exception panicError)
    {
        string reportPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SurfOS",
            "startup-crash.log");
        string report = $"""
            SurfOS startup failure
            Timestamp: {DateTime.Now:O}
            Executable: {Environment.ProcessPath}
            OS: {Environment.OSVersion}
            Runtime: {Environment.Version}

            Original startup error:
            {startupError}

            Panic-screen error:
            {panicError}

            """;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.AppendAllText(reportPath, report + Environment.NewLine);
        }
        catch
        {
            reportPath = "The fallback crash log could not be written.";
        }

        string message =
            $"SurfOS could not start.\n\n{startupError.Message}\n\nCrash log: {reportPath}";
        try
        {
            Console.Error.WriteLine(message);
        }
        catch
        {
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                MessageBoxW(IntPtr.Zero, message, "SurfOS Startup Error", 0x10);
            }
        }
        catch
        {
        }

        Environment.ExitCode = 1;
    }

    private static bool IsSurfOsInstallation(string path)
    {
        return File.Exists(Path.Combine(path, "installer_feedback.json"));
    }

    public static void LoadSettings(string filePath)
    {
        if (!File.Exists(filePath))
        {
            KernelLog.Error("boot", "options.json is missing");
            Console.WriteLine("Error: options.json is missing.");
            return;
        }

        try
        {
            Import.SystemOptions? options = JsonStorage.Read<Import.SystemOptions>(filePath);
            if (options is not null)
            {
                Import.Variables.numRun = options.NumRun + 1;
                Import.Variables.packageOption = options.PackageOption;
                Import.Variables.uuid = options.Uuid;
                Import.Variables.userName = options.UserName ?? string.Empty;
                string actualInstallPath = Path.GetDirectoryName(filePath) ?? options.InstallPath;
                Import.Variables.installPath = actualInstallPath;
                Import.Variables.vhdxHostDirectory = string.IsNullOrWhiteSpace(options.VhdxHostDirectory)
                    ? Import.Variables.vhdxHostDirectory
                    : options.VhdxHostDirectory;
                Import.Variables.vhdxPath = string.IsNullOrWhiteSpace(options.VhdxPath)
                    ? Import.Variables.vhdxPath
                    : options.VhdxPath;
                string? actualDriveRoot = Path.GetPathRoot(actualInstallPath);
                Import.Variables.partitionDriveLetter = !string.IsNullOrWhiteSpace(actualDriveRoot)
                    ? actualDriveRoot[0].ToString()
                    : options.PartitionDriveLetter;
                Import.Variables.defaultTheme =
                    options.DefaultTheme ?? "HolySurf";
                Import.Variables.setupMode = string.IsNullOrWhiteSpace(options.SetupMode)
                    ? "Advanced Setup"
                    : options.SetupMode;
                Import.Variables.timeZone =
                    string.IsNullOrEmpty(options.TimeZone) ? "Local" : options.TimeZone;
                Import.Variables.language = options.Language;
                Import.Variables.keyboardLayout = options.KeyboardLayout;
                Import.Variables.networkProfile = options.NetworkProfile;
                Import.Variables.machineName = string.IsNullOrWhiteSpace(options.MachineName) ? "SurfOS" : options.MachineName;
                Import.Variables.updateChannel = options.UpdateChannel;
                Import.Variables.performanceProfile = options.PerformanceProfile;
                Import.Variables.systemFootprint = string.IsNullOrWhiteSpace(options.SystemFootprint)
                    ? "Standard"
                    : options.SystemFootprint;
                Import.Variables.telemetryLevel = string.IsNullOrWhiteSpace(options.TelemetryLevel)
                    ? (options.TelemetryEnabled ? "Basic" : "Off")
                    : options.TelemetryLevel;
                Import.Variables.surfCloudAccount = options.SurfCloudAccount;
                Import.Variables.partitionLabel = options.PartitionLabel;
                Import.Variables.partitionFileSystem = options.PartitionFileSystem;
                Import.Variables.partitionSizeGb = options.PartitionSizeGb;
                Import.Variables.swapSizeGb = options.SwapSizeGb;
                Import.Variables.sleepTimeoutMinutes = options.SleepTimeoutMinutes;
                Import.Variables.automaticUpdates = options.AutomaticUpdates;
                Import.Variables.firewallEnabled = options.FirewallEnabled;
                Import.Variables.cloudServicesEnabled = options.CloudServicesEnabled;
                Import.Variables.surfCloudSignedIn = options.SurfCloudSignedIn;
                Import.Variables.telemetryEnabled = options.TelemetryEnabled;
                Import.Variables.crashReportsEnabled = options.CrashReportsEnabled;
                Import.Variables.locationServicesEnabled = options.LocationServicesEnabled;
                Import.Variables.partitionEncryption = options.PartitionEncryption;
                Import.Variables.developerToolsEnabled = options.DeveloperToolsEnabled;
                Import.Variables.sampleContentEnabled = options.SampleContentEnabled;
                KernelLog.Success("boot", "system options loaded");

                options.NumRun = Import.Variables.numRun;
                options.InstallPath = Import.Variables.installPath;
                options.PartitionDriveLetter = Import.Variables.partitionDriveLetter;
                JsonStorage.Write(filePath, options);
                KernelLog.Info("boot", $"boot count updated to {Import.Variables.numRun}");
                string? newRecoveryCode =
                    Recovery_Manager.EnsureConfigured(options, filePath);

                if (newRecoveryCode is not null)
                {
                    KernelLog.Warning("recovery", "new recovery code generated");
                    Recovery_Manager.DisplayRecoveryCode(newRecoveryCode);
                }
            }

            LoadUserDatabase(Path.Combine(
                Path.GetDirectoryName(filePath) ?? string.Empty,
                "database.json"));
        }
        catch (Exception ex)
        {
            KernelLog.Error("boot", $"error reading OS files: {ex}");
            Console.WriteLine($"Error reading OS files! ({ex.Message})");
            return;
        }

        KernelLog.Info("boot", "opening boot manager");
        BootMode bootMode = Boot_Manager.ShowBootMenu(filePath);
        KernelLog.Info("boot", $"boot manager selected {bootMode}");
        if (bootMode == BootMode.RecoveryMode)
        {
            Login_Manager.ShowRecoveryScreen();
        }

        Login_Manager.ShowLoginScreen();
    }

    private static void LoadUserDatabase(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            KernelLog.Warning("boot", "database.json is missing");
            return;
        }

        try
        {
            Import.Variables.userDatabase =
                JsonStorage.Read<List<Import.DatabaseRecord>>(databasePath) ?? [];
            KernelLog.Success("boot", $"loaded {Import.Variables.userDatabase.Count} user profile(s)");
        }
        catch (JsonException)
        {
            KernelLog.Warning("boot", "legacy single-record database detected");
            Import.DatabaseRecord? oldRecord =
                JsonStorage.Read<Import.DatabaseRecord>(databasePath);

            if (oldRecord is null)
            {
                KernelLog.Error("boot", "database migration failed: old record was null");
                return;
            }

            Import.Variables.userDatabase = [oldRecord];
            JsonStorage.Write(databasePath, Import.Variables.userDatabase);
            KernelLog.Success("boot", "migrated user database to list format");
        }
    }
}
