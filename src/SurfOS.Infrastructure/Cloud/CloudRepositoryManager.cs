using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;

namespace SurfOS2;

internal sealed class StoreManifest
{
    public string Repository { get; set; } = "SurfOS Cloud Store";
    public string Version { get; set; } = "1";
    public List<StorePackageManifest> Packages { get; set; } = [];
}

internal sealed class StorePackageManifest
{
    public string Id { get; set; } = string.Empty; // Id of the actual package, used for installation and removal
    public string Name { get; set; } = string.Empty; // Name of the package, used for display purposes
    public string Version { get; set; } = "1.0.0"; // Version of the package. Default number is 1.0.0
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Utilities";
    public string DownloadUrl { get; set; } = string.Empty;
    public string EncodedDownloadUrl { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public List<string> Dependencies { get; set; } = [];
    public string Sha256 { get; set; } = string.Empty;
    public string MinimumSurfOSVersion { get; set; } = Import.Variables.version; // The minimum SurfOS version required for this package to be installed
    public bool AllowUserDataDelete { get; set; }
    public string Command { get; set; } = string.Empty;
}

internal sealed class InstalledStorePackage
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public bool AllowUserDataDelete { get; set; }
    public DateTime InstalledAt { get; set; } = DateTime.Now;
    public List<string> InstalledFiles { get; set; } = [];
}

internal sealed class InstalledStorePackageState
{
    public List<InstalledStorePackage> Packages { get; set; } = [];
}

internal sealed class CloudManifestResult
{
    public StoreManifest Manifest { get; init; } = new();
    public bool IsOffline { get; init; }
    public string Message { get; init; } = string.Empty;
}

internal static class CloudRepositoryManager
{
    private const long MaximumManifestBytes = 1024 * 1024;
    private const long MaximumPackageBytes = 64 * 1024 * 1024;
    private const string CloudUrlKey = "SurfCloud";
    private const string EncodedPackagesManifestUrl =
        "OwEGFjBWQFoAIRwEA20LABoDPxBcBSwBQAAHbBAKFiweG0gAPAIcCiwNC1MNN0hDHiQGKjciA0IUXgddKxouITI2FBEiWzonICEkBToiWSc=";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static bool IsInternetAvailable()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(networkInterface =>
                networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                networkInterface.OperationalStatus == OperationalStatus.Up);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<CloudManifestResult> LoadRemotePackagesAsync(bool forceRefresh)
    {
        EnsureDirectories();

        if (!Import.Variables.cloudServicesEnabled ||
            Import.Variables.safeMode ||
            Import.Variables.networkProfile.Equals("Offline", StringComparison.OrdinalIgnoreCase))
        {
            return LoadOfflineManifest("SurfCloud is disabled. Showing cached and bundled packages.");
        }

        if (!forceRefresh && File.Exists(GetCachedManifestPath()))
        {
            StoreManifest? cached = ReadManifest(GetCachedManifestPath());
            if (cached is not null)
            {
                return new CloudManifestResult { Manifest = MergeWithSeedManifest(cached) };
            }
        }

        if (!IsInternetAvailable())
        {
            return LoadOfflineManifest("Surf Store is offline. Showing installed apps only.");
        }

        try
        {
            using HttpClient client = new()
            {
                Timeout = TimeSpan.FromSeconds(12),
                MaxResponseContentBufferSize = MaximumManifestBytes
            };
            string json = await client.GetStringAsync(DecodeCloudUrl(EncodedPackagesManifestUrl));
            StoreManifest? manifest = JsonSerializer.Deserialize<StoreManifest>(json, JsonOptions);
            if (manifest is null)
            {
                return LoadOfflineManifest("Invalid cloud manifest. Showing installed apps only.");
            }
            NormalizeManifest(manifest);
            if (manifest.Packages.Count == 0)
            {
                return LoadOfflineManifest("Invalid cloud manifest. Showing installed apps only.");
            }

            StoreManifest mergedManifest = MergeWithSeedManifest(manifest);
            File.WriteAllText(GetCachedManifestPath(), JsonSerializer.Serialize(mergedManifest, JsonOptions));
            Log("Cloud package manifest refreshed.");
            KernelLog.Success("store", $"loaded {mergedManifest.Packages.Count} cloud package(s)");
            return new CloudManifestResult { Manifest = mergedManifest };
        }
        catch (Exception ex)
        {
            KernelLog.Warning("store", $"cloud manifest load failed: {ex.Message}");
            return LoadOfflineManifest("Surf Store is offline. Showing installed apps only.");
        }
    }

