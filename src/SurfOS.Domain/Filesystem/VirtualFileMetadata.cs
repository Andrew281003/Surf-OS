using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SurfOS2;

internal sealed class VirtualFileMetadataStore
{
    public Dictionary<string, VirtualFileMetadata> Entries { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class VirtualFileMetadata
{
    public string Owner { get; set; } = string.Empty;
    public int Mode { get; set; }
}

internal static partial class VirtualFileSystem
{
    public static bool CreateLink(string target, string link, bool symbolic, out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string targetVirtual = NormalizeVirtualPath(target);
            string linkVirtual = NormalizeVirtualPath(link);
            string targetReal = ResolveVirtualPath(targetVirtual);
            string linkReal = ResolveVirtualPath(linkVirtual);

            if (IsSensitiveReadPath(targetReal) || IsProtectedMutationPath(linkReal))
            {
                error = "Protected SurfOS system data cannot be linked or modified from the shell.";
                return false;
            }
            if (!File.Exists(targetReal) && !Directory.Exists(targetReal))
            {
                error = $"Target not found: {targetVirtual}";
                return false;
            }
            // Reuse the inspection boundary so a link cannot be created through an
            // existing host junction or symbolic link that leaves the SurfOS root.
            _ = Inspect(targetVirtual);
            if (File.Exists(linkReal) || Directory.Exists(linkReal))
            {
                error = $"Link path already exists: {linkVirtual}";
                return false;
            }
            if (!symbolic && Directory.Exists(targetReal))
            {
                error = "Hard links can only target files; use -s for a directory.";
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(linkReal) ?? GetRoot());
            if (symbolic)
            {
                if (Directory.Exists(targetReal)) Directory.CreateSymbolicLink(linkReal, targetReal);
                else File.CreateSymbolicLink(linkReal, targetReal);
            }
            else
            {
                CreateNativeHardLink(linkReal, targetReal);
            }

            VirtualFileMetadata metadata = GetMetadataCore(targetVirtual, Directory.Exists(targetReal));
            SetMetadataCore(linkVirtual, metadata.Owner, metadata.Mode);
            return true;
        }
    }

    public static bool ChangeMode(string path, int mode, out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string virtualPath = NormalizeVirtualPath(path);
            string realPath = ResolveVirtualPath(virtualPath);
            bool directory = Directory.Exists(realPath);
            if (!directory && !File.Exists(realPath))
            {
                error = $"Path not found: {virtualPath}";
                return false;
            }
            if (IsProtectedMutationPath(realPath))
            {
                error = "Protected SurfOS system data cannot be modified from the shell.";
                return false;
            }

            VirtualFileMetadata current = GetMetadataCore(virtualPath, directory);
            SetMetadataCore(virtualPath, current.Owner, mode);

            // Windows has no POSIX mode bits. Its read-only bit is the closest host
            // enforcement available for regular files; the complete mode remains in
            // SurfOS metadata and is displayed by ls/stat.
            if (!directory)
            {
                FileAttributes attributes = File.GetAttributes(realPath);
                if ((mode & 0b010_010_010) == 0) attributes |= FileAttributes.ReadOnly;
                else attributes &= ~FileAttributes.ReadOnly;
                File.SetAttributes(realPath, attributes);
            }
            return true;
        }
    }

    public static bool ChangeOwner(string path, string owner, out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string virtualPath = NormalizeVirtualPath(path);
            string realPath = ResolveVirtualPath(virtualPath);
            bool directory = Directory.Exists(realPath);
            if (!directory && !File.Exists(realPath))
            {
                error = $"Path not found: {virtualPath}";
                return false;
            }
            if (IsProtectedMutationPath(realPath))
            {
                error = "Protected SurfOS system data cannot be modified from the shell.";
                return false;
            }
            VirtualFileMetadata current = GetMetadataCore(virtualPath, directory);
            SetMetadataCore(virtualPath, owner, current.Mode);
            return true;
        }
    }

    public static (string Owner, int Mode) GetMetadata(string path)
    {
        lock (SyncRoot)
        {
            string virtualPath = NormalizeVirtualPath(path);
            string realPath = ResolveVirtualPath(virtualPath);
            bool directory = Directory.Exists(realPath);
            if (!directory && !File.Exists(realPath)) throw new FileNotFoundException($"Path not found: {virtualPath}");
            VirtualFileMetadata value = GetMetadataCore(virtualPath, directory);
            return (value.Owner, value.Mode);
        }
    }

    public static string FormatMode(bool directory, int mode)
    {
        Span<char> result = stackalloc char[10];
        result[0] = directory ? 'd' : '-';
        const string flags = "rwxrwxrwx";
        for (int i = 0; i < 9; i++)
            result[i + 1] = (mode & (1 << (8 - i))) != 0 ? flags[i] : '-';
        return new string(result);
    }

    private static VirtualFileMetadata GetMetadataCore(string virtualPath, bool directory)
    {
        VirtualFileMetadataStore store = LoadMetadata();
        return store.Entries.TryGetValue(virtualPath, out VirtualFileMetadata? value)
            ? value
            : new VirtualFileMetadata
            {
                Owner = string.IsNullOrWhiteSpace(Import.Variables.userName) ? "Guest" : Import.Variables.userName,
                Mode = directory ? Convert.ToInt32("755", 8) : Convert.ToInt32("644", 8)
            };
    }

    private static void SetMetadataCore(string virtualPath, string owner, int mode)
    {
        VirtualFileMetadataStore store = LoadMetadata();
        store.Entries[virtualPath] = new VirtualFileMetadata { Owner = owner, Mode = mode };
        JsonStorage.Write(MetadataPath(), store);
    }

    private static VirtualFileMetadataStore LoadMetadata()
    {
        string path = MetadataPath();
        VirtualFileMetadataStore loaded = File.Exists(path)
            ? JsonStorage.Read<VirtualFileMetadataStore>(path) ?? new VirtualFileMetadataStore()
            : new VirtualFileMetadataStore();
        loaded.Entries = new Dictionary<string, VirtualFileMetadata>(loaded.Entries, StringComparer.OrdinalIgnoreCase);
        return loaded;
    }

    private static void RemoveMetadataCore(string virtualPath, bool descendants = false)
    {
        VirtualFileMetadataStore store = LoadMetadata();
        string prefix = virtualPath.TrimEnd('/') + "/";
        bool changed = false;
        foreach (string key in store.Entries.Keys
                     .Where(key => key.Equals(virtualPath, StringComparison.OrdinalIgnoreCase) ||
                                   descendants && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
            changed |= store.Entries.Remove(key);
        if (changed) JsonStorage.Write(MetadataPath(), store);
    }

    private static string MetadataPath() => Path.Combine(GetRoot(), "system", "vfs-metadata.json");

    private static void CreateNativeHardLink(string linkPath, string targetPath)
    {
        bool created = OperatingSystem.IsWindows()
            ? CreateHardLinkWindows(linkPath, targetPath, IntPtr.Zero)
            : CreateHardLinkUnix(targetPath, linkPath) == 0;
        if (!created) throw new IOException("The host filesystem could not create the hard link.", new Win32Exception(Marshal.GetLastPInvokeError()));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkWindows(string newFileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int CreateHardLinkUnix(string existingFileName, string newFileName);
}
