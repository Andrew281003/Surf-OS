using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Text.Unicode;
using System.Text;

namespace SurfOS2
{
    internal class Program
    {
        public static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Core_Engine.MaximizeWindow();


            string desktopPath = Path.Combine("C:\\Users", Import.Variables.machineName, "Desktop", "SurfOS");
            string documentsPath = Path.Combine("C:\\Users", Import.Variables.machineName, "Documents", "SurfOS");
            string rootPath = "C:\\SurfOS";

            string targetPath = "";

            if (File.Exists(Path.Combine(desktopPath, "installer_feedback.json"))) targetPath = desktopPath;
            else if (File.Exists(Path.Combine(documentsPath, "installer_feedback.json"))) targetPath = documentsPath;
            else if (File.Exists(Path.Combine(rootPath, "installer_feedback.json"))) targetPath = rootPath;

            if (!string.IsNullOrEmpty(targetPath))
            {
                string optionsFile = Path.Combine(targetPath, "options.json");
                LoadSettings(optionsFile);
            }
            else
            {
                Install_Setup.Install_WizardP1();
            }
            Console.ReadLine();
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
                string jsonString = File.ReadAllText(filePath);
                var options = JsonSerializer.Deserialize<Import.SystemOptions>(jsonString);

                if (options != null)
                {
                    Import.Variables.numRun = options.NumRun + 1;
                    Import.Variables.packageOption = options.PackageOption;
                    Import.Variables.uuid = options.Uuid;
                    Import.Variables.userName = options.UserName ?? string.Empty;
                    Import.Variables.installPath = options.InstallPath ?? string.Empty;
                    Import.Variables.defaultTheme = string.IsNullOrEmpty(options.DefaultTheme) ? "HolySurf" : options.DefaultTheme;
                    Import.Variables.timeZone = string.IsNullOrEmpty(options.TimeZone) ? "Local" : options.TimeZone;

                    options.NumRun = Import.Variables.numRun;
                    File.WriteAllText(filePath, JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true }));
                }

                string dbPath = Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, "database.json");
                if (File.Exists(dbPath))
                {
                    string dbString = File.ReadAllText(dbPath);
                    try
                    {
                        Import.Variables.userDatabase = JsonSerializer.Deserialize<List<Import.DatabaseRecord>>(dbString) ?? new List<Import.DatabaseRecord>();
                    }
                    catch
                    {
                        var oldDb = JsonSerializer.Deserialize<Import.DatabaseRecord>(dbString);
                        if (oldDb != null)
                        {
                            Import.Variables.userDatabase.Add(oldDb);
                            File.WriteAllText(dbPath, JsonSerializer.Serialize(Import.Variables.userDatabase, new JsonSerializerOptions { WriteIndented = true }));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading OS files! ({ex.Message}) 🚨");
                return;
            }

            Login_Manager.ShowLoginScreen();
        }
    }
}