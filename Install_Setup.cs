using System.Runtime.Versioning;

namespace SurfOS2;

internal static class Install_Setup
{
    [SupportedOSPlatform("windows")]
    public static void Install_WizardP1()
    {
        Screen_Print.Call_Setup();
        ConsoleKey key = Console.ReadKey(intercept: true).Key;

        if (key is not (ConsoleKey.RightArrow or ConsoleKey.F2))
        {
            return;
        }

        Console.Clear();
        RetroConsole.TypeLine("USER ACCOUNT CONFIGURATION", 5);
        RetroConsole.TypeLine("--------------------------", 2);
        RetroConsole.Type("Enter your desired SurfOS Username: ", 2);
        string name = Console.ReadLine() ?? string.Empty;
        Import.Variables.userName = string.IsNullOrWhiteSpace(name) ? "Guest" : name.Trim();

        RetroConsole.Type("Create a SurfOS Password: ", 2);
        string password = Console.ReadLine() ?? string.Empty;
        Import.Variables.userPassword = string.IsNullOrWhiteSpace(password) ? "1234" : password;

        if (!SelectInstallDirectory())
        {
            return;
        }
        SelectTheme();
        SelectTimeZone();

        File_Setup_Manager.Main_Directory();
        Login_Manager.ShowLoginScreen();
    }

    private static bool SelectInstallDirectory()
    {
        while (true)
        {
            Console.Clear();
            RetroConsole.TypeBlock("""
                Please select the directory where you want to install SurfOS

                [F1] - Desktop
                [F2] - Documents
                [F3] - Root (C partition)
                [F4] - Cancel
                """, 2);
            Console.Write("\nOption: ");

            switch (Console.ReadKey(intercept: true).Key)
            {
                case ConsoleKey.F1:
                    Import.Variables.installPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        "SurfOS");
                    return true;
                case ConsoleKey.F2:
                    Import.Variables.installPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "SurfOS");
                    return true;
                case ConsoleKey.F3:
                    Import.Variables.installPath = @"C:\SurfOS";
                    return true;
                case ConsoleKey.F4:
                    return false;
            }
        }
    }

    private static void SelectTheme()
    {
        while (true)
        {
            Console.Clear();
            RetroConsole.TypeBlock("""
                Pick the theme you would like to use:

                [F1] - HolySurf
                [F2] - UnHolySurf
                """, 2);
            Console.Write("\nOption: ");

            switch (Console.ReadKey(intercept: true).Key)
            {
                case ConsoleKey.F1:
                    Import.Variables.packageOption = 1;
                    Import.Variables.defaultTheme = "HolySurf";
                    return;
                case ConsoleKey.F2:
                    Import.Variables.packageOption = 2;
                    Import.Variables.defaultTheme = "UnHolySurf";
                    return;
            }
        }
    }

    private static void SelectTimeZone()
    {
        while (true)
        {
            Console.Clear();
            RetroConsole.TypeBlock("""
                Select your OS Timezone:

                [F1] - Local PC Time (Default)
                [F2] - UTC
                [F3] - Central European Time (CET)
                [F4] - Eastern Standard Time (EST)
                """, 2);
            Console.Write("\nOption: ");

            Import.Variables.timeZone = Console.ReadKey(intercept: true).Key switch
            {
                ConsoleKey.F1 => "Local",
                ConsoleKey.F2 => "UTC",
                ConsoleKey.F3 => "CET",
                ConsoleKey.F4 => "EST",
                _ => string.Empty
            };

            if (Import.Variables.timeZone.Length > 0)
            {
                return;
            }
        }
    }
}
