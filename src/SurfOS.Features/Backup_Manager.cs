using System.IO.Compression;
using System.Text.Json;

namespace SurfOS2;

internal sealed record BackupInfo(string Name, string Path, long Size, DateTime Created);

internal static class Backup_Manager
{
    private const string BackupExtension = ".surfbak";
    private const int CopyBufferSize = 1024 * 1024;

    public static bool CreateBackup(string requestedName, out string message)
    {
        return CreateBackupInternal(
            requestedName,
            overwrite: false,
            showProgress: true,
            out message);
    }

    public static bool CreateFactoryRecovery(out string message)
    {
        return CreateBackupInternal(
            "Factory-Recovery",
            overwrite: true,
            showProgress: true,
            out message);
    }

    public static IReadOnlyList<BackupInfo> ListBackups()
    {
        string backupDirectory = GetBackupDirectory();
        if (!Directory.Exists(backupDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(backupDirectory, $"*{BackupExtension}")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTime)
            .Select(file => new BackupInfo(
                Path.GetFileNameWithoutExtension(file.Name),
                file.FullName,
                file.Length,
                file.CreationTime))
            .ToList();
    }

    private static bool CreateBackupInternal(
        string requestedName,
        bool overwrite,
        bool showProgress,
        out string message)
    {
        message = string.Empty;
        if (!TryNormalizeName(requestedName, out string name, out message))
        {
            return false;
        }

        string root = Import.Variables.installPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            message = "The SurfOS system drive is not mounted.";
            return false;
        }

        string backupDirectory = GetBackupDirectory();
        Directory.CreateDirectory(backupDirectory);
        string destination = Path.Combine(backupDirectory, name + BackupExtension);
        string partialPath = destination + ".partial";
        if (File.Exists(destination) && !overwrite)
        {
            message = $"A backup named '{name}' already exists.";
            return false;
        }

        List<string> sourceFiles = EnumerateBackupFiles(root).ToList();
        long sourceBytes = sourceFiles.Sum(path => new FileInfo(path).Length);
        long safetyMargin = 64L * 1024L * 1024L;
        if (!Partition_Manager.CanAllocate(root, sourceBytes + safetyMargin, out message))
        {
            return false;
        }

        try
        {
            DriveInfo drive = new(Path.GetPathRoot(root)!);
            if (drive.AvailableFreeSpace < sourceBytes + safetyMargin)
            {
                message = $"Not enough free space. This backup needs about {FormatSize(sourceBytes + safetyMargin)}.";
                return false;
            }
        }
        catch (IOException)
        {
            // The partition manifest check above remains the fallback capacity check.
        }

        try
        {
            File.Delete(partialPath);
            if (overwrite)
            {
                File.Delete(destination);
            }

            long copiedBytes = 0;
            int lastPercent = -1;
            using (FileStream archiveStream = new(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (ZipArchive archive = new(archiveStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (string sourcePath in sourceFiles)
                {
                    string relativePath = Path.GetRelativePath(root, sourcePath)
                        .Replace('\\', '/');
                    ZipArchiveEntry entry = archive.CreateEntry(
                        relativePath,
                        CompressionLevel.NoCompression);
                    entry.LastWriteTime = File.GetLastWriteTime(sourcePath);

                    using FileStream source = new(
                        sourcePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        CopyBufferSize,
                        FileOptions.SequentialScan);
                    using Stream target = entry.Open();
                    byte[] buffer = new byte[CopyBufferSize];
                    int bytesRead;
                    while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        target.Write(buffer, 0, bytesRead);
                        copiedBytes += bytesRead;
                        if (showProgress && sourceBytes > 0)
                        {
                            int percent = (int)Math.Min(100, copiedBytes * 100 / sourceBytes);
                            if (percent != lastPercent)
                            {
                                Console.Write($"\rBACKUP SNAPSHOT [{percent,3}%] {FormatSize(copiedBytes)} / {FormatSize(sourceBytes)}");
                                lastPercent = percent;
                            }
                        }
                    }
                }

                ZipArchiveEntry manifestEntry = archive.CreateEntry(
                    "backup-manifest.json",
                    CompressionLevel.NoCompression);
                using StreamWriter writer = new(manifestEntry.Open());
                writer.Write(JsonSerializer.Serialize(new
                {
                    Name = name,
                    CreatedUtc = DateTime.UtcNow,
                    SourceVolume = root,
                    FileCount = sourceFiles.Count,
                    SourceBytes = sourceBytes,
                    Exclusions = new[]
                    {
                        "preVersions (prevents recursive backups)",
                        "system/swap/swapfile.sys (volatile)",
                        "Windows volume metadata"
                    }
                }, new JsonSerializerOptions { WriteIndented = true }));
            }

            if (showProgress)
            {
                Console.WriteLine();
            }

            File.Move(partialPath, destination, overwrite: false);
            FileInfo completed = new(destination);
            message = $"Backup '{name}' created in preVersions ({FormatSize(completed.Length)}).";
            KernelLog.Success("backup", message);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(partialPath);
            }
            catch
            {
            }

            message = $"Backup failed: {ex.Message}";
            KernelLog.Error("backup", message);
            return false;
        }
    }

    private static IEnumerable<string> EnumerateBackupFiles(string root)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };
        string backupDirectory = Path.GetFullPath(GetBackupDirectory())
            .TrimEnd(Path.DirectorySeparatorChar);
        foreach (string path in Directory.EnumerateFiles(root, "*", options))
        {
            string fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(
                    backupDirectory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = Path.GetRelativePath(root, fullPath)
                .Replace('\\', '/');
            if (relative.Equals("system/swap/swapfile.sys", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("System Volume Information/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("$RECYCLE.BIN/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return fullPath;
        }
    }

    private static bool TryNormalizeName(
        string requestedName,
        out string name,
        out string error)
    {
        name = requestedName.Trim();
        error = string.Empty;
        if (name.EndsWith(BackupExtension, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^BackupExtension.Length];
        }

        if (name.Length is < 1 or > 64 || name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "Backup names must be 1-64 characters and cannot contain path symbols.";
            return false;
        }

        return true;
    }

    private static string GetBackupDirectory() =>
        Path.Combine(Import.Variables.installPath, "preVersions");

    public static string FormatSize(long bytes)
    {
        double gb = bytes / (double)Partition_Manager.BytesPerGb;
        return gb >= 1
            ? $"{gb:0.00} GB"
            : $"{bytes / (1024d * 1024d):0.00} MB";
    }
}
