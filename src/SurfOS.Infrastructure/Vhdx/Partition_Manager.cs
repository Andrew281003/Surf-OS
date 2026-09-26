using System.Diagnostics;
using System.Text.Json.Serialization;

namespace SurfOS2;

internal sealed class PartitionManifest
{
    public string PartitionId { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "SURFOS";
    public string FileSystem { get; set; } = "SurfFS";
    public string HostFileSystem { get; set; } = "NTFS";
    public string MountPoint { get; set; } = "S:\\";
    public string DriveLetter { get; set; } = "S";
    public string BackingFilePath { get; set; } = string.Empty;
    public string VolumeType { get; set; } = "Fixed VHDX virtual disk";
    public string AllocationMode { get; set; } = "Fixed";
    public long CapacityBytes { get; set; }
    public long SystemReservedBytes { get; set; }
    public long SwapBytes { get; set; }
    public bool EncryptionEnabled { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public double CapacityGb => CapacityBytes / (double)Partition_Manager.BytesPerGb;
}

internal sealed class VhdMountConfiguration
{
    public string VhdxPath { get; set; } = string.Empty;
    public string PreferredDriveLetter { get; set; } = "S";
    public string InstallDirectoryName { get; set; } = "SurfOS";
    public string VolumeLabel { get; set; } = "SURFOS";
    public bool InstallationComplete { get; set; }
    public bool AutomaticMountEnabled { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

internal static class Partition_Manager
{
    public const long BytesPerGb = 1024L * 1024L * 1024L;
    public const int DefaultSizeGb = 15;
    public const int MinimumSizeGb = 4;
    public const int MaximumSizeGb = 512;
    public const int SystemReservedMb = 512;

    private const string VhdxFileName = "SurfOS-System.vhdx";
    private const string InstallDirectoryName = "";
    private const string AutomaticMountTaskName = "SurfOS Mount System Drive";
    private static readonly char[] PreferredDriveLetters =
        "SRQPONMLKJIHGFED".ToCharArray();

    public static string DefaultDirectoryInstallPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SurfOS",
        "System");

    public static string MountConfigurationPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SurfOS",
        "mount.json");

    private static string AutomaticMountScriptPath => Path.Combine(
        Path.GetDirectoryName(MountConfigurationPath)!,
        "mount-vhdx.txt");

    public static bool HasMountConfiguration =>
        OperatingSystem.IsWindows() && File.Exists(MountConfigurationPath);

    public static string ManifestPath(string installPath) =>
        Path.Combine(installPath, "partition.json");

    public static string GetPreferredDriveLetter()
    {
        foreach (char letter in PreferredDriveLetters)
        {
            if (!Directory.Exists($"{letter}:\\"))
            {
                return letter.ToString();
            }
        }

        throw new IOException("No free drive letter is available for the SurfOS disk.");
    }

    public static bool ProvisionFixedVhdx(out string error)
    {
        if (!OperatingSystem.IsWindows())
        {
            error = "VHDX system drives are available on Windows only.";
            return false;
        }

        error = string.Empty;
        string hostDirectory = Import.Variables.vhdxHostDirectory;
        if (string.IsNullOrWhiteSpace(hostDirectory))
        {
            error = "The VHDX storage location was not configured.";
            return false;
        }

        string vhdxPath = Path.GetFullPath(Path.Combine(hostDirectory, VhdxFileName));
        if (File.Exists(vhdxPath))
        {
            error = $"A SurfOS disk image already exists at {vhdxPath}.";
            return false;
        }

        long requiredBytes = Import.Variables.partitionSizeGb * BytesPerGb;
        long availableBytes = GetAvailableDriveBytes(hostDirectory);
        if (availableBytes > 0 && availableBytes < requiredBytes + 256L * 1024L * 1024L)
        {
            error = "The host drive does not have enough free space for the fixed-size SurfOS disk.";
            return false;
        }

        Directory.CreateDirectory(hostDirectory);
        string driveLetter = Import.Variables.partitionDriveLetter;
        if (string.IsNullOrWhiteSpace(driveLetter) || Directory.Exists($"{driveLetter}:\\"))
        {
            driveLetter = GetPreferredDriveLetter();
        }

        Import.Variables.partitionDriveLetter = driveLetter;
        int maximumMegabytes = checked(Import.Variables.partitionSizeGb * 1024);
        string[] commands =
        [
            $"create vdisk file=\"{vhdxPath}\" maximum={maximumMegabytes} type=fixed",
            $"select vdisk file=\"{vhdxPath}\"",
            "attach vdisk",
            "create partition primary",
            $"format fs=ntfs label=\"{Import.Variables.partitionLabel}\" quick",
            $"assign letter={driveLetter}",
            "exit"
        ];

        Console.WriteLine(
            $"Creating {Import.Variables.partitionSizeGb} GB fixed SurfOS disk. " +
            "This can take several minutes...");

        if (!RunDiskPart(commands, TimeSpan.FromMinutes(10), out string diskPartOutput))
        {
            CleanupFailedProvision(vhdxPath);
            error = $"Windows could not create the SurfOS disk. {SummarizeOutput(diskPartOutput)}";
            return false;
        }

        string driveRoot = $"{driveLetter}:\\";
        if (!WaitFor(
                () => IsExpectedCreatedVolume(driveRoot, requiredBytes),
                TimeSpan.FromSeconds(20)))
        {
            CleanupFailedProvision(vhdxPath);
            error = $"Windows created the disk image but could not verify it at {driveRoot}.";
            return false;
        }

        string installPath = driveRoot;
        Import.Variables.vhdxPath = vhdxPath;
        Import.Variables.installPath = installPath;

        SaveMountConfiguration(new VhdMountConfiguration
        {
            VhdxPath = vhdxPath,
            PreferredDriveLetter = driveLetter,
            InstallDirectoryName = InstallDirectoryName,
            VolumeLabel = Import.Variables.partitionLabel,
            InstallationComplete = false
        });

        return true;
    }

    public static bool ProvisionSystemStorage(out string error)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProvisionFixedVhdx(out error);
        }

        error = string.Empty;
        if (!OperatingSystem.IsMacOS())
        {
            error = "Directory-backed system storage is currently supported on macOS only.";
            return false;
        }

        try
        {
            string installPath = Path.GetFullPath(Import.Variables.installPath);
            string root = Path.GetPathRoot(installPath) ?? string.Empty;
            if (installPath.Equals(root, StringComparison.Ordinal))
            {
                error = "The filesystem root cannot be used as the SurfOS installation directory.";
                return false;
            }

            if (Directory.Exists(installPath) &&
                Directory.EnumerateFileSystemEntries(installPath).Any() &&
                !File.Exists(Path.Combine(installPath, "installer_feedback.json")))
            {
                error = $"The installation directory is not empty: {installPath}";
                return false;
            }

            Directory.CreateDirectory(installPath);
            Import.Variables.vhdxHostDirectory = string.Empty;
            Import.Variables.vhdxPath = string.Empty;
            Import.Variables.partitionDriveLetter = "/";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void CommitMountConfiguration()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        VhdMountConfiguration? configuration = LoadMountConfiguration();
        if (configuration is null)
        {
            return;
        }

        configuration.PreferredDriveLetter = Import.Variables.partitionDriveLetter;
        configuration.InstallationComplete = true;
        configuration.AutomaticMountEnabled = ConfigureAutomaticMount(
            configuration,
            out string automaticMountError);
        SaveMountConfiguration(configuration);

        if (!configuration.AutomaticMountEnabled)
        {
            KernelLog.Warning(
                "install",
                $"automatic VHDX mount task could not be registered: {automaticMountError}");
        }
    }

    public static string? TryMountConfiguredVhdx(out string error)
    {
        if (!OperatingSystem.IsWindows())
        {
            error = "VHDX system drives are available on Windows only.";
            return null;
        }

        error = string.Empty;
        VhdMountConfiguration? configuration = LoadMountConfiguration();
        if (configuration is null)
        {
            error = "The SurfOS mount configuration is missing or unreadable.";
            return null;
        }

        if (!File.Exists(configuration.VhdxPath))
        {
            error = $"The configured SurfOS disk image is missing: {configuration.VhdxPath}";
            return null;
        }

        string? mountedPath = FindMountedInstall(configuration);
        if (mountedPath is not null)
        {
            if (!configuration.InstallationComplete)
            {
                error = "A previous SurfOS installation was interrupted before it completed.";
                return null;
            }

            ApplyMountedConfiguration(configuration, mountedPath);
            return mountedPath;
        }

        string driveLetter = configuration.PreferredDriveLetter;
        if (string.IsNullOrWhiteSpace(driveLetter) || Directory.Exists($"{driveLetter}:\\"))
        {
            driveLetter = GetPreferredDriveLetter();
        }

        string[] commands =
        [
            $"select vdisk file=\"{configuration.VhdxPath}\"",
            "attach vdisk noerr",
            "select partition 1",
            $"assign letter={driveLetter} noerr",
            "exit"
        ];

        if (!RunDiskPart(commands, TimeSpan.FromMinutes(2), out string diskPartOutput))
        {
            error = $"Windows could not mount the SurfOS disk. {SummarizeOutput(diskPartOutput)}";
            return null;
        }

        string expectedPath = Path.Combine($"{driveLetter}:\\", configuration.InstallDirectoryName);
        WaitFor(
            () => File.Exists(Path.Combine(expectedPath, "installer_feedback.json")),
            TimeSpan.FromSeconds(20));

        mountedPath = File.Exists(Path.Combine(expectedPath, "installer_feedback.json"))
            ? expectedPath
            : FindMountedInstall(configuration);
        if (mountedPath is null)
        {
            error = configuration.InstallationComplete
                ? "The SurfOS disk mounted, but its installation files could not be found."
                : "A previous SurfOS installation was interrupted before it completed.";
            return null;
        }

        configuration.PreferredDriveLetter =
            (Path.GetPathRoot(mountedPath) ?? $"{driveLetter}:\\")[0].ToString();
        ApplyMountedConfiguration(configuration, mountedPath);
        configuration.AutomaticMountEnabled = ConfigureAutomaticMount(
            configuration,
            out _);
        SaveMountConfiguration(configuration);
        return mountedPath;
    }

    public static PartitionManifest Create(string installPath)
    {
        string driveRoot = OperatingSystem.IsWindows()
            ? Path.GetPathRoot(installPath) ?? $"{Import.Variables.partitionDriveLetter}:\\"
            : Path.GetFullPath(installPath);
        PartitionManifest manifest = new()
        {
            Label = string.IsNullOrWhiteSpace(Import.Variables.partitionLabel)
                ? "SURFOS"
                : Import.Variables.partitionLabel.Trim(),
            FileSystem = Import.Variables.partitionFileSystem,
            MountPoint = driveRoot,
            DriveLetter = Import.Variables.partitionDriveLetter,
            BackingFilePath = Import.Variables.vhdxPath,
            HostFileSystem = OperatingSystem.IsWindows() ? "NTFS" : GetHostFileSystem(installPath),
            VolumeType = OperatingSystem.IsWindows()
                ? "Fixed VHDX virtual disk"
                : "Directory-backed virtual volume",
            AllocationMode = OperatingSystem.IsWindows() ? "Fixed" : "Logical quota",
            CapacityBytes = Import.Variables.partitionSizeGb * BytesPerGb,
            SystemReservedBytes = SystemReservedMb * 1024L * 1024L,
            SwapBytes = Import.Variables.swapSizeGb * BytesPerGb,
            EncryptionEnabled = Import.Variables.partitionEncryption
        };

        JsonStorage.Write(ManifestPath(installPath), manifest);
        return manifest;
    }

    public static PartitionManifest? Load(string installPath)
    {
        string path = ManifestPath(installPath);
        return File.Exists(path) ? JsonStorage.Read<PartitionManifest>(path) : null;
    }

    public static bool IsConfiguredVhdxInstall(string installPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        VhdMountConfiguration? configuration = LoadMountConfiguration();
        PartitionManifest? manifest = Load(installPath);
        return configuration is not null && manifest is not null &&
               PathsEqual(configuration.VhdxPath, manifest.BackingFilePath) &&
               File.Exists(Path.Combine(installPath, "installer_feedback.json"));
    }

    public static bool RemoveConfiguredVhdx(string installPath, out string error)
    {
        if (!OperatingSystem.IsWindows())
        {
            error = "VHDX system drives are available on Windows only.";
            return false;
        }

        error = string.Empty;
        VhdMountConfiguration? configuration = LoadMountConfiguration();
        PartitionManifest? manifest = Load(installPath);
        if (configuration is null || manifest is null ||
            !PathsEqual(configuration.VhdxPath, manifest.BackingFilePath) ||
            !File.Exists(configuration.VhdxPath))
        {
            error = "The mounted SurfOS VHDX could not be safely verified.";
            return false;
        }

        RemoveAutomaticMount();

        string[] commands =
        [
            $"select vdisk file=\"{configuration.VhdxPath}\"",
            "detach vdisk",
            "exit"
        ];
        if (!RunDiskPart(commands, TimeSpan.FromMinutes(2), out string diskPartOutput))
        {
            error = $"Windows could not detach the SurfOS disk. {SummarizeOutput(diskPartOutput)}";
            return false;
        }

        Exception? lastError = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                File.Delete(configuration.VhdxPath);
                File.Delete(MountConfigurationPath);
                return true;
            }
            catch (IOException ex)
            {
                lastError = ex;
                Thread.Sleep(250);
            }
        }

