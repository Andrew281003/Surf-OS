namespace SurfOS.Users
{
    public interface IPasswordHasher
    {
        string CreateSalt();
        string Hash(string username, string password, string salt);
        bool Verify(string username, string password, string salt, string expectedVerifier);
    }
}
