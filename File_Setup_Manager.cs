using System;
using System.IO;

namespace SurfOS2
{
    internal class File_Setup_Manager
    {
        public static void Main_Directory()
        {
            try
            {
                Import.Variables.uuid = Random.Shared.Next(10000, 99999);

                string mainFolder = Import.Variables.installPath;

                // --- Make Folders ---
                Directory.CreateDirectory(mainFolder);
                Directory.CreateDirectory(Path.Combine(mainFolder, "preVersions"));
                
                string packagesDirectory = Path.Combine(mainFolder, "Packages");
                Directory.CreateDirectory(packagesDirectory);
                RetroConsole.Spinner("FORMATTING SYSTEM DIRECTORIES", 350);

                // 1. Write Database (Now as a List!)
                List<Import.DatabaseRecord> dbList =
                [
                    new()
                    {
                        ID = Import.Variables.uuid,
                        Username = Import.Variables.userName,
                        Password = Import.Variables.userPassword,
                        Admin = $"{Import.Variables.uuid}::{Import.Variables.machineName}"
                    }
                ];
                Import.Variables.userDatabase = dbList;
                JsonStorage.Write(Path.Combine(mainFolder, "database.json"), dbList);
                RetroConsole.Spinner("WRITING USER DATABASE", 300);

                // 2. Write System Options
                Import.SystemOptions optionsData = new()
                {
                    InstallPath = Import.Variables.installPath,
                    UserName = Import.Variables.userName,
                    MachineName = Import.Variables.machineName,
                    Uuid = Import.Variables.uuid,
                    NumRun = Import.Variables.numRun,
                    PackageOption = Import.Variables.packageOption,
                    DefaultTheme = Import.Variables.defaultTheme,
                    TimeZone = Import.Variables.timeZone
                };
                string recoveryCode =
                    Recovery_Manager.ConfigureNewInstallation(optionsData);
                JsonStorage.Write(Path.Combine(mainFolder, "options.json"), optionsData);
                RetroConsole.Spinner("WRITING SYSTEM CONFIGURATION", 300);

                // 3. Write Installer Feedback flag
                File.WriteAllText(Path.Combine(mainFolder, "installer_feedback.json"), "{\"Installed\": true}");

                // 4. Seed Default Theme Packs
                var holyPack = new Import.SurfTheme {
                    ThemeName = "HolySurf",
                    WindowTitle = "HolySurf OS Environment",
                    TargetColor = "DarkYellow",
                    AsciiArt = Packages_IMPORT.Packages_Import.holysurf_PRINT
                };
                JsonStorage.Write(Path.Combine(packagesDirectory, "HolySurf.json"), holyPack);

                var unholyPack = new Import.SurfTheme {
                    ThemeName = "UnHolySurf",
                    WindowTitle = "UnHolySurf OS Environment",
                    TargetColor = "Red",
                    AsciiArt = Packages_IMPORT.Packages_Import.unholysurf_PRINT
                };
                JsonStorage.Write(Path.Combine(packagesDirectory, "UnHolySurf.json"), unholyPack);
                RetroConsole.ProgressBar("COPYING THEME PACKAGES", 18, 15);

                // Proceed to show UI
                Screen_Print.Print_Selected_Package();
                Recovery_Manager.DisplayRecoveryCode(recoveryCode);
            }
            catch (Exception error) when (error.Message.Contains("access"))
            {
                Console.Clear();
                Console.WriteLine($"I'm so sorry. I cannot start the setup. Error: \n\n{error.Message}\n");
                Console.WriteLine("Please start the program as an administrator.");
                Console.ReadKey();
                Console.WriteLine("Shutting down...");
                System.Threading.Thread.Sleep(1000);
                Environment.Exit(0);
            }
        }
    }
}