    public static InstalledStorePackageState LoadInstalledState()
    {
        EnsureDirectories();
        string path = GetInstalledPackagesPath();
        if (!File.Exists(path))
        {
            return new InstalledStorePackageState();
        }

        InstalledStorePackageState state =
            JsonStorage.Read<InstalledStorePackageState>(path) ?? new InstalledStorePackageState();
        state.Packages ??= [];
        foreach (InstalledStorePackage package in state.Packages)
        {
            package.InstalledFiles ??= [];
        }
        return state;
    }

    public static void SaveInstalledState(InstalledStorePackageState state)
    {
        EnsureDirectories();
        state.Packages = state.Packages
            .GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        JsonStorage.Write(GetInstalledPackagesPath(), state);
    }

    public static bool IsPackageInstalled(string packageId)
    {
        return LoadInstalledState().Packages.Any(package =>
            package.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<(bool Success, string Message)> InstallPackageAsync(StorePackageManifest package)
    {
        EnsureDirectories();

        if (!IsValidPackageId(package.Id))
        {
            return (false, "Invalid package identifier.");
        }

        if (!IsSupportedSurfOsVersion(package.MinimumSurfOSVersion))
        {
            return (false, $"Unsupported SurfOS version. Requires {package.MinimumSurfOSVersion} or newer.");
        }

        InstalledStorePackageState state = LoadInstalledState();
        package.Dependencies ??= [];
        List<string> missingDependencies = package.Dependencies
            .Where(dependency => state.Packages.All(installed =>
                !installed.Id.Equals(dependency, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (missingDependencies.Count > 0)
        {
            return (false, $"Missing dependencies: {string.Join(", ", missingDependencies)}");
        }

        string targetPath;
        string packageStorePath;
        try
        {
            targetPath = ResolveInstallPath(package.InstallPath, package.Id);
            packageStorePath = GetPackageStorePath(package);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or InvalidOperationException)
        {
            return (false, $"Invalid package path: {ex.Message}");
        }
        string tempPath = packageStorePath + ".download";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? GetAppsPath());
            Directory.CreateDirectory(Path.GetDirectoryName(packageStorePath) ?? GetAppsPath());
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            string downloadUrl = GetPackageDownloadUrl(package);
            if (!string.IsNullOrWhiteSpace(downloadUrl))
            {
                if (!IsValidSha256(package.Sha256))
                {
                    return (false, "Downloaded packages must declare a valid SHA-256 hash.");
                }
                await DownloadFileAsync(downloadUrl, tempPath);
                if (!ValidateSha256(tempPath, package.Sha256, out string actualHash))
                {
                    File.Delete(tempPath);
                    return (false, $"Invalid SHA-256. Expected {package.Sha256}, got {actualHash}.");
                }

                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                if (File.Exists(packageStorePath))
                {
                    File.Delete(packageStorePath);
                }

                File.Move(tempPath, packageStorePath);
                if (!packageStorePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(packageStorePath, targetPath, overwrite: true);
                }
            }
            else
            {
                File.WriteAllText(packageStorePath, JsonSerializer.Serialize(package, JsonOptions));
                if (!packageStorePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(packageStorePath, targetPath, overwrite: true);
                }
            }

            state.Packages.RemoveAll(installed =>
                installed.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase));
            state.Packages.Add(new InstalledStorePackage
            {
                Id = package.Id,
                Name = package.Name,
                Version = package.Version,
                Category = package.Category,
                Command = package.Command,
                AllowUserDataDelete = package.AllowUserDataDelete,
                InstalledAt = DateTime.Now,
                InstalledFiles = packageStorePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase)
                    ? [targetPath]
                    : [packageStorePath, targetPath]
            });
            SaveInstalledState(state);

            Log($"Installed {package.Id} {package.Version}.");
            KernelLog.Success("store", $"installed {package.Id}");
            return (true, $"Installed {package.Name} {package.Version}.");
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            KernelLog.Error("store", $"install failed for {package.Id}: {ex}");
            return (false, $"Failed download/install: {ex.Message}");
        }
    }

    public static (bool Success, string Message) RemovePackage(string packageId)
    {
        InstalledStorePackageState state = LoadInstalledState();
        InstalledStorePackage? package = state.Packages.FirstOrDefault(installed =>
            installed.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));

        if (package is null)
        {
            return (false, $"{packageId} is not installed.");
        }

        foreach (string file in package.InstalledFiles ?? [])
        {
            if (!IsAllowedPackagePath(file) || !File.Exists(file))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (false, $"Could not remove {package.Name}: {ex.Message}");
            }
        }

