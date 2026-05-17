using System.Text.Json;

namespace SurfOS2
{
    internal class Screen_Print
    {
        public static void Call_Setup()
        {
            Console.WriteLine("Hello, welcome to SurfOS. The place where YOU have 100% access to your computer." +
                "\n" +
                "\n[RIGHT ARROW] To start setup" +
                "\n[Q] to quit");
        }

        public static void Confirm_Setup()
        {
            Console.Clear();
            Console.WriteLine("Please press any button to confirm the setup! 🚀");
        }

        // 🌟 NEW: Completely strip the active theme and revert to standard OS colors
        public static void ResetToDefaultOSTheme()
        {
            Import.Variables.activeForegroundColor = ConsoleColor.Gray;
            Import.Variables.activeBackgroundColor = ConsoleColor.Black;
            Import.Variables.activePromptStyle = "Standard";
            Console.Title = "SurfOS";
            ResetColors();
            Console.Clear();
            Core_Engine.SetConsoleFont("Consolas");
        }

        // 🌟 NEW: Custom reset method that respects the OS Theme!
        public static void ResetColors()
        {
            Console.ForegroundColor = Import.Variables.activeForegroundColor;
            Console.BackgroundColor = Import.Variables.activeBackgroundColor;
        }

        public static void LoadAndApplyTheme(string themeName, bool sudoOverride = false)
        {
            string packagePath = Path.Combine(Import.Variables.installPath, "Packages", $"{themeName}.json");

            if (!File.Exists(packagePath))
            {
                Console.Title = "SurfOS Default";
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.BackgroundColor = ConsoleColor.Black;
                Console.WriteLine($"[SurfOS] Welcome! (Theme pack '{themeName}.json' was not found).");
                return;
            }

            try
            {
                string jsonString = File.ReadAllText(packagePath);
                Import.SurfTheme? theme = JsonSerializer.Deserialize<Import.SurfTheme>(jsonString);

                if (theme != null)
                {
                    // 1. SECURITY CHECK
                    if (!Core_Engine.IsThemeLegit(theme))
                    {
                        if (sudoOverride)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkYellow;
                            Console.WriteLine("⚠️ SUDO OVERRIDE: Loading unverified third-party theme...");
                            ResetColors();
                            System.Threading.Thread.Sleep(1200);
                        }
                        else
                        {
                            Console.Clear();
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("⚠️ WARNING: UNAUTHORIZED THEME DETECTED! ⚠️");
                            Console.WriteLine("This theme file is either corrupted or not from the official SurfOS website.");
                            Console.WriteLine("Loading aborted to protect system integrity.");
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine("\nTIP: Use 'sudo theme load <name>' to force load at your own risk.");
                            ResetColors();
                            return;
                        }
                    }

                    // 2. Apply Custom Font
                    Core_Engine.SetConsoleFont(theme.FontName);

                    // 3. Save & Apply Colors
                    Console.Title = theme.WindowTitle;

                    if (Enum.TryParse(theme.TargetColor, true, out ConsoleColor customForeground))
                        Import.Variables.activeForegroundColor = customForeground;

                    if (Enum.TryParse(theme.BackgroundColor, true, out ConsoleColor customBackground))
                        Import.Variables.activeBackgroundColor = customBackground;

                    // 🌟 Apply the saved theme colors!
                    ResetColors();

                    // 4. Draw Logo/ASCII
                    Console.Clear();
                    Console.WriteLine(theme.AsciiArt);

                    // 5. Apply custom UI Layout logic
                    if (theme.UILayout == "Centered")
                    {
                        Console.WriteLine($"\n\t\t--- {theme.WelcomeMessage} ---");
                    }
                    else
                    {
                        Console.WriteLine($"\n> {theme.WelcomeMessage}");
                    }

                    Import.Variables.activePromptStyle = theme.PromptStyle;
                }
            }
            catch (Exception ex)
            {
                ResetColors();
                Console.WriteLine($"🚨 Failed to render pack graphics: {ex.Message}");
            }
        }

        public static void Print_Selected_Package()
        {
            LoadAndApplyTheme(Import.Variables.defaultTheme, true);
        }
    }
}