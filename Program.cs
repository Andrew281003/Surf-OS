using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace SurfOS2;

[SupportedOSPlatform("windows")]
internal class Program
{
    public static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        RetroConsole.Initialize();
        Core_Engine.MaximizeWindow();
        RetroConsole.BootSequence();
        Cloud_Manager.StartInBackground();

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
            Install_Setup.Install_WizardP1();
            return;
        }

        LoadSettings(Path.Combine(targetPath, "options.json"));
    }

    private static bool IsSurfOsInstallation(string path)
    {
        return File.Exists(Path.Combine(path, "installer_feedback.json"));
    }

    public static void LoadSettings(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine("Error: options.json is missing! 🚨");
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

                options.NumRun = Import.Variables.numRun;
                JsonStorage.Write(filePath, options);
                string? newRecoveryCode =
                    Recovery_Manager.EnsureConfigured(options, filePath);

                if (newRecoveryCode is not null)
                {
                    Recovery_Manager.DisplayRecoveryCode(newRecoveryCode);
                }
            }

            LoadUserDatabase(Path.Combine(
                Path.GetDirectoryName(filePath) ?? string.Empty,
                "database.json"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading OS files! ({ex.Message}) 🚨");
            return;
        }

        Login_Manager.ShowLoginScreen();
    }

    private static void LoadUserDatabase(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return;
        }

        try
        {
            Import.Variables.userDatabase =
                JsonStorage.Read<List<Import.DatabaseRecord>>(databasePath) ?? [];
        }
        catch (JsonException)
        {
            Import.DatabaseRecord? oldRecord =
                JsonStorage.Read<Import.DatabaseRecord>(databasePath);

            if (oldRecord is null)
            {
                return;
            }

            Import.Variables.userDatabase = [oldRecord];
            JsonStorage.Write(databasePath, Import.Variables.userDatabase);
        }
    }
}
