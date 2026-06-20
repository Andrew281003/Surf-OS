using System;
using System.Threading;

namespace SurfOS2
{
    internal class Desktop_Environment
    {
        public static void StartGUI()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            RetroConsole.Spinner("LOADING VIDEO DRIVER", 300);
            RetroConsole.Spinner("INITIALIZING WINDOW MANAGER", 300);
            RetroConsole.ProgressBar("DRAWING DESKTOP", 18, 16);
            Console.ResetColor();

            int selectedIndex = 0;
            string[] apps = { "💻 Terminal (Exit GUI)", "🖥️ System Monitor", "📬 Mailbox", "🛒 Surf Shop", "⛏️ Crypto Miner", "📅 Calendar & Clock" };
            bool inGUI = true;

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
                for (int i = 0; i < apps.Length; i++)
                {
                    if (i == selectedIndex)
                    {
                        // Highlight the selected app
                        Console.BackgroundColor = ConsoleColor.DarkGray;
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine($"   > [ {apps[i]} ] <   ");
                        Screen_Print.ResetColors();
                    }
                    else
                    {
                        Console.WriteLine($"     [ {apps[i]} ]     ");
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
                if (selectedIndex < 0) selectedIndex = apps.Length - 1;
                if (selectedIndex >= apps.Length) selectedIndex = 0;

                // 5. Execute Apps when clicked!
                if (key == ConsoleKey.Enter)
                {
                    Console.Clear();
                    RetroConsole.Spinner($"OPENING {apps[selectedIndex]}", 220, ConsoleColor.Cyan);
                    bool dummyRun = true;

                    if (selectedIndex == 0) // Terminal
                    {
                        inGUI = false; 
                    }
                    else if (selectedIndex == 1) // Sys Monitor
                    {
                        CLI_Engine.ExecuteCommand("sys", ref dummyRun, true);
                        PauseForGUI();
                    }
                    else if (selectedIndex == 2) // Mail
                    {
                        CLI_Engine.ExecuteCommand("mail", ref dummyRun, true);
                        PauseForGUI();
                    }
                    else if (selectedIndex == 3) // Shop
                    {
                        CLI_Engine.ExecuteCommand("shop", ref dummyRun, true);
                        PauseForGUI();
                    }
                    else if (selectedIndex == 4) // Mine
                    {
                        CLI_Engine.ExecuteCommand("mine", ref dummyRun, true);
                        PauseForGUI();
                    }
                    else if (selectedIndex == 5) // Calendar
                    {
                        CLI_Engine.ExecuteCommand("clock", ref dummyRun, true);
                        CLI_Engine.ExecuteCommand("calendar", ref dummyRun, true);
                        PauseForGUI();
                    }
                }
            }

            Console.CursorVisible = true; // Turn the typing cursor back on for the terminal!
            Console.Clear();
            Screen_Print.Print_Selected_Package(); // Redraw the CLI logo
        }

        private static void PauseForGUI()
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\n[Press ENTER to close window and return to Desktop...]");
            Screen_Print.ResetColors();
            Console.ReadLine();
        }
    }
}
