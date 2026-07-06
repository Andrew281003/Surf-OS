using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace SurfOS2;

[SupportedOSPlatform("windows")]
internal class Program
{
    public static void Main()
    {
        try
        {
            Console.Title = "SurfOS - BIOS";
            Console.OutputEncoding = Encoding.UTF8;
            RetroConsole.Initialize();
            KernelLog.Info("boot", "SurfOS process started");
            Core_Engine.MaximizeWindow();
            KernelLog.Success("boot", "console initialized");

            string desktopPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "SurfOS");
            string documentsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "SurfOS");

            string? targetPath = new[] { desktopPath, documentsPath, @"C:\SurfOS" }
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
            KernelLog.Error("crash", ex.ToString());
            KernelPanic.ShowAndHandle(
                ex,
                "Program.cs",
                "Reboot SurfOS. If the panic repeats, start Safe Mode and inspect dmesg errors.");
        }
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
                Import.Variables.installPath = options.InstallPath ?? string.Empty;
                Import.Variables.defaultTheme =
                    string.IsNullOrEmpty(options.DefaultTheme) ? "HolySurf" : options.DefaultTheme;
                Import.Variables.timeZone =
                    string.IsNullOrEmpty(options.TimeZone) ? "Local" : options.TimeZone;
                KernelLog.Success("boot", "system options loaded");

                options.NumRun = Import.Variables.numRun;
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
