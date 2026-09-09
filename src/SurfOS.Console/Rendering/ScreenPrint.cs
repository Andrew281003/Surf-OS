using System.Security.Authentication.ExtendedProtection;
using System.Text.Json;

namespace SurfOS2
{
    internal class Screen_Print
    {
        public static void Call_Setup()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            RetroConsole.TypeLine("SURFOS INSTALLATION PROGRAM", 5);
            RetroConsole.TypeLine("---------------------------", 2);
            RetroConsole.TypeLine("Welcome, operator. Choose Guided Setup for recommended defaults", 3);
            RetroConsole.TypeLine("or Advanced Setup for complete control over the installation.", 3);
            RetroConsole.TypeLine("The default SurfOS drive is a safe 15 GB fixed VHDX and can be customized.", 3);
            RetroConsole.TypeLine("\n[RIGHT ARROW / ENTER] Begin installation", 2);
            RetroConsole.TypeLine("[Q] Abort", 2);
            Console.ResetColor();
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
            Core_Engine.SetConsoleFont("Consolas");
            if (!Console.IsOutputRedirected) Console.Clear();
        }

        // 🌟 NEW: Custom reset method that respects the OS Theme!
        public static void ResetColors()
        {
            Console.ForegroundColor = Import.Variables.activeForegroundColor;
            Console.BackgroundColor = Import.Variables.activeBackgroundColor;
        }

        public static void LoadAndApplyTheme(string themeName, bool sudoOverride = false)
        {
            if (!PathSafety.IsSafeFileName(themeName))
            {
                Console.WriteLine("[SurfOS] Invalid theme name.");
                return;
            }

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
                            // Nothing needed, at least for now
                        }
                        else
                        {
                            Console.Clear();
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("⚠️ WARNING: UNAUTHORIZED THEME DETECTED! ⚠️");
                            Console.WriteLine("This theme file is either corrupted or not from the official SurfOS website.");
                            Console.WriteLine("Loading aborted to protect system integrity.");
                            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\nTIP: Use 'theme load <name> --force' to force load at your own risk.");
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
                    RetroConsole.RevealLines(theme.AsciiArt);

                    // 5. Apply custom UI Layout logic
                    if (theme.UILayout == "Centered")
                    {
                        RetroConsole.TypeLine($"\n\t\t--- {theme.WelcomeMessage} ---", 3);
                    }
                    else
                    {
                        RetroConsole.TypeLine($"\n> {theme.WelcomeMessage}", 3);
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
            if (string.IsNullOrEmpty(Import.Variables.defaultTheme))
            {
                ResetToDefaultOSTheme();
                return;
            }
            LoadAndApplyTheme(Import.Variables.defaultTheme, true);
            return;
        }
    }
}
