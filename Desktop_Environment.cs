using System;
using System.Threading;
using SurfOS2.os_Apps;

namespace SurfOS2
{
    internal sealed class DesktopMenuItem
    {
        public string Label { get; init; } = string.Empty;
        public Action Launch { get; init; } = () => { };
    }

    internal class Desktop_Environment
    {
        public static void StartGUI()
        {
            try
            {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            RetroConsole.Spinner("LOADING VIDEO DRIVER", 300);
            RetroConsole.Spinner("INITIALIZING WINDOW MANAGER", 300);
            RetroConsole.ProgressBar("DRAWING DESKTOP", 18, 16);
            Console.ResetColor();

            int selectedIndex = 0;
            bool inGUI = true;
            List<DesktopMenuItem> apps = BuildDesktopMenu(() => inGUI = false);

            Console.CursorVisible = false; // Hide the blinking typing cursor for a real GUI feel

            while (inGUI)
            {
                Console.Clear();
                Screen_Print.ResetColors(); 

                // 1. Draw the Desktop Taskbar
                Console.BackgroundColor = ConsoleColor.White;
                Console.ForegroundColor = ConsoleColor.Black;
                string time = Time_Manager.GetCurrentTime().ToString("HH:mm");
                string topBar = $"  🌊 SurfOS Desktop Environment | Active User: {Import.Variables.userName} | {time} ";
                
                // Pad right ensures the white bar stretches across the entire monitor
                Console.WriteLine(topBar.PadRight(Console.WindowWidth)); 
                Screen_Print.ResetColors();

                Console.WriteLine("\n\n");
                
                // 2. Draw a visual Desktop Logo
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(@"      ____             __  ___  _____      ");
                Console.WriteLine(@"     / __/_ ________  /  |/  / / ___/      ");
                Console.WriteLine(@"    _\ \/ // / __/ _ \/ /|_/ / / /__       ");
                Console.WriteLine(@"   /___/\_,_/_/  \_  /_/  /_/  \___/       ");
                Console.WriteLine(@"                /_/                        ");
                Console.WriteLine("\n");
                Screen_Print.ResetColors();

                // 3. Render the App "Icons"
                apps = BuildDesktopMenu(() => inGUI = false);
                if (selectedIndex >= apps.Count)
                {
                    selectedIndex = apps.Count - 1;
                }

                for (int i = 0; i < apps.Count; i++)
                {
                    if (i == selectedIndex)
                    {
                        // Highlight the selected app
                        Console.BackgroundColor = ConsoleColor.DarkGray;
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine($"   > [ {apps[i].Label} ] <   ");
                        Screen_Print.ResetColors();
                    }
                    else
                    {
                        Console.WriteLine($"     [ {apps[i].Label} ]     ");
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("\n\n  (Use UP/DOWN Arrow Keys to navigate, ENTER to launch app)");
                Screen_Print.ResetColors();

                // 4. Listen for Keyboard Navigation
                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow) { selectedIndex--; }
                if (key == ConsoleKey.DownArrow) { selectedIndex++; }

                // Wrap around the menu if they go too far up or down
                if (selectedIndex < 0) selectedIndex = apps.Count - 1;
                if (selectedIndex >= apps.Count) selectedIndex = 0;

                // 5. Execute Apps when clicked!
                if (key == ConsoleKey.Enter)
                {
                    Console.Clear();
                    RetroConsole.Spinner($"OPENING {apps[selectedIndex].Label}", 220, ConsoleColor.Cyan);
                    apps[selectedIndex].Launch();
                }
            }

            Console.CursorVisible = true; // Turn the typing cursor back on for the terminal!
            Console.Clear();
            Screen_Print.Print_Selected_Package(); // Redraw the CLI logo
            }
            catch (Exception ex)
            {
                Console.CursorVisible = true;
                KernelPanic.ShowAndHandle(
                    ex,
                    "Desktop_Environment.cs",
                    "Reboot SurfOS. If the desktop keeps crashing, boot to the terminal and avoid startx until logs are reviewed.");
            }
        }

        private static void PauseForGUI()
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\n[Press ENTER to close window and return to Desktop...]");
            Screen_Print.ResetColors();
            Console.ReadLine();
        }

        private static List<DesktopMenuItem> BuildDesktopMenu(Action exitGui)
        {
            List<DesktopMenuItem> menu =
            [
                new()
                {
                    Label = "Terminal (Exit GUI)",
                    Launch = exitGui
                },
                new()
                {
                    Label = "SurfCode IDE",
                    Launch = () =>
                    {
                        Console.CursorVisible = true;
                        using (ProcessManager.StartProcess(
                                   "SurfCode IDE",
                                   96,
                                   supportsKill: true))
                        {
                            CodeEditor.Launch();
                        }
                        Console.CursorVisible = false;
                    }
                },
                new()
                {
                    Label = "System Monitor",
                    Launch = () => RunDesktopCommand("sys")
                },
                new()
                {
                    Label = "Mailbox",
                    Launch = () => RunDesktopCommand("mail")
                },
                new()
                {
                    Label = "Music Player",
                    Launch = () => RunDesktopCommand("music")
                },
                new()
                {
                    Label = "Surf Store",
                    Launch = () => SurfStore.Open()
                },
                new()
                {
                    Label = "Crypto Miner",
                    Launch = () => RunDesktopCommand("mine")
                },
                new()
                {
                    Label = "Calendar & Clock",
                    Launch = () =>
                    {
                        RunDesktopCommand("clock", pauseAfter: false);
                        RunDesktopCommand("calendar");
                    }
                }
            ];

            foreach (DesktopPackageEntry package in Package_Manager.GetDesktopPackages())
            {
                string label = menu.Any(item => item.Label.Equals(
                    package.DisplayName,
                    StringComparison.OrdinalIgnoreCase))
                        ? $"Package: {package.DisplayName}"
                        : package.DisplayName;

                menu.Add(new DesktopMenuItem
                {
                    Label = label,
                    Launch = () => RunDesktopCommand(package.Command)
                });
            }

            return menu;
        }

        private static void RunDesktopCommand(string command, bool pauseAfter = true)
        {
            bool dummyRun = true;
            CLI_Engine.ExecuteCommand(command, ref dummyRun, true);
            if (pauseAfter)
            {
                PauseForGUI();
            }
        }
    }
}
