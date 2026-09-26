namespace SurfOS.Users
{
    public sealed class UserAccount
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string HomeDirectory { get; set; }
        public bool IsAdministrator { get; set; }
        public string PasswordSalt { get; set; }
        public string PasswordVerifier { get; set; }
        public string AvatarId { get; set; } = "pilot";
        public System.ConsoleColor ProfileColor { get; set; } = System.ConsoleColor.DarkCyan;
    }
}