        state.Packages.Remove(package);
        SaveInstalledState(state);
        Log($"Removed {package.Id}.");
        KernelLog.Warning("store", $"removed {package.Id}");
        return (true, $"Removed {package.Name}.");
    }

    public static async Task DownloadFileAsync(string url, string targetPath)
    {
        if (!Import.Variables.cloudServicesEnabled || Import.Variables.safeMode)
        {
            throw new InvalidOperationException("SurfCloud downloads are disabled by the current system profile.");
        }

        if (!IsInternetAvailable())
        {
            throw new InvalidOperationException("Surf Store is offline. Showing installed apps only.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("SurfCloud downloads require an HTTPS URL.");
        }

        using HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(30),
            MaxResponseContentBufferSize = MaximumPackageBytes
        };
        byte[] bytes = await client.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(targetPath, bytes);
    }

    public static string DecodeCloudUrl(string encodedUrl)
    {
        if (string.IsNullOrWhiteSpace(encodedUrl))
        {
            return string.Empty;
        }

        byte[] bytes = Convert.FromBase64String(encodedUrl);
        byte[] key = System.Text.Encoding.UTF8.GetBytes(CloudUrlKey);
        for (int index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)(bytes[index] ^ key[index % key.Length]);
        }

        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static string GetPackageDownloadUrl(StorePackageManifest package)
    {
        if (!string.IsNullOrWhiteSpace(package.EncodedDownloadUrl))
        {
            return DecodeCloudUrl(package.EncodedDownloadUrl);
        }

        return package.DownloadUrl;
    }

    public static bool ValidateSha256(string path, string expectedHash, out string actualHash)
    {
        using FileStream stream = File.OpenRead(path);
        actualHash = Convert.ToHexString(SHA256.HashData(stream));
        return IsValidSha256(expectedHash) &&
               actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetAppsPath()
    {
        return Path.Combine(GetRootPath(), "apps");
    }

    public static string GetInstalledPackagesPath()
    {
        return Path.Combine(GetAppsPath(), "installed_packages.json");
    }

    private static CloudManifestResult LoadOfflineManifest(string message)
    {
        StoreManifest? cached = ReadManifest(GetCachedManifestPath());
        if (cached is not null)
        {
            return new CloudManifestResult
            {
                Manifest = MergeWithSeedManifest(cached),
                IsOffline = true,
                Message = message
            };
        }

        foreach (string seedPath in GetSeedManifestPaths())
        {
            StoreManifest? seeded = ReadManifest(seedPath);
            if (seeded is not null)
            {
                return new CloudManifestResult
                {
                    Manifest = seeded,
                    IsOffline = true,
                    Message = message
                };
            }
        }

        return new CloudManifestResult
        {
            Manifest = MergeWithSeedManifest(new StoreManifest()),
            IsOffline = true,
            Message = message
        };
    }

    private static StoreManifest? ReadManifest(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaximumManifestBytes)
            {
                return null;
            }
            StoreManifest? manifest = JsonSerializer.Deserialize<StoreManifest>(File.ReadAllText(path), JsonOptions);
            if (manifest is not null)
            {
                NormalizeManifest(manifest);
            }
            return manifest;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveInstallPath(string installPath, string packageId)
    {
        string relativePath = string.IsNullOrWhiteSpace(installPath)
            ? Path.Combine("apps", packageId, $"{packageId}.pkg")
            : installPath.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Package install path must be relative.");
        }

        string firstSegment = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        bool installsApp = firstSegment.Equals("apps", StringComparison.OrdinalIgnoreCase);
        bool installsTheme = firstSegment.Equals("Packages", StringComparison.OrdinalIgnoreCase);
        if (!installsApp && !installsTheme)
        {
            throw new InvalidOperationException("Packages may only install under apps or Packages.");
        }
        string fullPath = Path.GetFullPath(Path.Combine(GetRootPath(), relativePath));
        string root = Path.GetFullPath(GetRootPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string allowedRoot = installsApp ? GetAppsPath() : Path.Combine(GetRootPath(), "Packages");

        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !PathSafety.IsInsideRoot(fullPath, allowedRoot) ||
            Path.GetFullPath(fullPath).Equals(Path.GetFullPath(allowedRoot), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Package install path escapes its allowed package folder.");
        }

        return fullPath;
    }

    private static string GetPackageStorePath(StorePackageManifest package)
    {
        string fileName = Path.GetFileName(
            string.IsNullOrWhiteSpace(package.InstallPath)
                ? $"{package.Id}.pkg"
                : package.InstallPath.Replace('/', Path.DirectorySeparatorChar));

        string packageDirectory = Path.Combine(GetAppsPath(), "packages", package.Id);
        string fullPath = Path.GetFullPath(Path.Combine(packageDirectory, fileName));
        if (!PathSafety.IsInsideRoot(fullPath, GetAppsPath()))
        {
            throw new InvalidOperationException("Package storage path escapes the apps folder.");
        }
        return fullPath;
    }

    private static bool IsValidPackageId(string packageId) =>
        !string.IsNullOrWhiteSpace(packageId) &&
        packageId is not "." and not ".." &&
        packageId.Length <= 80 &&
        packageId.All(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool IsValidSha256(string hash) =>
        hash.Length == 64 && hash.All(Uri.IsHexDigit);

    private static bool IsAllowedPackagePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            return PathSafety.IsInsideRoot(fullPath, GetAppsPath()) ||
                   PathSafety.IsInsideRoot(fullPath, Path.Combine(GetRootPath(), "Packages"));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsSupportedSurfOsVersion(string minimumVersion)
    {
        if (string.IsNullOrWhiteSpace(minimumVersion) ||
            !Version.TryParse(minimumVersion, out Version? required) ||
            !Version.TryParse(Import.Variables.version, out Version? current))
        {
            return true;
        }

        return current >= required;
    }

    private static void EnsureDirectories()
    {
        Directory.CreateDirectory(GetAppsPath());
        Directory.CreateDirectory(Path.Combine(GetAppsPath(), "cache"));
        Directory.CreateDirectory(Path.Combine(GetRootPath(), "logs"));
    }

    private static string GetCachedManifestPath()
    {
        return Path.Combine(GetAppsPath(), "cache", "packages.json");
    }

    private static StoreManifest MergeWithSeedManifest(StoreManifest manifest)
    {
        StoreManifest merged = new()
        {
            Repository = manifest.Repository,
            Version = manifest.Version,
            Packages = manifest.Packages.ToList()
        };

        foreach (StorePackageManifest package in GetBuiltInSurfCloudPackages())
        {
            AddPackageIfMissing(merged, package);
        }

        foreach (string seedPath in GetSeedManifestPaths())
        {
            StoreManifest? seeded = ReadManifest(seedPath);
            if (seeded is null)
            {
                continue;
            }

            foreach (StorePackageManifest seedPackage in seeded.Packages)
            {
                AddPackageIfMissing(merged, seedPackage);
            }
        }

        return merged;
    }

    private static void AddPackageIfMissing(StoreManifest manifest, StorePackageManifest package)
    {
        if (manifest.Packages.Any(existing =>
                existing.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        manifest.Packages.Add(package);
    }

    private static void NormalizeManifest(StoreManifest manifest)
    {
        manifest.Packages ??= [];
        manifest.Packages.RemoveAll(package => package is null);
        foreach (StorePackageManifest package in manifest.Packages)
        {
            package.Dependencies ??= [];
        }
    }

    private static IEnumerable<StorePackageManifest> GetBuiltInSurfCloudPackages()
    {
        yield return new StorePackageManifest
        {
            Id = "surfcode-ide",
            Name = "SurfCode IDE",
            Version = "1.0.0",
            Author = "SurfOS Core",
            Description = "A CLI-native project workspace and C# editor for SurfOS.",
            Category = "Developer Tools",
            InstallPath = "apps/surfcode-ide/surfcode-ide.pkg",
            MinimumSurfOSVersion = "2.0.0",
            AllowUserDataDelete = false,
            Command = "code"
        };
    }

    private static IEnumerable<string> GetSeedManifestPaths()
    {
        yield return Path.Combine(GetRootPath(), "surfcloud-seed", "packages.json");
        yield return Path.Combine(Environment.CurrentDirectory, "surfcloud-seed", "packages.json");
    }

    private static string GetRootPath()
    {
        return string.IsNullOrWhiteSpace(Import.Variables.installPath)
            ? Environment.CurrentDirectory
            : Import.Variables.installPath;
    }

    private static void Log(string message)
    {
        try
        {
            string logPath = Path.Combine(GetRootPath(), "logs", "store.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? GetRootPath());
            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // Store logging is best-effort.
        }
    }
}
