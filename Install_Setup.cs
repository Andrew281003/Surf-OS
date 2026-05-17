using System;

namespace SurfOS2
{
    internal class Install_Setup
    {
        public static void Install_WizardP1()
        {
            Screen_Print.Call_Setup();

            ConsoleKeyInfo input = Console.ReadKey(true);

            if (input.Key == ConsoleKey.RightArrow || input.Key == ConsoleKey.F2)
            {
                Console.Clear();
                Console.Write("Enter your desired SurfOS Username: ");
                string name = Console.ReadLine() ?? string.Empty;
                Import.Variables.userName = string.IsNullOrWhiteSpace(name) ? "Guest" : name;

                // 🔐 NEW: Ask for a password
                Console.Write("Create a SurfOS Password: ");
                string pass = Console.ReadLine() ?? string.Empty;
                Import.Variables.userPassword = string.IsNullOrWhiteSpace(pass) ? "1234" : pass; // Default to 1234 if empty

                Install_WizardP2();
            }
        }

        public static void Install_WizardP2()
        {
            Console.Clear();
            Console.WriteLine("Please select the directory where you want to install SurfOS" +
                "\n" +
                "\n[F1] - Desktop" +
                "\n[F2] - Documents" +
                "\n[F3] - Root (C partition)" +
                "\n[F4] - Cancel");

            Console.Write("\nOption: ");
            ConsoleKeyInfo input = Console.ReadKey(true);

            if (input.Key == ConsoleKey.F1) { Import.Variables.installPath = $"C:\\Users\\{Import.Variables.machineName}\\Desktop\\SurfOS"; }
            else if (input.Key == ConsoleKey.F2) { Import.Variables.installPath = $"C:\\Users\\{Import.Variables.machineName}\\Documents\\SurfOS"; }
            else if (input.Key == ConsoleKey.F3) { Import.Variables.installPath = "C:\\SurfOS"; } 
            else if (input.Key == ConsoleKey.F4) { Install_WizardP1(); return; }
            else { Install_WizardP2(); return; }
            
            Install_WizardP3();
        }

        public static void Install_WizardP3()
                {
                    Console.Clear();
                    Console.WriteLine("Pick the theme you would like to use:" +
                        "\n" +
                        "\n[F1] - HolySurf" +
                        "\n[F2] - UnHolySurf");

                    Console.Write("\nOption: ");
                    ConsoleKeyInfo input = Console.ReadKey(true);

                    if (input.Key == ConsoleKey.F1) { Import.Variables.packageOption = 1; }
                    else if (input.Key == ConsoleKey.F2) { Import.Variables.packageOption = 2; }
                    else { Install_WizardP3(); return; }

                    // 🌟 FIX: Go to Phase 4 instead of finishing!
                    Install_WizardP4();
                }

                // 🌟 NEW: The Timezone Setup Screen
                public static void Install_WizardP4()
                {
                    Console.Clear();
                    Console.WriteLine("Select your OS Timezone:" +
                        "\n" +
                        "\n[F1] - Local PC Time (Default)" +
                        "\n[F2] - UTC" +
                        "\n[F3] - Central European Time (CET)" +
                        "\n[F4] - Eastern Standard Time (EST)");

                    Console.Write("\nOption: ");
                    ConsoleKeyInfo input = Console.ReadKey(true);

                    if (input.Key == ConsoleKey.F1) { Import.Variables.timeZone = "Local"; }
                    else if (input.Key == ConsoleKey.F2) { Import.Variables.timeZone = "UTC"; }
                    else if (input.Key == ConsoleKey.F3) { Import.Variables.timeZone = "CET"; }
                    else if (input.Key == ConsoleKey.F4) { Import.Variables.timeZone = "EST"; }
                    else { Install_WizardP4(); return; }

                    File_Setup_Manager.Main_Directory();
                }
    }
}