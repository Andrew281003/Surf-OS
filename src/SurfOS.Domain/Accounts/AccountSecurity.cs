using System.Security.Cryptography;

namespace SurfOS2;

internal static class AccountSecurity
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 210_000;
    private const int MinimumAcceptedIterations = 10_000;
    private const int MaximumAcceptedIterations = 1_000_000;

    public static void SetPassword(Import.DatabaseRecord account, string password)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Hash(password, salt, Iterations);
        account.Password = string.Empty;
        account.PasswordSalt = Convert.ToBase64String(salt);
        account.PasswordHash = Convert.ToBase64String(hash);
        account.PasswordIterations = Iterations;
    }

    public static bool VerifyPassword(
        Import.DatabaseRecord account,
        string password,
        out bool usedLegacyPassword)
    {
        ArgumentNullException.ThrowIfNull(account);
        usedLegacyPassword = false;

        if (!string.IsNullOrWhiteSpace(account.PasswordHash) &&
            !string.IsNullOrWhiteSpace(account.PasswordSalt))
        {
            try
            {
                byte[] expected = Convert.FromBase64String(account.PasswordHash);
                byte[] salt = Convert.FromBase64String(account.PasswordSalt);
                int iterations = account.PasswordIterations > 0
                    ? account.PasswordIterations
                    : Iterations;
                if (iterations is < MinimumAcceptedIterations or > MaximumAcceptedIterations)
                {
                    return false;
                }
                byte[] actual = Hash(password, salt, iterations);
                return expected.Length == actual.Length &&
                       CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        if (string.IsNullOrEmpty(account.Password))
        {
            return false;
        }

        usedLegacyPassword = string.Equals(
            password,
            account.Password,
            StringComparison.Ordinal);
        return usedLegacyPassword;
    }

    public static bool IsAdministrator(Import.DatabaseRecord? account) =>
        account is not null &&
        !string.IsNullOrWhiteSpace(account.Admin) &&
        !account.Admin.Equals("User", StringComparison.OrdinalIgnoreCase);

    private static byte[] Hash(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashSize);
}
