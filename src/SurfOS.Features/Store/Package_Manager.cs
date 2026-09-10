namespace SurfOS2;

internal static class Package_Manager
{
    public static void Initialize()
    {
        if (CloudRepositoryManager.LoadInstalledState().Packages.Count == 0)
        {
            return;
        }

        try
        {
            CloudManifestResult result = CloudRepositoryManager.LoadRemotePackagesAsync(forceRefresh: true)
                .GetAwaiter()
                .GetResult();
            foreach (PackageUpdate update in CloudRepositoryManager.GetNewUpdateNotifications(result.Manifest))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(
                    $"[SurfCloud] Update available: {update.Name} {update.InstalledVersion} -> {update.AvailableVersion}");
                Console.ResetColor();
                Console.WriteLine($"            Run: surf install {update.Id}");
            }
        }
        catch (Exception ex)
        {
            KernelLog.Warning("store", $"startup update check failed: {ex.Message}");
        }
    }

    public static void ManageSurfCommand(string[] args, int cmdIndex)
    {
        if (args.Skip(cmdIndex + 1).Any(argument => argument is "-h" or "--help"))
        {
            PrintUsage();
            return;
        }

        if (args.Length > cmdIndex + 1 &&
            args[cmdIndex + 1].Equals("store", StringComparison.OrdinalIgnoreCase))
        {
            SurfStore.Open();
            return;
        }

        if (args.Length <= cmdIndex + 1)
        {
            PrintUsage();
            return;
        }

        string action = args[cmdIndex + 1].ToLowerInvariant();
        switch (action)
        {
            case "search":
                Search(args.Length > cmdIndex + 2 ? args[cmdIndex + 2] : string.Empty);
                break;

            case "install":
                if (args.Length <= cmdIndex + 2)
                {
                    Console.WriteLine("Usage: surf install <package>");
                    return;
                }
                Install(args[cmdIndex + 2]);
                break;

            case "remove":
                if (args.Length <= cmdIndex + 2)
                {
                    Console.WriteLine("Usage: surf remove <package>");
                    return;
                }
                Remove(args[cmdIndex + 2]);
                break;

            case "list":
                ListInstalled();
                break;

            case "update":
                Update();
                break;

            case "updates":
                ListUpdates();
                break;

            case "info":
                if (args.Length <= cmdIndex + 2)
                {
                    Console.WriteLine("Usage: surf info <package>");
                    return;
                }
                ShowInfo(args[cmdIndex + 2]);
                break;

            default:
                PrintUsage();
                break;
        }
    }

    private static void Search(string query)
    {
        CloudManifestResult result = CloudRepositoryManager.LoadRemotePackagesAsync(forceRefresh: true)
            .GetAwaiter()
            .GetResult();
        if (result.IsOffline)
        {
            Console.WriteLine(result.Message);
        }

        IEnumerable<StorePackageManifest> packages = result.Manifest.Packages;
        if (!string.IsNullOrWhiteSpace(query))
        {
            packages = packages.Where(package =>
                package.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                package.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                package.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                package.Category.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        PrintPackageTable(packages.ToList(), "Available packages");
    }

    private static void Install(string packageId)
    {
        StorePackageManifest? package = FindRemotePackage(packageId);
        if (package is null)
        {
            Console.WriteLine($"Package '{packageId}' was not found.");
            return;
        }

        (bool success, string message) = CloudRepositoryManager.InstallPackageAsync(package)
            .GetAwaiter()
            .GetResult();
        Console.WriteLine(message);
    }

    private static void Remove(string packageId)
    {
        (bool success, string message) = CloudRepositoryManager.RemovePackage(packageId);
        Console.WriteLine(message);
    }

    private static void ListInstalled()
    {
        InstalledStorePackageState state = CloudRepositoryManager.LoadInstalledState();
        Console.WriteLine("\n--- Installed packages ---");
        if (state.Packages.Count == 0)
        {
            Console.WriteLine("No packages installed.");
            return;
        }

        foreach (InstalledStorePackage package in state.Packages)
        {
            Console.WriteLine($"{package.Id,-18} {package.Version,-8} {package.Category} - {package.Name}");
        }
    }

    private static void Update()
    {
        CloudManifestResult result = CloudRepositoryManager.LoadRemotePackagesAsync(forceRefresh: true)
            .GetAwaiter()
            .GetResult();
        Console.WriteLine(result.IsOffline
            ? result.Message
            : $"Cloud package manifest refreshed. {result.Manifest.Packages.Count} package(s) available.");
        PrintUpdates(CloudRepositoryManager.GetAvailableUpdates(result.Manifest));
    }

    private static void ListUpdates()
    {
        CloudManifestResult result = CloudRepositoryManager.LoadRemotePackagesAsync(forceRefresh: true)
            .GetAwaiter()
            .GetResult();
        if (result.IsOffline) Console.WriteLine(result.Message);
        PrintUpdates(CloudRepositoryManager.GetAvailableUpdates(result.Manifest));
    }

    private static void PrintUpdates(IReadOnlyList<PackageUpdate> updates)
    {
        if (updates.Count == 0)
        {
            Console.WriteLine("All installed apps are up to date.");
            return;
        }

        Console.WriteLine("\n--- App updates ---");
        foreach (PackageUpdate update in updates)
        {
            Console.WriteLine(
                $"{update.Id,-18} {update.InstalledVersion,-10} -> {update.AvailableVersion,-10} surf install {update.Id}");
        }
    }

    private static void ShowInfo(string packageId)
    {
        StorePackageManifest? package = FindRemotePackage(packageId);
        if (package is null)
        {
            Console.WriteLine($"Package '{packageId}' was not found.");
            return;
        }

        InstalledStorePackage? installed = CloudRepositoryManager.LoadInstalledState()
            .Packages
            .FirstOrDefault(item => item.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"\nPackage        : {package.Id}");
        Console.WriteLine($"Name           : {package.Name}");
        Console.WriteLine($"Version        : {package.Version}");
        Console.WriteLine($"Author         : {package.Author}");
        Console.WriteLine($"Category       : {package.Category}");
        Console.WriteLine($"Installed      : {(installed is null ? "no" : $"yes ({installed.Version})")}");
        Console.WriteLine($"Install path   : {package.InstallPath}");
        Console.WriteLine($"Minimum SurfOS : {package.MinimumSurfOSVersion}");
        Console.WriteLine($"Dependencies   : {string.Join(", ", package.Dependencies)}");
        Console.WriteLine($"SHA-256        : {package.Sha256}");
        Console.WriteLine($"Install command: surf install {package.Id}");
        if (TryGetThemeCommand(package, out string themeCommand))
        {
            Console.WriteLine($"Theme command  : {themeCommand}");
        }
        if (installed is not null)
        {
            Console.WriteLine($"Remove command : surf remove {package.Id}");
        }
        Console.WriteLine($"Description    : {package.Description}");
    }

    private static bool TryGetThemeCommand(StorePackageManifest package, out string command)
    {
        command = string.Empty;
        if (!package.InstallPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) &&
            !package.InstallPath.StartsWith("Packages\\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string themeName = Path.GetFileNameWithoutExtension(package.InstallPath);
        if (string.IsNullOrWhiteSpace(themeName))
        {
            return false;
        }

        command = $"theme load {themeName}";
        return true;
    }

    private static StorePackageManifest? FindRemotePackage(string packageId)
    {
        CloudManifestResult result = CloudRepositoryManager.LoadRemotePackagesAsync(forceRefresh: true)
            .GetAwaiter()
            .GetResult();
        if (result.IsOffline)
        {
            Console.WriteLine(result.Message);
        }

        return result.Manifest.Packages.FirstOrDefault(package =>
            package.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase) ||
            package.Name.Equals(packageId, StringComparison.OrdinalIgnoreCase));
    }

    private static void PrintPackageTable(IReadOnlyList<StorePackageManifest> packages, string title)
    {
        Console.WriteLine($"\n--- {title} ---");
        if (packages.Count == 0)
        {
            Console.WriteLine("No packages found.");
            return;
        }

        HashSet<string> installed = CloudRepositoryManager.LoadInstalledState()
            .Packages
            .Select(package => package.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (StorePackageManifest package in packages)
        {
            string marker = installed.Contains(package.Id) ? "installed" : "available";
            Console.WriteLine($"{package.Id,-18} {package.Version,-8} {marker,-10} {package.Category,-15} {package.Name}");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: surf <command> [arguments]");
        Console.WriteLine("  surf store");
        Console.WriteLine("  surf search [name]");
        Console.WriteLine("  surf install <package>");
        Console.WriteLine("  surf remove <package>");
        Console.WriteLine("  surf list");
        Console.WriteLine("  surf update");
        Console.WriteLine("  surf updates");
        Console.WriteLine("  surf info <package>");
    }
}
