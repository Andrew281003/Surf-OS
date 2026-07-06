using System.Runtime.Versioning;
using System.Text;

namespace SurfOS2;

internal class Login_Manager
{
    [SupportedOSPlatform("windows")]
    public static void ShowLoginScreen()
    {
        Screen_Print.ResetToDefaultOSTheme();
        KernelLog.Info("login", "login screen opened");

        while (true)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            RetroConsole.TypeLine("======================================", 2);
            RetroConsole.TypeLine("       SURFOS SECURITY TERMINAL       ", 3);
            RetroConsole.TypeLine("======================================", 2);
            RetroConsole.TypeLine("IDENTIFICATION REQUIRED\n", 3);
            Console.ResetColor();

            Console.Write("Username: ");
            string inputUser = (Console.ReadLine() ?? string.Empty).Trim();

            Import.DatabaseRecord? userRecord = Import.Variables.userDatabase.Find(
                user => user.Username.Equals(inputUser, StringComparison.OrdinalIgnoreCase));

            if (inputUser.Equals("surfos-bypass"))
            {
                KernelLog.Warning("login", "bypass login requested");
                Import.DatabaseRecord? bypassUser =
                    userRecord ?? Import.Variables.userDatabase.FirstOrDefault();

                if (bypassUser is null)
                {
                    KernelLog.Error("login", "bypass failed: no profiles exist");
                    ShowLoginError("No user profiles exist to bypass into.");
                    continue;
                }

                KernelLog.Success("login", $"bypass accepted for {bypassUser.Username}");
                StartSession(bypassUser);
                continue;
            }
            if (inputUser.Equals("surfos-shutdown"))
            {
                Console.Clear();
                Console.WriteLine("Force shutdown engaged...");
                Environment.Exit(0);
            }

            if (Recovery_Manager.IsRecoveryUsername(inputUser))
            {
                KernelLog.Warning("login", "recovery login requested");
                RunPasswordRecovery();
                continue;
            }

            if (userRecord is null)
            {
                KernelLog.Warning("login", $"unknown username attempted: {inputUser}");
                ShowLoginError("User not found in the database.");
                continue;
            }

            Console.Write("Password: ");
            string inputPassword = ReadPassword();

            if (!string.Equals(inputPassword, userRecord.Password, StringComparison.Ordinal))
            {
                KernelLog.Warning("login", $"bad password for {userRecord.Username}");
                ShowLoginError("Incorrect Password. Try again.");
                continue;
            }

            KernelLog.Success("login", $"login accepted for {userRecord.Username}");
            StartSession(userRecord);
        }
    }

    public static void ShowRecoveryScreen()
    {
        Screen_Print.ResetToDefaultOSTheme();
        KernelLog.Warning("recovery", "recovery screen opened from boot manager");
        Console.ForegroundColor = ConsoleColor.Yellow;
        RetroConsole.TypeLine("======================================", 2);
        RetroConsole.TypeLine("        SURFOS RECOVERY SYSTEM        ", 3);
        RetroConsole.TypeLine("======================================", 2);
        Console.ResetColor();

        RunPasswordRecovery();
    }

    [SupportedOSPlatform("windows")]
    private static void StartSession(Import.DatabaseRecord userRecord)
    {
        userRecord.Mailbox ??= new List<Import.MailMessage>();
        userRecord.CurrentRank =
            string.IsNullOrWhiteSpace(userRecord.CurrentRank) ? "User" : userRecord.CurrentRank;

        Import.Variables.userName = userRecord.Username;
        Import.Variables.userPassword = userRecord.Password;
        Import.Variables.uuid = userRecord.ID;
        Import.Variables.sessionStartTime = DateTime.Now;
        KernelLog.Success("session", $"session started for {userRecord.Username}");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine();
        RetroConsole.TypeLine("\nACCESS GRANTED", 7);
        RetroConsole.Spinner("VERIFYING USER PROFILE", 250);
        RetroConsole.Spinner("MOUNTING USER STORAGE", 250);
        RetroConsole.ProgressBar("STARTING COMMAND PROCESSOR", 16, 12);

        if (userRecord.Mailbox.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(
                $"\nNotification: You have ({userRecord.Mailbox.Count}) unread message(s)! Type 'mail' to check.");
        }

        Console.ResetColor();

        if (!BIOS.CurrentOptions.StartupSoundEnabled)
        {
            KernelLog.Info("session", "startup sound skipped by BIOS");
            CLI_Engine.StartTerminal();
            return;
        }

        try
        {
            Console.Beep(440, 100);
            Console.Beep(554, 100);
            Console.Beep(659, 180);
            KernelLog.Success("session", "startup sound played");
        }
        catch (Exception ex)
        {
            KernelLog.Warning("session", $"startup sound unavailable: {ex.Message}");
        }

        CLI_Engine.StartTerminal();
    }

    private static void ShowLoginError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        RetroConsole.TypeLine($"\n{message}", 5);
        Console.ResetColor();
        Thread.Sleep(350);
    }

    private static void RunPasswordRecovery()
    {
        Console.Write("Recovery code: ");
        string recoveryCode = ReadPassword();
        Console.WriteLine();

        if (!Recovery_Manager.VerifyCode(recoveryCode))
        {
            KernelLog.Warning("recovery", "invalid recovery code submitted");
            ShowLoginError("Invalid recovery code.");
            return;
        }

        Import.DatabaseRecord? account = SelectRecoveryAccount();
        if (account is null)
        {
            KernelLog.Error("recovery", "recovery account selection failed");
            ShowLoginError("Account not found.");
            return;
        }

        Console.Write("New password: ");
        string newPassword = ReadPassword();
        Console.Write("\nConfirm password: ");
        string confirmedPassword = ReadPassword();
        Console.WriteLine();

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            KernelLog.Warning("recovery", "empty password rejected");
            ShowLoginError("The new password cannot be empty.");
            return;
        }

        if (!string.Equals(
                newPassword,
                confirmedPassword,
                StringComparison.Ordinal))
        {
            KernelLog.Warning("recovery", "password confirmation mismatch");
            ShowLoginError("The passwords do not match.");
            return;
        }

        account.Password = newPassword;
        JsonStorage.Write(
            Path.Combine(Import.Variables.installPath, "database.json"),
            Import.Variables.userDatabase);

        Console.ForegroundColor = ConsoleColor.Green;
        KernelLog.Success("recovery", $"password reset complete for {account.Username}");
        RetroConsole.TypeLine(
            $"\nPASSWORD RESET COMPLETE FOR '{account.Username}'.", 5);
        Console.ResetColor();
        Thread.Sleep(500);
    }

    private static Import.DatabaseRecord? SelectRecoveryAccount()
    {
        if (Import.Variables.userDatabase.Count == 1)
        {
            return Import.Variables.userDatabase[0];
        }

        Console.Write("Account username: ");
        string username = (Console.ReadLine() ?? string.Empty).Trim();

        return Import.Variables.userDatabase.Find(
            user => user.Username.Equals(
                username,
                StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadPassword()
    {
        StringBuilder password = new();

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                return password.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write('*');
            }
        }
    }
}
