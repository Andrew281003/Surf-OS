using System;
using System.Collections.Generic;
using SurfOS.Users;

void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
}

Check(AvatarLibrary.All.Count >= 30, "avatar count");
var ids = new HashSet<string>();
var signatures = new HashSet<string>();
foreach (var avatar in AvatarLibrary.All)
{
    Check(ids.Add(avatar.Id), "unique id");
    Check(signatures.Add(string.Join("~", avatar.Small)), "unique small artwork");
    Check(AvatarLibrary.Get(avatar.Id) == avatar, "lookup");
    Check(avatar.Small.Length == 3 && avatar.Medium.Length == 7 && avatar.Large.Length == 13, "size variants");
    foreach (AvatarSize size in Enum.GetValues<AvatarSize>())
    {
        var lines = AvatarRenderer.Layout(avatar.Id, size, 12, 2, false);
        Check(lines.Length == avatar.Lines(size).Length, "render rows");
        foreach (var line in lines) Check(line.Length <= 12, "terminal clipping");
    }
}
Check(AvatarLibrary.Get("missing").Id == "pilot", "fallback avatar");
Check(ProfileColors.Normalize("invalid") == ProfileColors.Default, "fallback color");
Check(MenuNavigation.Move(0, 3, ConsoleKey.UpArrow) == 2, "navigation wrap");
Check(MenuNavigation.FirstVisible(8, 0, 3) == 6, "scrolling");

var storage = new MemoryStorage();
storage.Text = "# old database\n1000|alice|admin|/Users/alice|salt|verifier\n1001|bob|user|/Users/bob|salt|verifier\n";
var users = new UserService(storage, new FakeHasher(), new FakeLogger());
users.Load();
Check(users.Accounts.Count == 2 && users.Accounts[0].AvatarId == "pilot", "legacy accounts");
users.SetProfile(users.Accounts[1], "cat", ConsoleColor.Blue);
Check(storage.Text.Contains("1000|alice|admin|/Users/alice|salt|verifier|pilot|"), "legacy fields preserved");
Check(storage.Text.Contains("1001|bob|user|/Users/bob|salt|verifier|cat|Blue"), "profile persistence");
users.Load();
Check(users.Accounts[1].AvatarId == "cat" && users.Accounts[1].ProfileColor == ConsoleColor.Blue, "reload");
storage.Text = storage.Text.Replace("|cat|Blue", "|missing|notacolor");
users.Load();
Check(users.Accounts[1].AvatarId == "pilot" && users.Accounts[1].ProfileColor == ProfileColors.Default, "corrupt profile fallback");
Console.WriteLine("Avatar tests passed.");

sealed class MemoryStorage : SurfOS.Storage.IFileSystemService
{
    public string Text = "";
    public bool DirectoryExists(string path) => true;
    public void CreateDirectory(string path) { }
    public string ReadAllText(string path) => Text;
    public void WriteAllText(string path, string content) => Text = content;
}
sealed class FakeHasher : IPasswordHasher
{
    public string CreateSalt() => "salt";
    public string Hash(string username, string password, string salt) => "verifier";
    public bool Verify(string username, string password, string salt, string expectedVerifier) => password == "correct";
}
sealed class FakeLogger : SurfOS.Logging.IKernelLogger
{
    public void Info(string message) { }
}
namespace SurfOS.Storage
{
    public interface IFileSystemService
    {
        bool DirectoryExists(string path);
        void CreateDirectory(string path);
        string ReadAllText(string path);
        void WriteAllText(string path, string content);
    }
}
namespace SurfOS.Logging
{
    public interface IKernelLogger { void Info(string message); }
}
