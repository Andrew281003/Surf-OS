using System.IO.Compression;

namespace SurfOS2;

internal sealed record BackupInfo(string Name, long Size, DateTime Created);

internal static class Backup_Manager
{
    private const string FactoryBackupName = "Factory-Recovery";

    public static bool CreateFactoryRecovery(out string message) =>
        CreateBackup(FactoryBackupName, out message);

    public static bool CreateBackup(string name, out string message)
    {
        message = string.Empty;
        string safeName = name.Trim();
        if (!PathSafety.IsSafeFileName(safeName))
        {
            message = "Backup names must be safe filenames without path symbols.";
            return false;
        }

        string installRoot = Path.GetFullPath(Import.Variables.installPath);
        if (!Directory.Exists(installRoot))
        {
            message = "The SurfOS installation directory could not be found.";
            return false;
        }

        string backupDirectory = Path.Combine(installRoot, "preVersions");
        string backupPath = Path.Combine(backupDirectory, safeName + ".surfbak");
        string temporaryPath = backupPath + ".tmp";
        try
        {
            Directory.CreateDirectory(backupDirectory);
            if (File.Exists(backupPath))
            {
                message = $"A backup named '{safeName}' already exists.";
                return false;
            }

            using (FileStream output = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (ZipArchive archive = new(output, ZipArchiveMode.Create))
            {
                foreach (string file in EnumerateFiles(installRoot))
                {
                    string relativePath = Path.GetRelativePath(installRoot, file);
                    if (ShouldExclude(relativePath) || new FileInfo(file).LinkTarget is not null)
                    {
                        continue;
                    }

                    ZipArchiveEntry entry = archive.CreateEntry(
                        relativePath.Replace(Path.DirectorySeparatorChar, '/'),
                        CompressionLevel.Fastest);
                    entry.LastWriteTime = File.GetLastWriteTime(file);
                    using Stream input = File.OpenRead(file);
                    using Stream entryStream = entry.Open();
                    input.CopyTo(entryStream);
                }
            }

            File.Move(temporaryPath, backupPath);
            message = $"Backup '{safeName}' created ({FormatSize(new FileInfo(backupPath).Length)}).";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporaryPath);
            message = $"Backup failed: {ex.Message}";
            return false;
        }
    }

    public static IReadOnlyList<BackupInfo> ListBackups()
    {
        string directory = Path.Combine(Import.Variables.installPath, "preVersions");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.surfbak", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => new BackupInfo(
                Path.GetFileNameWithoutExtension(info.Name),
                info.Length,
                info.LastWriteTime))
            .ToArray();
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static bool ShouldExclude(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith("preVersions/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("partition.json", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("system/swap/swapfile.sys", StringComparison.OrdinalIgnoreCase) ||
               normalized is ".DS_Store" or "System Volume Information";
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    pending.Push(entry);
                }
                else
                {
                    yield return entry;
                }
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
