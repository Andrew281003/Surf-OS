using System;

namespace SurfOS.Users
{
    public sealed class AuthenticationConsole
    {
        private readonly SessionService _sessions;

        public AuthenticationConsole(SessionService sessions)
        {
            _sessions = sessions;
        }

        public void LoginUntilSuccessful()
        {
            while (!_sessions.IsAuthenticated)
            {
                Console.Write("Login: ");
                string username = Console.ReadLine();
                Console.Write("Password: ");
                string password = ReadSecret();
                if (!_sessions.Login(username, password))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Login failed.");
                    Console.ResetColor();
                }
            }
        }

        public bool ReauthenticateCurrentUser()
        {
            if (!_sessions.IsAuthenticated) { return false; }
            Console.Write("Authenticate " + _sessions.CurrentUser.Username + ": ");
            string password = ReadSecret();
            return _sessions.Login(_sessions.CurrentUser.Username, password);
        }

        public static string ReadSecret()
        {
            string value = string.Empty;
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return value;
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (value.Length > 0)
                    {
                        value = value.Substring(0, value.Length - 1);
                        ErasePreviousMaskCharacter();
                    }
                    continue;
                }
                if (!char.IsControl(key.KeyChar))
                {
                    value += key.KeyChar;
                    Console.Write('*');
                }
            }
        }

        private static void ErasePreviousMaskCharacter()
        {
            int left = Console.CursorLeft;
            int top = Console.CursorTop;
            if (left > 0)
            {
                left--;
            }
            else
            {
                if (top == 0) { return; }
                top--;
                left = Console.WindowWidth - 1;
            }

            // Cosmos prints the backspace control character as a visible glyph.
            // Move the cursor explicitly, erase the mask, and leave the cursor there.
            Console.SetCursorPosition(left, top);
            Console.Write(' ');
            Console.SetCursorPosition(left, top);
        }
    }
}
