using System;

namespace SurfOS2
{
    internal class Login_Manager
    {
        // 🌟 FIX: Guard this entire method so Windows-only beeps don't throw warnings!
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public static void ShowLoginScreen()
        {
            Screen_Print.ResetToDefaultOSTheme();

            bool isAuthenticated = false;

            while (!isAuthenticated)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("======================================");
                Console.WriteLine($"            SurfOS Login              ");
                Console.WriteLine("======================================\n");
                Console.ResetColor();

                Console.Write("Username: ");
                string inputUser = Console.ReadLine() ?? string.Empty;

                var userRecord = Import.Variables.userDatabase.Find(u => u.Username.Equals(inputUser, StringComparison.OrdinalIgnoreCase));

                if (userRecord == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n🚨 User not found in the database.");
                    Console.ResetColor();
                    System.Threading.Thread.Sleep(1500);
                    continue;
                }

                Console.Write("Password: ");
                string inputPass = ReadPassword();

                if (inputPass == userRecord.Password)
                {
                    isAuthenticated = true;
                    
                    Import.Variables.userName = userRecord.Username;
                    Import.Variables.userPassword = userRecord.Password;
                    Import.Variables.uuid = userRecord.ID;

                    // 🌟 Start counting session uptime right now!
                    Import.Variables.sessionStartTime = DateTime.Now;

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\n\n");
                    
                    string bootMsg = "Access Granted! Booting system...";
                    foreach (char c in bootMsg)
                    {
                        Console.Write(c);
                        System.Threading.Thread.Sleep(30); 
                    }
                    Console.WriteLine();

                    // 🌟 MAIL CHECK: Flash unread mail notifications before terminal opens!
                    if (userRecord.Mailbox != null && userRecord.Mailbox.Count > 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"\n📩 Notification: You have ({userRecord.Mailbox.Count}) unread message(s)! Type 'mail' to check.");
                        Console.ResetColor();
                        System.Threading.Thread.Sleep(1500);
                    }

                    try 
                    {
                        Console.Beep(440, 150); 
                        Console.Beep(554, 150); 
                        Console.Beep(659, 300); 
                    } 
                    catch { }

                    System.Threading.Thread.Sleep(800); 
                    CLI_Engine.StartTerminal(); 
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n🚨 Incorrect Password. Try again.");
                    Console.ResetColor();
                    System.Threading.Thread.Sleep(1500);
                }
            }
        }

        private static string ReadPassword()
        {
            string password = "";
            ConsoleKeyInfo info = Console.ReadKey(true);
            while (info.Key != ConsoleKey.Enter)
            {
                if (info.Key != ConsoleKey.Backspace)
                {
                    Console.Write("*");
                    password += info.KeyChar;
                }
                else if (info.Key == ConsoleKey.Backspace)
                {
                    if (!string.IsNullOrEmpty(password))
                    {
                        password = password.Substring(0, password.Length - 1);
                        int pos = Console.CursorLeft;
                        Console.SetCursorPosition(pos - 1, Console.CursorTop);
                        Console.Write(" ");
                        Console.SetCursorPosition(pos - 1, Console.CursorTop);
                    }
                }
                info = Console.ReadKey(true);
            }
            return password;
        }
    }
}