        error = $"The disk was detached, but its backing file could not be deleted: {lastError?.Message}";
        return false;
    }

    public static long GetUsedBytes(string installPath)
    {
        if (!Directory.Exists(installPath))
        {
            return 0;
        }

        try
        {
            return EnumerateFilesIncludingHidden(installPath)
                .Where(path => !path.Equals(ManifestPath(installPath), StringComparison.OrdinalIgnoreCase))
                .Sum(path => new FileInfo(path).Length);
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public static bool CanAllocate(string installPath, long additionalBytes, out string error)
    {
        error = string.Empty;
        PartitionManifest? manifest = Load(installPath);
        if (manifest is null || additionalBytes <= 0)
        {
            return true;
        }

        // Swap is represented by an allocated file and is already included in used bytes.
        long usableCapacity = manifest.CapacityBytes - manifest.SystemReservedBytes;
        long used = GetUsedBytes(installPath);
        if (used + additionalBytes <= usableCapacity)
        {
            return true;
        }

        double availableGb = Math.Max(0, usableCapacity - used) / (double)BytesPerGb;
        error = $"SurfOS partition is full. {availableGb:0.00} GB remains on {manifest.Label}.";
        return false;
    }

    public static long GetAvailableDriveBytes(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return 0;
        }

        try
        {
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static VhdMountConfiguration? LoadMountConfiguration()
    {
        try
        {
            return File.Exists(MountConfigurationPath)
                ? JsonStorage.Read<VhdMountConfiguration>(MountConfigurationPath)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void SaveMountConfiguration(VhdMountConfiguration configuration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MountConfigurationPath)!);
        JsonStorage.Write(MountConfigurationPath, configuration);
    }

    private static string? FindMountedInstall(VhdMountConfiguration configuration)
    {
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                string candidate = Path.Combine(drive.RootDirectory.FullName, configuration.InstallDirectoryName);
                if (!File.Exists(Path.Combine(candidate, "installer_feedback.json")))
                {
                    continue;
                }

                PartitionManifest? manifest = Load(candidate);
                if (manifest is not null && PathsEqual(manifest.BackingFilePath, configuration.VhdxPath))
                {
                    return candidate;
                }
            }
            catch (IOException)
            {
                // Ignore a drive that became unavailable during enumeration.
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore volumes that Windows does not allow this process to inspect.
            }
        }

        return null;
    }

    private static void ApplyMountedConfiguration(
        VhdMountConfiguration configuration,
        string mountedInstallPath)
    {
        string driveRoot = Path.GetPathRoot(mountedInstallPath) ?? string.Empty;
        Import.Variables.vhdxPath = configuration.VhdxPath;
        Import.Variables.vhdxHostDirectory =
            Path.GetDirectoryName(configuration.VhdxPath) ?? string.Empty;
        Import.Variables.partitionDriveLetter = driveRoot.Length > 0
            ? driveRoot[0].ToString()
            : configuration.PreferredDriveLetter;
        Import.Variables.installPath = mountedInstallPath;
    }

    private static bool RunDiskPart(
        IEnumerable<string> commands,
        TimeSpan timeout,
        out string output)
    {
        string scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"surfos-diskpart-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllLines(scriptPath, commands);
            ProcessStartInfo startInfo = new("diskpart.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add(scriptPath);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("diskpart.exe could not be started.");
            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                output = "DiskPart timed out.";
                return false;
            }

            string standardOutput = standardOutputTask.GetAwaiter().GetResult();
            string standardError = standardErrorTask.GetAwaiter().GetResult();
            output = $"{standardOutput}\n{standardError}".Trim();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return false;
        }
        finally
        {
            try
            {
                File.Delete(scriptPath);
            }
            catch
            {
                // The temporary script contains no credentials; cleanup can retry on reboot.
            }
        }
    }

    private static void CleanupFailedProvision(string vhdxPath)
    {
        if (!File.Exists(vhdxPath))
        {
            return;
        }

        RunDiskPart(
            [$"select vdisk file=\"{vhdxPath}\"", "detach vdisk noerr", "exit"],
            TimeSpan.FromMinutes(1),
            out _);
        try
        {
            File.Delete(vhdxPath);
        }
        catch
        {
            // Preserve the failed image for manual recovery if Windows still owns a handle.
        }
    }

    private static bool ConfigureAutomaticMount(
        VhdMountConfiguration configuration,
        out string error)
    {
        error = string.Empty;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AutomaticMountScriptPath)!);
            File.WriteAllLines(
                AutomaticMountScriptPath,
                [
                    $"select vdisk file=\"{configuration.VhdxPath}\"",
                    "attach vdisk noerr",
                    "select partition 1",
                    $"assign letter={configuration.PreferredDriveLetter} noerr",
                    "exit"
                ]);

            string taskAction = $"diskpart.exe /s \"{AutomaticMountScriptPath}\"";
            string[] arguments =
            [
                "/Create", "/F",
                "/SC", "ONSTART",
                "/RU", "SYSTEM",
                "/RL", "HIGHEST",
                "/TN", AutomaticMountTaskName,
                "/TR", taskAction
            ];
            if (!RunProcess("schtasks.exe", arguments, TimeSpan.FromMinutes(1), out string output))
            {
                error = SummarizeOutput(output);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void RemoveAutomaticMount()
    {
        RunProcess(
            "schtasks.exe",
            ["/Delete", "/F", "/TN", AutomaticMountTaskName],
            TimeSpan.FromMinutes(1),
            out _);
        try
        {
            File.Delete(AutomaticMountScriptPath);
        }
        catch
        {
            // A stale task script is harmless and contains no credentials.
        }
    }

    private static bool RunProcess(
        string executable,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        out string output)
    {
        try
        {
            ProcessStartInfo startInfo = new(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"{executable} could not be started.");
            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                output = $"{executable} timed out.";
                return false;
            }

            output = $"{standardOutputTask.GetAwaiter().GetResult()}\n" +
                     standardErrorTask.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return false;
        }
    }

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(150);
        }

        return condition();
    }

    private static bool IsExpectedCreatedVolume(string driveRoot, long requestedCapacityBytes)
    {
        try
        {
            DriveInfo drive = new(driveRoot);
            if (!drive.IsReady ||
                !drive.VolumeLabel.Equals(
                    Import.Variables.partitionLabel,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            const long tolerance = 128L * 1024L * 1024L;
            return Math.Abs(drive.TotalSize - requestedCapacityBytes) <= tolerance;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool PathsEqual(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        return Path.GetFullPath(first).Equals(
            Path.GetFullPath(second),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string SummarizeOutput(string output)
    {
        string normalized = output.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 500 ? normalized : normalized[^500..];
    }

    private static IEnumerable<string> EnumerateFilesIncludingHidden(string root)
    {
        Stack<string> pending = new();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string entry in entries)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

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

    private static string GetHostFileSystem(string path)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "/";
            return new DriveInfo(root).DriveFormat;
        }
        catch
        {
            return OperatingSystem.IsMacOS() ? "APFS/macOS" : "Host filesystem";
        }
    }
}
