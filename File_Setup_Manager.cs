using System;
using System.IO;
using System.Text.Json;

namespace SurfOS2
{
    internal class File_Setup_Manager
    {
        public static void Main_Directory()
        {
            try
            {
                Random random = new Random();
                Import.Variables.uuid = random.Next(10000, 99999);

                string main_folder = Import.Variables.installPath;

                // --- Make Folders ---
                Directory.CreateDirectory(main_folder);
                Directory.CreateDirectory(Path.Combine(main_folder, "preVersions"));
                
                string packagesDirectory = Path.Combine(main_folder, "Packages");
                Directory.CreateDirectory(packagesDirectory);

                JsonSerializerOptions jsonFormatting = new JsonSerializerOptions { WriteIndented = true };

                // 1. Write Database (Now as a List!)
                var dbList = new System.Collections.Generic.List<Import.DatabaseRecord>();
                dbList.Add(new Import.DatabaseRecord
                {
                    ID = Import.Variables.uuid,
                    Username = Import.Variables.userName,
                    Password = Import.Variables.userPassword,
                    Admin = $"{Import.Variables.uuid}::{Import.Variables.machineName}"
                });
                File.WriteAllText(Path.Combine(main_folder, "database.json"), JsonSerializer.Serialize(dbList, jsonFormatting));

                // 2. Write System Options
                var optionsData = new Import.SystemOptions
                {
                    InstallPath = Import.Variables.installPath,
                    UserName = Import.Variables.userName,
                    MachineName = Import.Variables.machineName,
                    Uuid = Import.Variables.uuid,
                    NumRun = Import.Variables.numRun,
                    PackageOption = Import.Variables.packageOption,
                    TimeZone = Import.Variables.timeZone
                };
                File.WriteAllText(Path.Combine(main_folder, "options.json"), JsonSerializer.Serialize(optionsData, jsonFormatting));

                // 3. Write Installer Feedback flag
                File.WriteAllText(Path.Combine(main_folder, "installer_feedback.json"), "{\"Installed\": true}");

                // 4. Seed Default Theme Packs
                var holyPack = new Import.SurfTheme {
                    ThemeName = "HolySurf",
                    WindowTitle = "HolySurf OS Environment",
                    TargetColor = "DarkYellow",
                    AsciiArt = Packages_IMPORT.Packages_Import.holysurf_PRINT
                };
                File.WriteAllText(Path.Combine(packagesDirectory, "HolySurf.json"), JsonSerializer.Serialize(holyPack, jsonFormatting));

                var unholyPack = new Import.SurfTheme {
                    ThemeName = "UnHolySurf",
                    WindowTitle = "UnHolySurf OS Environment",
                    TargetColor = "Red",
                    AsciiArt = Packages_IMPORT.Packages_Import.unholysurf_PRINT
                };
                File.WriteAllText(Path.Combine(packagesDirectory, "UnHolySurf.json"), JsonSerializer.Serialize(unholyPack, jsonFormatting));

                // Proceed to show UI
                Screen_Print.Print_Selected_Package();
            }
            catch (Exception error)
            {
                Console.Clear();
                Console.WriteLine($"I'm so sorry. I cannot start the setup. Error: \n\n{error.Message}\n");
                Console.WriteLine("I recommend trying to install it to a different location (like Desktop). Press any key to retry.");
                Console.ReadKey();
                Program.Main();
            }
        }
    }
}