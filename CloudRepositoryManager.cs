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
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Utilities";
    public string DownloadUrl { get; set; } = string.Empty;
    public string EncodedDownloadUrl { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public List<string> Dependencies { get; set; } = [];
    public bool DesktopEnabled { get; set; }
    public string DesktopIcon { get; set; } = string.Empty;
    public string DesktopTitle { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string MinimumSurfOSVersion { get; set; } = "2.0.0";
    public bool AllowUserDataDelete { get; set; }
    public string Command { get; set; } = string.Empty;
}

internal sealed class InstalledStorePackage
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool DesktopEnabled { get; set; }
    public string DesktopIcon { get; set; } = string.Empty;
    public string DesktopTitle { get; set; } = string.Empty;
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
    public const string SurfOsVersion = "2.0.0";

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

        if (!forceRefresh && File.Exists(GetCachedManifestPath()))
        {
            StoreManifest? cached = ReadManifest(GetCachedManifestPath());
            if (cached is not null)
            {
                return new CloudManifestResult { Manifest = cached };
            }
        }

        if (!IsInternetAvailable())
        {
            return LoadOfflineManifest("Surf Store is offline. Showing installed apps only.");
        }

        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(12) };
            string json = await client.GetStringAsync(DecodeCloudUrl(EncodedPackagesManifestUrl));
            StoreManifest? manifest = JsonSerializer.Deserialize<StoreManifest>(json, JsonOptions);
            if (manifest is null || manifest.Packages.Count == 0)
            {
                return LoadOfflineManifest("Invalid cloud manifest. Showing installed apps only.");
            }

            File.WriteAllText(GetCachedManifestPath(), JsonSerializer.Serialize(manifest, JsonOptions));
            Log("Cloud package manifest refreshed.");
            KernelLog.Success("store", $"loaded {manifest.Packages.Count} cloud package(s)");
            return new CloudManifestResult { Manifest = manifest };
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

        return JsonStorage.Read<InstalledStorePackageState>(path) ?? new InstalledStorePackageState();
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

    public static async Task<(bool Success, string Message)> InstallPackageAsync(StorePackageManifest package)
    {
        EnsureDirectories();

        if (!IsSupportedSurfOsVersion(package.MinimumSurfOSVersion))
        {
            return (false, $"Unsupported SurfOS version. Requires {package.MinimumSurfOSVersion} or newer.");
        }

        InstalledStorePackageState state = LoadInstalledState();
        List<string> missingDependencies = package.Dependencies
            .Where(dependency => state.Packages.All(installed =>
                !installed.Id.Equals(dependency, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (missingDependencies.Count > 0)
        {
            return (false, $"Missing dependencies: {string.Join(", ", missingDependencies)}");
        }

        string targetPath = ResolveInstallPath(package.InstallPath, package.Id);
        string packageStorePath = GetPackageStorePath(package);
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
                DesktopEnabled = package.DesktopEnabled,
                DesktopIcon = package.DesktopIcon,
                DesktopTitle = string.IsNullOrWhiteSpace(package.DesktopTitle)
                    ? package.Name
                    : package.DesktopTitle,
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

        foreach (string file in package.InstalledFiles)
        {
            if (!File.Exists(file))
            {
                continue;
            }

            File.Delete(file);
        }

        state.Packages.Remove(package);
        SaveInstalledState(state);
        Log($"Removed {package.Id}.");
        KernelLog.Warning("store", $"removed {package.Id}");
        return (true, $"Removed {package.Name}.");
    }

    public static async Task DownloadFileAsync(string url, string targetPath)
    {
        if (!IsInternetAvailable())
        {
            throw new InvalidOperationException("Surf Store is offline. Showing installed apps only.");
        }

        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
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
        return string.IsNullOrWhiteSpace(expectedHash) ||
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
                Manifest = cached,
                IsOffline = true,
                Message = message
            };
        }

        return new CloudManifestResult
        {
            Manifest = new StoreManifest(),
            IsOffline = true,
            Message = message
        };
    }

    private static StoreManifest? ReadManifest(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<StoreManifest>(File.ReadAllText(path), JsonOptions);
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
        string fullPath = Path.GetFullPath(Path.Combine(GetRootPath(), relativePath));
        string root = Path.GetFullPath(GetRootPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Package install path escapes SurfOS root.");
        }

        return fullPath;
    }

    private static string GetPackageStorePath(StorePackageManifest package)
    {
        string fileName = Path.GetFileName(
            string.IsNullOrWhiteSpace(package.InstallPath)
                ? $"{package.Id}.pkg"
                : package.InstallPath.Replace('/', Path.DirectorySeparatorChar));

        return Path.Combine(GetAppsPath(), "packages", package.Id, fileName);
    }

    private static bool IsSupportedSurfOsVersion(string minimumVersion)
    {
        if (string.IsNullOrWhiteSpace(minimumVersion) ||
            !Version.TryParse(minimumVersion, out Version? required) ||
            !Version.TryParse(SurfOsVersion, out Version? current))
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
