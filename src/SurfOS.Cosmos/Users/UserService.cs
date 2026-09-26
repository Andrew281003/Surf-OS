using System;
using System.Collections.Generic;
using SurfOS.Logging;
using SurfOS.Storage;

namespace SurfOS.Users
{
    public sealed class UserService
    {
        private const string DatabasePath = "/Users/users.db";
        private readonly IFileSystemService _fileSystem;
        private readonly IPasswordHasher _passwords;
        private readonly IKernelLogger _logger;
        private readonly List<UserAccount> _accounts = new List<UserAccount>();

        public UserService(IFileSystemService fileSystem, IPasswordHasher passwords, IKernelLogger logger)
        {
            _fileSystem = fileSystem;
            _passwords = passwords;
            _logger = logger;
        }

        public void Load()
        {
            _accounts.Clear();
            string[] lines = _fileSystem.ReadAllText(DatabasePath).Replace("\r", string.Empty).Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index].Length == 0 || lines[index][0] == '#') { continue; }
                string[] fields = lines[index].Split('|');
                if (fields.Length < 6) { continue; }
                int id;
                if (!int.TryParse(fields[0], out id)) { continue; }
                UserAccount account = new UserAccount();
                account.Id = id;
                account.Username = fields[1];
                account.IsAdministrator = fields[2] == "admin";
                account.HomeDirectory = fields[3];
                account.PasswordSalt = fields[4];
                account.PasswordVerifier = fields[5];
                account.AvatarId = fields.Length > 6 && AvatarLibrary.IsValid(fields[6]) ? fields[6] : "pilot";
                account.ProfileColor = fields.Length > 7 ? ProfileColors.Normalize(fields[7]) : ProfileColors.Default;
                _accounts.Add(account);
            }
            if (_accounts.Count == 0)
            {
                throw new InvalidOperationException("The user database contains no valid accounts.");
            }
        }

        public UserAccount CreateAdministrator(string username, string password)
        {
            ValidateUsername(username);
            if (password == null || password.Length < 8)
            {
                throw new InvalidOperationException("Password must contain at least 8 characters.");
            }
            string salt = _passwords.CreateSalt();
            UserAccount account = new UserAccount();
            account.Id = 1000;
            account.Username = username;
            account.HomeDirectory = "/Users/" + username;
            account.IsAdministrator = true;
            account.PasswordSalt = salt;
            account.PasswordVerifier = _passwords.Hash(username, password, salt);
            _accounts.Clear();
            _accounts.Add(account);
            try
            {
                if (!_fileSystem.DirectoryExists(account.HomeDirectory))
                {
                    _fileSystem.CreateDirectory(account.HomeDirectory);
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Could not create the administrator home directory: " + exception.Message);
            }
            try
            {
                Save();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Could not save the administrator account: " + exception.Message);
            }
            _logger.Info("Created initial administrator account " + username + ".");
            return account;
        }

        public UserAccount Find(string username)
        {
            for (int index = 0; index < _accounts.Count; index++)
            {
                if (string.Equals(_accounts[index].Username, username, StringComparison.OrdinalIgnoreCase))
                {
                    return _accounts[index];
                }
            }
            return null;
        }

        public IReadOnlyList<UserAccount> Accounts { get { return _accounts.AsReadOnly(); } }

        public void SetProfile(UserAccount account, string avatarId, ConsoleColor color)
        {
            if (account == null || !_accounts.Contains(account)) throw new InvalidOperationException("Unknown account.");
            if (!AvatarLibrary.IsValid(avatarId)) throw new InvalidOperationException("Unknown avatar.");
            bool allowed = false;
            for (int i = 0; i < ProfileColors.Choices.Length; i++) if (ProfileColors.Choices[i] == color) allowed = true;
            if (!allowed) throw new InvalidOperationException("Unsupported profile color.");
            string oldAvatar = account.AvatarId;
            ConsoleColor oldColor = account.ProfileColor;
            account.AvatarId = avatarId;
            account.ProfileColor = color;
            try { Save(); }
            catch { account.AvatarId = oldAvatar; account.ProfileColor = oldColor; throw; }
        }

        public bool Authenticate(string username, string password, out UserAccount account)
        {
            account = Find(username);
            return account != null && _passwords.Verify(account.Username, password, account.PasswordSalt, account.PasswordVerifier);
        }

        private void Save()
        {
            string text = "# SurfOS user database v2 - password verifiers and profile choices\r\n";
            for (int index = 0; index < _accounts.Count; index++)
            {
                UserAccount account = _accounts[index];
                text += account.Id + "|" + account.Username + "|" +
                        (account.IsAdministrator ? "admin" : "user") + "|" +
                        account.HomeDirectory + "|" + account.PasswordSalt + "|" +
                        account.PasswordVerifier + "|" + account.AvatarId + "|" + account.ProfileColor + "\r\n";
            }
            _fileSystem.WriteAllText(DatabasePath, text);
        }

        private static void ValidateUsername(string username)
        {
            if (username == null || username.Length < 2 || username.Length > 8)
            {
                throw new InvalidOperationException("Username must be 2 to 8 characters in the current FAT milestone.");
            }
            for (int index = 0; index < username.Length; index++)
            {
                char value = username[index];
                bool valid = (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') ||
                             (value >= '0' && value <= '9') || value == '-' || value == '_';
                if (!valid)
                {
                    throw new InvalidOperationException("Username may contain letters, numbers, '-' and '_'.");
                }
            }
        }
    }
}
