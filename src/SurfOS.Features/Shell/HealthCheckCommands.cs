using System.Text.Json;

namespace SurfOS2;

internal partial class CLI_Engine
{
    private sealed record HealthCheckItem(string Name, bool Passed, string Detail);

    private static void RunHealthCheck(string[] args, int cmdIndex)
    {
        if (args.Length == cmdIndex + 2 &&
            args[cmdIndex + 1].Equals("--system", StringComparison.OrdinalIgnoreCase))
        {
            CheckSystemHealth();
            return;
        }

        if (args.Length == cmdIndex + 3 &&
            args[cmdIndex + 1].Equals("--app", StringComparison.OrdinalIgnoreCase))
        {
            CheckAppHealth(args[cmdIndex + 2]);
            return;
        }

        PrintCommandUsage("check");
    }

    private static void CheckSystemHealth()
    {
        List<HealthCheckItem> checks = [];
        string root = string.IsNullOrWhiteSpace(Import.Variables.installPath)
            ? Environment.CurrentDirectory
            : Import.Variables.installPath;

        checks.Add(new HealthCheckItem(
            "Installation",
            Directory.Exists(root),
            Directory.Exists(root) ? Path.GetFullPath(root) : $"Directory not found: {root}"));

        string[] requiredDirectories = ["home", "system", "apps", "logs", "temp"];
        List<string> missingDirectories = requiredDirectories
            .Where(directory => !Directory.Exists(Path.Combine(root, directory)))
            .ToList();
        checks.Add(new HealthCheckItem(
            "Filesystem",
            missingDirectories.Count == 0,
            missingDirectories.Count == 0
                ? "Core virtual directories are present."
                : "Missing: " + string.Join(", ", missingDirectories)));

        checks.Add(CheckStorageWritable(root));
        checks.Add(new HealthCheckItem(
            "Session",
            !string.IsNullOrWhiteSpace(Import.Variables.userName),
            string.IsNullOrWhiteSpace(Import.Variables.userName)
                ? "No active profile name."
                : $"Active profile: {Import.Variables.userName}"));

        foreach (string stateFile in new[] { "options.json", "database.json", "services.json" })
        {
            string path = Path.Combine(root, stateFile);
            if (File.Exists(path))
            {
                checks.Add(CheckJsonState(stateFile, path));
            }
        }

        try
        {
            IReadOnlyList<ServiceState> services = ServiceManager.ListServices();
            List<ServiceState> unhealthy = services
                .Where(service => service.Status.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                  service.Status.Equals("Faulted", StringComparison.OrdinalIgnoreCase) ||
                                  IsExpectedRunningService(service) && !service.IsRunning)
                .ToList();
            checks.Add(new HealthCheckItem(
                "Services",
                unhealthy.Count == 0,
                unhealthy.Count == 0
                    ? $"{services.Count} registered service(s) are in the expected state."
                    : string.Join(", ", unhealthy.Select(service => $"{service.Name} ({service.Status})"))));
        }
        catch (Exception ex)
        {
            checks.Add(new HealthCheckItem("Services", false, ex.Message));
        }

        PrintHealthReport("SurfOS system", checks);
    }

    private static HealthCheckItem CheckStorageWritable(string root)
    {
        if (!Directory.Exists(root))
        {
            return new HealthCheckItem("Storage write", false, "Installation directory is unavailable.");
        }

        string probe = Path.Combine(root, $".surfos-check-{Guid.NewGuid():N}.tmp");
        try
        {
            using (FileStream stream = new(
                       probe,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       1,
                       FileOptions.DeleteOnClose))
            {
                stream.WriteByte(0);
                stream.Flush(flushToDisk: true);
            }
            return new HealthCheckItem("Storage write", true, "Installation storage accepts writes.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new HealthCheckItem("Storage write", false, ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(probe)) File.Delete(probe);
            }
            catch
            {
                // A failed cleanup is reported by a later filesystem check or visible as a probe file.
            }
        }
    }

    private static HealthCheckItem CheckJsonState(string name, string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return new HealthCheckItem(name, true, "Valid JSON state.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new HealthCheckItem(name, false, ex.Message);
        }
    }

