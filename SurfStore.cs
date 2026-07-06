namespace SurfOS2;

internal static class SurfStore
{
    private static readonly string[] Categories =
    [
        "Productivity",
        "Games",
        "Themes",
        "Developer Tools",
        "Utilities"
    ];

    public static void Open()
    {
        OpenAsync().GetAwaiter().GetResult();
    }

    private static async Task OpenAsync()
    {
        CloudManifestResult manifestResult =
            await CloudRepositoryManager.LoadRemotePackagesAsync(forceRefresh: true);
        int selectedIndex = 0;
        int selectedCategory = 0;
        string statusMessage = manifestResult.IsOffline
            ? manifestResult.Message
            : "Surf Store online. Press R to refresh.";

        while (true)
        {
            List<StorePackageManifest> visiblePackages = GetVisiblePackages(
                manifestResult.Manifest,
                selectedCategory,
                manifestResult.IsOffline);
            if (selectedIndex >= visiblePackages.Count)
            {
                selectedIndex = Math.Max(0, visiblePackages.Count - 1);
            }

            DrawStore(manifestResult, selectedCategory, selectedIndex, visiblePackages, statusMessage);
            ConsoleKey key = Console.ReadKey(intercept: true).Key;

            switch (key)
            {
                case ConsoleKey.UpArrow:
                    selectedIndex = selectedIndex == 0
                        ? Math.Max(0, visiblePackages.Count - 1)
                        : selectedIndex - 1;
                    break;

                case ConsoleKey.DownArrow:
                    selectedIndex = visiblePackages.Count == 0
                        ? 0
                        : (selectedIndex + 1) % visiblePackages.Count;
                    break;

                case ConsoleKey.LeftArrow:
                    selectedCategory = selectedCategory == 0
                        ? Categories.Length - 1
                        : selectedCategory - 1;
                    selectedIndex = 0;
                    break;

                case ConsoleKey.RightArrow:
                    selectedCategory = (selectedCategory + 1) % Categories.Length;
                    selectedIndex = 0;
                    break;

                case ConsoleKey.R:
                    statusMessage = "Refreshing Surf Store cloud manifest...";
                    DrawStore(manifestResult, selectedCategory, selectedIndex, visiblePackages, statusMessage);
                    manifestResult = await CloudRepositoryManager.LoadRemotePackagesAsync(forceRefresh: true);
                    statusMessage = manifestResult.IsOffline
                        ? manifestResult.Message
                        : $"Refreshed {manifestResult.Manifest.Packages.Count} package(s).";
                    break;

                case ConsoleKey.Enter:
                    if (visiblePackages.Count > 0)
                    {
                        statusMessage = await ShowPackageDetailsAsync(visiblePackages[selectedIndex], manifestResult.Manifest);
                    }
                    break;

                case ConsoleKey.Q:
                case ConsoleKey.Escape:
                    Console.Clear();
                    Screen_Print.Print_Selected_Package();
                    return;
            }
        }
    }

    private static List<StorePackageManifest> GetVisiblePackages(
        StoreManifest manifest,
        int selectedCategory,
        bool offline)
    {
        InstalledStorePackageState installed = CloudRepositoryManager.LoadInstalledState();
        if (offline && manifest.Packages.Count == 0)
        {
            return installed.Packages
                .Where(package => package.Category.Equals(Categories[selectedCategory], StringComparison.OrdinalIgnoreCase))
                .Select(ToManifest)
                .ToList();
        }

        return manifest.Packages
            .Where(package => package.Category.Equals(Categories[selectedCategory], StringComparison.OrdinalIgnoreCase))
            .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void DrawStore(
        CloudManifestResult manifestResult,
        int selectedCategory,
        int selectedIndex,
        IReadOnlyList<StorePackageManifest> packages,
        string statusMessage)
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("============================================================");
        Console.WriteLine("                      SURF STORE                            ");
        Console.WriteLine("============================================================");
        Console.ResetColor();
        Console.WriteLine("Up/Down: packages  Left/Right: categories  Enter: details");
        Console.WriteLine("R: refresh cloud manifest  Q/Esc: exit\n");

        for (int index = 0; index < Categories.Length; index++)
        {
            bool selected = index == selectedCategory;
            Console.ForegroundColor = selected ? ConsoleColor.Black : ConsoleColor.Gray;
            Console.BackgroundColor = selected ? ConsoleColor.Cyan : ConsoleColor.Black;
            Console.Write($" {Categories[index]} ");
            Console.ResetColor();
            Console.Write(" ");
        }

        Console.WriteLine("\n");
        if (manifestResult.IsOffline)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Surf Store is offline. Showing installed apps only.");
            Console.ResetColor();
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(statusMessage);
        Console.ResetColor();
        Console.WriteLine();

        if (packages.Count == 0)
        {
            Console.WriteLine("No packages in this category.");
            return;
        }

        for (int index = 0; index < packages.Count; index++)
        {
            StorePackageManifest package = packages[index];
            string state = GetInstallStatus(package, manifestResult.Manifest);
            bool selected = index == selectedIndex;

            Console.ForegroundColor = selected ? ConsoleColor.Black : GetStatusColor(state);
            Console.BackgroundColor = selected ? ConsoleColor.Gray : ConsoleColor.Black;
            Console.WriteLine(
                $"{(selected ? ">" : " ")} {package.Name,-24} {package.Version,-8} {state,-18} {package.Author}");
            Console.ResetColor();
        }
    }

