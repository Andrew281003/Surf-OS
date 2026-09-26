using System;

namespace SurfOS.Users
{
    public sealed class AuthenticationConsole
    {
        public static int WelcomeDelayMilliseconds = 350;
        private readonly SessionService _sessions;
        private readonly UserService _users;

        public AuthenticationConsole(SessionService sessions, UserService users)
        {
            _sessions = sessions;
            _users = users;
        }

        public void LoginUntilSuccessful()
        {
            while (!_sessions.IsAuthenticated)
            {
                int selected = SelectAccount();
                UserAccount account = _users.Accounts[selected];
                Console.Clear();
                AvatarRenderer.Draw(account.AvatarId, AvatarSize.Medium, account.ProfileColor, 0, false);
                Console.WriteLine(account.Username);
                Console.WriteLine("Esc: choose another account");
                Console.Write("Password: ");
                string password = ReadSecret(true);
                if (password == null) continue;
                if (!_sessions.Login(account.Username, password))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Login failed.");
                    Console.ResetColor();
                    continue;
                }
                Console.Clear();
                AvatarRenderer.Draw(account.AvatarId, AvatarSize.Medium, account.ProfileColor, 0, true);
                Console.WriteLine("Welcome, " + account.Username + ".");
                if (WelcomeDelayMilliseconds > 0) System.Threading.Thread.Sleep(WelcomeDelayMilliseconds);
            }
        }

        private int SelectAccount()
        {
            int selected = 0, first = 0;
            while (true)
            {
                Console.Clear();
                Console.WriteLine("SurfOS accounts  (Up/Down: choose, Enter: continue)");
                int rows = Math.Max(1, (Console.WindowHeight - 4) / 4);
                first = MenuNavigation.FirstVisible(selected, first, rows);
                for (int i = first; i < Math.Min(_users.Accounts.Count, first + rows); i++)
                {
                    UserAccount account = _users.Accounts[i];
                    ConsoleColor previous = Console.ForegroundColor;
                    Console.ForegroundColor = ProfileColors.Display(account.ProfileColor);
                    string[] icon = AvatarRenderer.Layout(account.AvatarId, AvatarSize.Small, Math.Max(1, Console.WindowWidth - 2), 0, false);
                    for (int line = 0; line < icon.Length; line++)
                    {
                        string display = (i == selected ? "> " : "  ") + icon[line] + (line == 1 ? "  " + account.Username : string.Empty);
                        Console.WriteLine(display.Substring(0, Math.Min(display.Length, Math.Max(1, Console.WindowWidth - 1))));
                    }
                    Console.ForegroundColor = previous;
                }
                Console.WriteLine((first + 1) + "-" + Math.Min(_users.Accounts.Count, first + rows) + " of " + _users.Accounts.Count);
                ConsoleKey key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.Enter) return selected;
                selected = MenuNavigation.Move(selected, _users.Accounts.Count, key);
            }
        }

        public bool ReauthenticateCurrentUser()
        {
            if (!_sessions.IsAuthenticated) { return false; }
            Console.Write("Authenticate " + _sessions.CurrentUser.Username + ": ");
            string password = ReadSecret();
            return _sessions.Login(_sessions.CurrentUser.Username, password);
        }

        public static string ReadSecret(bool allowEscape = false)
        {
            string value = string.Empty;
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (allowEscape && key.Key == ConsoleKey.Escape) { Console.WriteLine(); return null; }
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