    private static void CheckAppHealth(string requestedName)
    {
        if (!PathSafety.IsSafeFileName(requestedName))
        {
            ShellError("check: app name must be a single safe name.", 2);
            return;
        }

        string root = string.IsNullOrWhiteSpace(Import.Variables.installPath)
            ? Environment.CurrentDirectory
            : Import.Variables.installPath;
        string appsRoot = Path.Combine(root, "apps");
        List<HealthCheckItem> checks = [];

        InstalledStorePackageState state;
        try
        {
            state = CloudRepositoryManager.LoadInstalledState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            PrintHealthReport(requestedName, [new HealthCheckItem("Package registry", false, ex.Message)]);
            return;
        }

        InstalledStorePackage? package = state.Packages.FirstOrDefault(candidate =>
            candidate.Id.Equals(requestedName, StringComparison.OrdinalIgnoreCase) ||
            candidate.Name.Equals(requestedName, StringComparison.OrdinalIgnoreCase));

        string? appDirectory = Directory.Exists(appsRoot)
            ? Directory.EnumerateDirectories(appsRoot)
                .FirstOrDefault(path =>
                    !IsInternalAppsDirectory(Path.GetFileName(path)) &&
                    Path.GetFileName(path).Equals(requestedName, StringComparison.OrdinalIgnoreCase))
            : null;

        if (package is null && appDirectory is null)
        {
            ShellError($"check: app '{requestedName}' is not installed.");
            return;
        }

        if (package is not null)
        {
            checks.Add(new HealthCheckItem(
                "Package registry",
                true,
                $"{package.Name} {package.Version} ({package.Id})"));

            List<string> files = package.InstalledFiles ?? [];
            string packagesRoot = Path.Combine(root, "Packages");
            List<string> missing = files
                .Where(path => !File.Exists(path) ||
                               !IsAllowedHealthCheckPath(path, appsRoot, packagesRoot))
                .Select(path => Path.GetFileName(path) ?? path)
                .ToList();
            checks.Add(new HealthCheckItem(
                "Installed files",
                files.Count > 0 && missing.Count == 0,
                files.Count == 0
                    ? "The package registry contains no installed files."
                    : missing.Count == 0
                        ? $"All {files.Count} registered file(s) are present."
                        : "Missing or unsafe: " + string.Join(", ", missing)));
        }
        else
        {
            checks.Add(new HealthCheckItem(
                "App directory",
                true,
                $"Present outside the package registry: {appDirectory}"));
        }

        string[] processNames = package is null
            ? [requestedName]
            : [requestedName, package.Id, package.Name, package.Command];
        bool running = ProcessManager.ListProcesses()
            .Any(process => processNames.Any(name =>
                !string.IsNullOrWhiteSpace(name) &&
                process.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        checks.Add(new HealthCheckItem(
            "Runtime",
            true,
            running ? "The app currently has a running process." : "Installed and ready; no process is currently active."));

        PrintHealthReport(package?.Name ?? requestedName, checks);
    }

    private static bool IsExpectedRunningService(ServiceState service)
    {
        if (!service.Enabled || !service.AutoStart)
        {
            return false;
        }

        return !Import.Variables.safeMode ||
               service.Name.Equals("ClockService", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInternalAppsDirectory(string name) =>
        name.Equals("cache", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("packages", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedHealthCheckPath(string path, string appsRoot, string packagesRoot)
    {
        try
        {
            return Path.IsPathFullyQualified(path) &&
                   (PathSafety.IsInsideRoot(path, appsRoot) ||
                    PathSafety.IsInsideRoot(path, packagesRoot));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static void PrintHealthReport(string target, IReadOnlyList<HealthCheckItem> checks)
    {
        Console.WriteLine($"Health check: {target}");
        foreach (HealthCheckItem check in checks)
        {
            Console.WriteLine($"  [{(check.Passed ? "PASS" : "FAIL")}] {check.Name}: {check.Detail}");
        }

        int failures = checks.Count(check => !check.Passed);
        Console.WriteLine(failures == 0
            ? $"Result: HEALTHY ({checks.Count} checks passed)"
            : $"Result: UNHEALTHY ({failures} of {checks.Count} checks failed)");
        if (failures > 0) LastExitCode = 1;
    }
}