    private static async Task<string> ShowPackageDetailsAsync(
        StorePackageManifest package,
        StoreManifest manifest)
    {
        while (true)
        {
            InstalledStorePackage? installed = FindInstalled(package.Id);
            bool updateAvailable = installed is not null &&
                                   CompareVersions(package.Version, installed.Version) > 0;

            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("============================================================");
            Console.WriteLine($" {package.Name}");
            Console.WriteLine("============================================================");
            Console.ResetColor();
            Console.WriteLine($"ID             : {package.Id}");
            Console.WriteLine($"Version        : {package.Version}");
            Console.WriteLine($"Author         : {package.Author}");
            Console.WriteLine($"Category       : {package.Category}");
            Console.WriteLine($"Installed      : {(installed is null ? "no" : $"yes ({installed.Version})")}");
            Console.WriteLine($"Update         : {(updateAvailable ? "available" : "none")}");
            Console.WriteLine($"Desktop        : {(package.DesktopEnabled ? package.DesktopTitle : "no")}");
            Console.WriteLine($"Minimum SurfOS : {package.MinimumSurfOSVersion}");
            Console.WriteLine($"Dependencies   : {FormatDependencies(package.Dependencies)}");
            Console.WriteLine($"SHA-256        : {(string.IsNullOrWhiteSpace(package.Sha256) ? "not provided" : package.Sha256)}");
            Console.WriteLine($"Install command: surf install {package.Id}");
            if (TryGetThemeCommand(package, out string themeCommand))
            {
                Console.WriteLine($"Theme command  : {themeCommand}");
            }
            if (installed is not null)
            {
                Console.WriteLine($"Remove command : surf remove {package.Id}");
            }
            Console.WriteLine();
            Console.WriteLine(package.Description);
            Console.WriteLine();

            if (installed is null)
            {
                Console.WriteLine("Enter: install  Esc: back");
            }
            else
            {
                Console.WriteLine(updateAvailable
                    ? "Enter: update  Delete/R: remove  Esc: back"
                    : "Delete/R: remove  Esc: back");
            }

            ConsoleKey key = Console.ReadKey(intercept: true).Key;
            if (key == ConsoleKey.Escape)
            {
                return "Returned from package details.";
            }

            if (key == ConsoleKey.Enter)
            {
                if (installed is not null && !updateAvailable)
                {
                    continue;
                }

                Console.WriteLine("\nWorking...");
                (bool success, string message) = await CloudRepositoryManager.InstallPackageAsync(package);
                Notify(message);
                return message;
            }

            if ((key == ConsoleKey.Delete || key == ConsoleKey.R) && installed is not null)
            {
                (bool success, string message) = CloudRepositoryManager.RemovePackage(package.Id);
                Notify(message);
                return message;
            }
        }
    }

    private static string GetInstallStatus(StorePackageManifest package, StoreManifest manifest)
    {
        InstalledStorePackage? installed = FindInstalled(package.Id);
        if (installed is null)
        {
            return "not installed";
        }

        return CompareVersions(package.Version, installed.Version) > 0
            ? "update available"
            : "installed";
    }

    private static InstalledStorePackage? FindInstalled(string packageId)
    {
        return CloudRepositoryManager.LoadInstalledState()
            .Packages
            .FirstOrDefault(package => package.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
    }

    private static ConsoleColor GetStatusColor(string status)
    {
        return status switch
        {
            "installed" => ConsoleColor.Green,
            "update available" => ConsoleColor.Yellow,
            _ => ConsoleColor.Gray
        };
    }

    private static int CompareVersions(string left, string right)
    {
        bool leftParsed = Version.TryParse(left, out Version? leftVersion);
        bool rightParsed = Version.TryParse(right, out Version? rightVersion);
        if (leftParsed && rightParsed)
        {
            return leftVersion!.CompareTo(rightVersion);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatDependencies(IReadOnlyList<string> dependencies)
    {
        return dependencies.Count == 0 ? "none" : string.Join(", ", dependencies);
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

    private static StorePackageManifest ToManifest(InstalledStorePackage installed)
    {
        return new StorePackageManifest
        {
            Id = installed.Id,
            Name = installed.Name,
            Version = installed.Version,
            Category = installed.Category,
            DesktopEnabled = installed.DesktopEnabled,
            DesktopIcon = installed.DesktopIcon,
            DesktopTitle = installed.DesktopTitle,
            Command = installed.Command,
            Description = "Installed package. Cloud manifest unavailable."
        };
    }

    private static void Notify(string message)
    {
        KernelLog.Info("store", message);
        // NotificationManager hook point: call NotificationManager.Notify(message) if added later.
    }
}
