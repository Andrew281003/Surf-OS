using System.Security.Cryptography;

namespace SurfOS2;

internal static class Recovery_Manager
{
    public const string RecoveryUsername = "surfos-recovery";

    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public static string? EnsureConfigured(
        Import.SystemOptions options,
        string optionsPath)
    {
        if (!string.IsNullOrWhiteSpace(options.RecoveryCodeHash) &&
            !string.IsNullOrWhiteSpace(options.RecoveryCodeSalt))
        {
            return null;
        }

        string recoveryCode = GenerateRecoveryCode();
        SetRecoveryCode(options, recoveryCode);
        JsonStorage.Write(optionsPath, options);
        return recoveryCode;
    }

    public static string ConfigureNewInstallation(Import.SystemOptions options)
    {
        string recoveryCode = GenerateRecoveryCode();
        SetRecoveryCode(options, recoveryCode);
        return recoveryCode;
    }

    public static bool IsRecoveryUsername(string username)
    {
        return username.Equals(RecoveryUsername, StringComparison.OrdinalIgnoreCase);
    }

    public static bool VerifyCode(string recoveryCode)
    {
        string optionsPath = Path.Combine(Import.Variables.installPath, "options.json");
        if (!File.Exists(optionsPath))
        {
            return false;
        }

        Import.SystemOptions? options =
            JsonStorage.Read<Import.SystemOptions>(optionsPath);

        if (options is null ||
            string.IsNullOrWhiteSpace(options.RecoveryCodeHash) ||
            string.IsNullOrWhiteSpace(options.RecoveryCodeSalt))
        {
            return false;
        }

        try
        {
            byte[] salt = Convert.FromBase64String(options.RecoveryCodeSalt);
            byte[] expectedHash = Convert.FromBase64String(options.RecoveryCodeHash);
            byte[] actualHash = HashCode(NormalizeCode(recoveryCode), salt);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static void DisplayRecoveryCode(string recoveryCode)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        RetroConsole.TypeLine("\n==============================================", 2);
        RetroConsole.TypeLine("          SAVE YOUR RECOVERY CODE", 4);
        RetroConsole.TypeLine("==============================================", 2);
        RetroConsole.TypeLine($"Recovery username: {RecoveryUsername}", 3);
        RetroConsole.TypeLine($"Recovery code    : {recoveryCode}", 6);
        RetroConsole.TypeLine("\nThis code is shown only once. Store it somewhere safe.", 3);
        Console.ResetColor();
        Console.WriteLine("\nPress any key to continue...");
        Console.ReadKey(intercept: true);
    }

    private static string GenerateRecoveryCode()
    {
        string rawCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
        return $"{rawCode[..4]}-{rawCode[4..8]}-{rawCode[8..]}";
    }

    private static void SetRecoveryCode(
        Import.SystemOptions options,
        string recoveryCode)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = HashCode(NormalizeCode(recoveryCode), salt);

        options.RecoveryCodeSalt = Convert.ToBase64String(salt);
        options.RecoveryCodeHash = Convert.ToBase64String(hash);
    }

    private static byte[] HashCode(string recoveryCode, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            recoveryCode,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);
    }

    private static string NormalizeCode(string recoveryCode)
    {
        return recoveryCode
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
    }
}
