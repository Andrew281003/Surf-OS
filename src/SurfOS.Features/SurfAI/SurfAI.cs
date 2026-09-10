using System.Net.NetworkInformation;
using System.Text.Json;

namespace SurfOS2;

internal static class SurfAI
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void HandleCommand(string[] args, int cmdIndex)
    {
        string prompt = string.Join(' ', args.Skip(cmdIndex + 1));
        if (string.IsNullOrWhiteSpace(prompt))
        {
            ShowIntro();
            return;
        }

        Answer(prompt);
    }

    private static void ShowIntro()
    {
        Console.WriteLine("\nSurfAI local assistant");
        Console.WriteLine("No AI API is connected yet. Answers use local SurfOS data only.");
        Console.WriteLine("\nTry:");
        Console.WriteLine("  surfai packages");
        Console.WriteLine("  surfai themes");
        Console.WriteLine("  surfai install neon-tide");
        Console.WriteLine("  surfai online");
        Console.WriteLine("  surfai explain Failed download/install: Surf Store is offline");
        Console.WriteLine("  surfai surfcloud public link");
    }

    private static void Answer(string prompt)
    {
        string normalized = prompt.ToLowerInvariant();

        if (ContainsAny(normalized, "online", "offline", "internet", "network", "connection"))
        {
            ExplainCloudStatus();
            return;
        }

        if (ContainsAny(normalized, "theme", "themes"))
        {
            ListCloudThemes();
            return;
        }

        if (ContainsAny(normalized, "package", "packages", "available", "list", "search"))
        {
            ListCloudPackages();
            return;
        }

        if (ContainsAny(normalized, "install", "download", "add app", "get app"))
        {
            ExplainPackageInstall(prompt);
            return;
        }

        if (ContainsAny(normalized, "update", "refresh manifest", "manifest"))
        {
            ExplainUpdateError(prompt);
            return;
        }

        if (ContainsAny(normalized, "surfcloud", "public link", "sharing link", "permission"))
        {
            ExplainSurfCloudPublicLinks();
            return;
        }

        if (ContainsAny(normalized, "error", "failed", "sha", "dependency", "unsupported", "not found"))
        {
            ExplainCloudError(prompt);
            return;
        }

        ShowIntro();
    }

    private static void ExplainCloudStatus()
    {
        bool online = IsNetworkAdapterOnline();
        LocalManifest manifest = LoadLocalManifest();

        Console.WriteLine(online
            ? "SurfOS appears online: an active non-loopback network adapter is available."
            : "SurfOS appears offline: no active non-loopback network adapter was detected.");

        if (manifest.Manifest.Packages.Count > 0)
        {
            Console.WriteLine($"Local cloud package data is available from {manifest.Source}.");
            Console.WriteLine($"SurfAI can list {manifest.Manifest.Packages.Count} cached/bundled package(s) without calling the cloud.");
        }
        else
        {
            Console.WriteLine("No local cloud package manifest was found yet.");
        }
    }

    private static void ListCloudPackages()
    {
        LocalManifest manifest = LoadLocalManifest();
        Console.WriteLine("\nAvailable cloud packages from local SurfOS data:");
        Console.WriteLine($"Source: {manifest.Source}");

        if (manifest.Manifest.Packages.Count == 0)
        {
            Console.WriteLine("No local package data found. Run 'surf update' when online to cache the cloud manifest.");
            return;
        }

        PrintPackageRows(manifest.Manifest.Packages);
        Console.WriteLine("\nInstall with: surf install <package-id>");
        Console.WriteLine("Details with: surf info <package-id>");
    }

    private static void ListCloudThemes()
    {
        LocalManifest manifest = LoadLocalManifest();
        List<StorePackageManifest> themes = manifest.Manifest.Packages
            .Where(IsThemePackage)
            .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine("\nAvailable cloud themes from local SurfOS data:");
        Console.WriteLine($"Source: {manifest.Source}");

        if (themes.Count == 0)
        {
            Console.WriteLine("No theme packages found in local package data.");
            return;
        }

        PrintPackageRows(themes);
        Console.WriteLine("\nInstall with: surf install <theme-id>");
        Console.WriteLine("Load after install with: theme load <theme-name>");
    }

    private static void ExplainPackageInstall(string prompt)
    {
        LocalManifest manifest = LoadLocalManifest();
        StorePackageManifest? package = FindMentionedPackage(prompt, manifest.Manifest.Packages);

        if (package is null)
        {
            Console.WriteLine("To install a SurfOS cloud package, use:");
            Console.WriteLine("  surf install <package-id>");
            Console.WriteLine("\nUse 'surfai packages' to see package IDs from local data.");
            return;
        }

        Console.WriteLine($"Package: {package.Id} ({package.Name})");
        Console.WriteLine($"Install command: surf install {package.Id}");
        if (IsThemePackage(package))
        {
            string themeName = Path.GetFileNameWithoutExtension(package.InstallPath);
            Console.WriteLine($"Theme load command after install: theme load {themeName}");
        }

        if (!IsSupportedSurfOsVersion(package.MinimumSurfOSVersion))
        {
            Console.WriteLine($"Warning: this package requires SurfOS {package.MinimumSurfOSVersion} or newer.");
        }

        if (package.Dependencies.Count > 0)
        {
            Console.WriteLine($"Dependencies: {string.Join(", ", package.Dependencies)}");
        }
    }

    private static void ExplainCloudError(string prompt)
    {
        string normalized = prompt.ToLowerInvariant();

        if (normalized.Contains("offline") || normalized.Contains("internet"))
        {
            Console.WriteLine("That error means SurfOS could not reach the cloud. Check 'surfai online', then try 'surf update' again when connected.");
        }
        else if (normalized.Contains("sha") || normalized.Contains("hash"))
        {
            Console.WriteLine("That error means the downloaded file did not match the package SHA-256 checksum. Do not force install it; refresh the manifest or fix the package file/link.");
        }
        else if (normalized.Contains("depend"))
        {
            Console.WriteLine("That error means another package must be installed first. Run 'surf info <package-id>' to see dependencies, then install the missing package IDs.");
        }
        else if (normalized.Contains("unsupported") || normalized.Contains("version"))
        {
            Console.WriteLine($"That error means the package requires a newer SurfOS version. This build reports SurfOS {Import.Variables.version}.");
        }
        else if (normalized.Contains("not found"))
        {
            Console.WriteLine("That error means the package ID was not found in the local/cached cloud manifest. Run 'surfai packages' and use the exact package ID.");
        }
        else if (normalized.Contains("escapes") || normalized.Contains("path"))
        {
            Console.WriteLine("That error means the package install path points outside the SurfOS install folder. Fix the manifest installPath before installing.");
        }
        else
        {
            Console.WriteLine("For cloud download errors, check the exact message:");
            Console.WriteLine("  offline/internet: network unavailable");
            Console.WriteLine("  Invalid SHA-256: package file changed or wrong hash");
            Console.WriteLine("  Missing dependencies: install required packages first");
            Console.WriteLine("  Unsupported SurfOS version: package needs a newer SurfOS");
            Console.WriteLine("  not found: package ID is not in local manifest");
        }
    }

    private static void ExplainUpdateError(string prompt)
    {
        string normalized = prompt.ToLowerInvariant();
        if (normalized.Contains("invalid"))
        {
            Console.WriteLine("An invalid cloud manifest usually means the SurfCloud link did not return package JSON. Check that the file is public and uses a direct download URL.");
            return;
        }

        if (normalized.Contains("offline") || normalized.Contains("failed") || normalized.Contains("error"))
        {
            Console.WriteLine("Update failed because SurfOS could not refresh the cloud manifest. SurfAI can still read cached or bundled local package data.");
            Console.WriteLine("Check: surfai online");
            Console.WriteLine("Retry: surf update");
            return;
        }

        Console.WriteLine("Surf package updates refresh the local cloud manifest cache.");
        Console.WriteLine("Use 'surf update' to refresh, then 'surfai packages' to inspect the local result.");
    }

    private static void ExplainSurfCloudPublicLinks()
    {
        Console.WriteLine("For SurfOS cloud packages hosted on SurfCloud:");
        Console.WriteLine("1. Make the SurfCloud package file readable by SurfOS.");
        Console.WriteLine("2. Use a SurfCloud direct download endpoint, not a browser preview page.");
        Console.WriteLine("3. The package manifest URL must return raw JSON.");
        Console.WriteLine("4. If SurfOS says 'Invalid cloud manifest', the link probably points to a preview page or private file.");
        Console.WriteLine("5. If SHA-256 fails, update the manifest hash after replacing the SurfCloud file.");
    }

    private static void PrintPackageRows(IReadOnlyList<StorePackageManifest> packages)
    {
        HashSet<string> installed = CloudRepositoryManager.LoadInstalledState()
            .Packages
            .Select(package => package.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (StorePackageManifest package in packages.OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase))
        {
            string marker = installed.Contains(package.Id) ? "installed" : "available";
            Console.WriteLine($"{package.Id,-18} {package.Version,-8} {marker,-10} {package.Category,-12} {package.Name}");
        }
    }

    private static StorePackageManifest? FindMentionedPackage(string prompt, IReadOnlyList<StorePackageManifest> packages)
    {
        return packages.FirstOrDefault(package =>
            prompt.Contains(package.Id, StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains(package.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsThemePackage(StorePackageManifest package)
    {
        return package.Category.Equals("Themes", StringComparison.OrdinalIgnoreCase) ||
               package.InstallPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ||
               package.InstallPath.StartsWith("Packages\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(value.Contains);
    }

    private static bool IsNetworkAdapterOnline()
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

    private static LocalManifest LoadLocalManifest()
    {
        foreach ((string path, string source) in GetLocalManifestCandidates())
        {
            StoreManifest? manifest = ReadManifest(path);
            if (manifest is not null)
            {
                return new LocalManifest(manifest, source);
            }
        }

        return new LocalManifest(new StoreManifest(), "none");
    }

    private static IEnumerable<(string Path, string Source)> GetLocalManifestCandidates()
    {
        string root = GetRootPath();
        yield return (Path.Combine(root, "apps", "cache", "packages.json"), "cached Surf Store manifest");
        yield return (Path.Combine(root, "surfcloud-seed", "packages.json"), "bundled SurfCloud seed packages");
        yield return (Path.Combine(Environment.CurrentDirectory, "surfcloud-seed", "packages.json"), "workspace SurfCloud seed packages");
    }

    private static StoreManifest? ReadManifest(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<StoreManifest>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string GetRootPath()
    {
        return string.IsNullOrWhiteSpace(Import.Variables.installPath)
            ? Environment.CurrentDirectory
            : Import.Variables.installPath;
    }

    private sealed record LocalManifest(StoreManifest Manifest, string Source);
}
