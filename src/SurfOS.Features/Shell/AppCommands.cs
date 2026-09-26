namespace SurfOS2;

internal partial class CLI_Engine
{
    private static void ManageApps(
        string[] args,
        int cmdIndex,
        ref bool isRunning,
        bool isScriptExecution)
    {
        string[] options = args[(cmdIndex + 1)..];
        if (options is ["--list"])
        {
            ListInstalledApps();
            return;
        }

        if (options is ["--config"])
        {
            ShowAppConfiguration();
            return;
        }

        if (options is ["--open", var requestedName])
        {
            OpenInstalledApp(requestedName, false, ref isRunning, isScriptExecution);
            return;
        }

        if (options is ["--open", var approvedName, "--allow-third-party"])
        {
            OpenInstalledApp(approvedName, true, ref isRunning, isScriptExecution);
            return;
        }

        PrintCommandUsage("app");
    }

    private static List<InstalledStorePackage> GetInstalledApps() =>
        CloudRepositoryManager.LoadInstalledState()
            .Packages
            .Where(package =>
                !package.Category.Equals("Theme", StringComparison.OrdinalIgnoreCase) &&
                !package.Category.Equals("Themes", StringComparison.OrdinalIgnoreCase))
            .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void ListInstalledApps()
    {
        List<InstalledStorePackage> apps = GetInstalledApps();
        Console.WriteLine("\n--- Installed apps ---");
        if (apps.Count == 0)
        {
            Console.WriteLine("No apps installed. Use 'surf search' to find one.");
            return;
        }

        Console.WriteLine($"{"ID",-20} {"VERSION",-10} {"STATUS",-13} NAME");
        foreach (InstalledStorePackage app in apps)
        {
            string status = string.IsNullOrWhiteSpace(app.Command) ? "not launchable" : "ready";
            Console.WriteLine($"{app.Id,-20} {app.Version,-10} {status,-13} {app.Name}");
        }
    }

    private static void OpenInstalledApp(
        string requestedName,
        bool allowThirdParty,
        ref bool isRunning,
        bool isScriptExecution)
    {
        InstalledStorePackage? app = GetInstalledApps().FirstOrDefault(candidate =>
            candidate.Id.Equals(requestedName, StringComparison.OrdinalIgnoreCase) ||
            candidate.Name.Equals(requestedName, StringComparison.OrdinalIgnoreCase));
        if (app is null)
        {
            ShellError($"app: '{requestedName}' is not installed.");
            return;
        }

        if (string.IsNullOrWhiteSpace(app.Command) ||
            !TryTokenizeCommand(app.Command, out string[] launchCommand, out _) ||
            launchCommand.Length == 0 ||
            launchCommand[0].Equals("app", StringComparison.OrdinalIgnoreCase))
        {
            ShellError($"app: '{app.Name}' does not define a valid launch command.");
            return;
        }

        bool trustedBuiltIn = app.Id.Equals("surfcode-ide", StringComparison.Ordinal) &&
            app.Command.Equals("code", StringComparison.Ordinal);
        if (!trustedBuiltIn && !allowThirdParty)
        {
            ShellError($"app: '{app.Name}' is third-party code. To run it with your SurfOS permissions, use app --open \"{app.Id}\" --allow-third-party.");
            return;
        }

        KernelLog.Info("app", $"launch '{app.Id}'");
        ExecuteCommand(app.Command, ref isRunning, isScriptExecution);
    }

    private static void ShowAppConfiguration()
    {
        List<InstalledStorePackage> apps = GetInstalledApps();
        Console.WriteLine("\n--- App manager configuration ---");
        Console.WriteLine("Apps directory : /apps");
        Console.WriteLine("Registry       : /apps/installed_packages.json");
        Console.WriteLine($"Installed apps : {apps.Count}");
        Console.WriteLine($"Launchable     : {apps.Count(app => !string.IsNullOrWhiteSpace(app.Command))}");
        Console.WriteLine($"Cloud services : {(Import.Variables.cloudServicesEnabled ? "enabled" : "disabled")}");
        Console.WriteLine($"Network profile: {Import.Variables.networkProfile}");
        Console.WriteLine("Launch commands are supplied by each installed package manifest.");
    }
}
