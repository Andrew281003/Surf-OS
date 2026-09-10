using SurfOS2;
using System.IO.Compression;

internal static class Program
{
    private static int failures;

    public static async Task<int> Main()
    {
        string testRoot = Path.Combine(Path.GetTempPath(), $"surfos-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
        try
        {
            TestVimViewport();
            TestPasswordStorage();
            TestPathSafety(testRoot);
            TestMacOsStorage(testRoot);
            TestJsonStorage(testRoot);
            TestVirtualFileSystem(testRoot);
            TestShellCommands(testRoot);
            TestFilesystemInspection(testRoot);
            TestExtendedShell(testRoot);
            await TestHttpCommands(testRoot);
            TestPackageBuilder(testRoot);
            await TestPackageBoundaries(testRoot);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }

        Console.WriteLine(failures == 0
            ? "All SurfOS regression tests passed."
            : $"{failures} SurfOS regression test(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void TestVimViewport()
    {
        Check(VimEditor.FormatViewportLine("   1 \ttext", 10) == "   1    te",
            "Vim expands tabs before clipping source rows");
        Check(VimEditor.FormatViewportLine("abc\tdef", 5) == "abc d",
            "Vim clips text at the right edge after tab expansion");
        Check(VimEditor.FormatViewportLine("a\rb\nc\bd", 20) == "abcd",
            "Vim control characters cannot displace rendered rows");
        Check(VimEditor.FormatViewportLine("text", 0) == string.Empty,
            "Vim supports a viewport without writable columns");
    }

    private static void TestPasswordStorage()
    {
        Import.DatabaseRecord account = new() { Username = "alice", Admin = "admin" };
        AccountSecurity.SetPassword(account, "correct horse");
        Check(string.IsNullOrEmpty(account.Password), "new passwords are not stored as plaintext");
        Check(AccountSecurity.VerifyPassword(account, "correct horse", out bool legacy) && !legacy,
            "hashed password verifies");
        Check(!AccountSecurity.VerifyPassword(account, "wrong", out _), "wrong password is rejected");
        Check(AccountSecurity.IsAdministrator(account), "administrator marker is recognized");
        Check(!AccountSecurity.IsAdministrator(new Import.DatabaseRecord { Admin = "User" }),
            "normal user is not treated as administrator");

        Import.DatabaseRecord oldAccount = new() { Password = "legacy" };
        Check(AccountSecurity.VerifyPassword(oldAccount, "legacy", out bool usedLegacy) && usedLegacy,
            "legacy passwords can be migrated on login");
    }

    private static void TestPathSafety(string testRoot)
    {
        Check(PathSafety.TryResolveRelativePath(testRoot, "folder/file.txt", out _),
            "relative path inside root is accepted");
        Check(!PathSafety.TryResolveRelativePath(testRoot, "../escape.txt", out _),
            "parent traversal is rejected");
        Check(!PathSafety.TryResolveRelativePath(testRoot, Path.Combine(testRoot, "absolute.txt"), out _),
            "absolute path is rejected");
        Check(!PathSafety.IsSafeFileName("../theme"), "unsafe simple name is rejected");
        Check(!PathSafety.IsSafeFileName("CON") && !PathSafety.IsSafeFileName("name."),
            "reserved and trailing-dot Windows names are rejected");
    }

    private static void TestMacOsStorage(string testRoot)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        string installPath = Path.Combine(testRoot, "macos-storage", "System");
        Import.Variables.installPath = installPath;
        Import.Variables.vhdxHostDirectory = "unused";
        Import.Variables.vhdxPath = "unused.vhdx";
        Import.Variables.partitionSizeGb = Partition_Manager.MinimumSizeGb;
        Check(Partition_Manager.ProvisionSystemStorage(out string error),
            $"macOS directory-backed storage is provisioned ({error})");
        Check(Directory.Exists(installPath) && string.IsNullOrEmpty(Import.Variables.vhdxPath),
            "macOS storage does not create a VHDX");
        PartitionManifest manifest = Partition_Manager.Create(installPath);
        Check(manifest.VolumeType == "Directory-backed virtual volume" &&
              manifest.MountPoint == Path.GetFullPath(installPath),
            "macOS storage manifest identifies its directory-backed volume");
    }

    private static void TestJsonStorage(string testRoot)
    {
        string path = Path.Combine(testRoot, "state", "settings.json");
        JsonStorage.Write(path, new { Value = 1 });
        JsonStorage.Write(path, new { Value = 2 });
        Check(File.ReadAllText(path).Contains("2", StringComparison.Ordinal),
            "JSON state can be atomically replaced");
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(),
            "JSON temporary files are cleaned up");
    }

    private static void TestVirtualFileSystem(string testRoot)
    {
        string root = Path.Combine(testRoot, "vfs");
        Import.Variables.installPath = root;
        Import.Variables.userName = "Tester";
        VirtualFileSystem.InitializeForCurrentUser();
        Check(VirtualFileSystem.ChangeDirectory("/", out _) &&
              VirtualFileSystem.CurrentDirectory == "/",
            "VFS can navigate from home to the root directory");
        Check(VirtualFileSystem.ChangeDirectory("/home/Tester", out _) &&
              VirtualFileSystem.ChangeDirectory("../..", out _) &&
              VirtualFileSystem.CurrentDirectory == "/",
            "VFS parent navigation can reach the root directory");
        Check(VirtualFileSystem.CreateDirectory("source", out _), "VFS source directory is created");
        Check(VirtualFileSystem.Touch("source/file.txt", out _), "VFS source file is created");
        Check(!VirtualFileSystem.Copy("source", "source/nested", true, false, out string copyError) &&
              copyError.Contains("itself", StringComparison.OrdinalIgnoreCase),
            "recursive self-copy is rejected");
        Check(!VirtualFileSystem.Move("/system", "/home/Tester/system", false, out _),
            "protected VFS directory cannot be moved");
        File.WriteAllText(Path.Combine(root, "database.json"), "sensitive");
        Check(!VirtualFileSystem.ReadFile("/database.json", out _, out _),
            "sensitive account storage cannot be read from the shell");
        Check(!VirtualFileSystem.WriteFile("/options.json", "tampered", false, out _),
            "system settings cannot be overwritten from the shell");
        Directory.CreateDirectory(Path.Combine(root, "apps", "sample-app"));
        Check(VirtualFileSystem.List("/apps", out _).Any(entry => entry.Name == "sample-app"),
            "/apps maps to installed applications rather than developer projects");
    }

    private static async Task TestPackageBoundaries(string testRoot)
    {
        string root = Path.Combine(testRoot, "packages-root");
        Directory.CreateDirectory(root);
        Import.Variables.installPath = root;

        (bool validId, _) = await CloudRepositoryManager.InstallPackageAsync(new StorePackageManifest
        {
            Id = "../escape",
            Name = "Escape"
        });
        Check(!validId, "package identifiers cannot traverse directories");
        (bool escapedInstallPath, _) = await CloudRepositoryManager.InstallPackageAsync(new StorePackageManifest
        {
            Id = "escape-path",
            Name = "Escape path",
            InstallPath = "Packages/../database.json"
        });
        Check(!escapedInstallPath, "package install paths cannot escape an allowed package folder");
        (bool compatibleInstall, _) = await CloudRepositoryManager.InstallPackageAsync(new StorePackageManifest
        {
            Id = "compatible-app",
            Name = "Compatible app",
            InstallPath = "apps/compatible-app/compatible-app.pkg",
            MinimumSurfOSVersion = "2.0.0"
        });
        Check(compatibleInstall, "current SurfOS version can install packages requiring version 2.0");
        Check(!CloudRepositoryManager.ValidateSha256(
                typeof(Program).Assembly.Location,
                string.Empty,
                out _),
            "downloaded packages require a declared SHA-256");

        string outsideFile = Path.Combine(testRoot, "outside.txt");
        File.WriteAllText(outsideFile, "keep");
        CloudRepositoryManager.SaveInstalledState(new InstalledStorePackageState
        {
            Packages =
            [
                new InstalledStorePackage
                {
                    Id = "tampered",
                    Name = "Tampered",
                    InstalledFiles = [outsideFile]
                }
            ]
        });
        (bool removed, _) = CloudRepositoryManager.RemovePackage("tampered");
        Check(removed && File.Exists(outsideFile),
            "tampered package state cannot delete files outside package roots");
    }

    private static void TestPackageBuilder(string testRoot)
    {
        string root = Path.Combine(testRoot, "builder-root");
        Directory.CreateDirectory(root);
        Import.Variables.installPath = root;
        Import.Variables.userName = "Builder";

        Check(PackageBuilder.TryCreateProject(
                PackageTemplate.Theme,
                "night-wave",
                "Night Wave",
                "Builder",
                out string projectPath,
                out _),
            "package builder creates a theme workspace");
        Check(File.Exists(Path.Combine(projectPath, "package.json")) &&
              File.Exists(Path.Combine(projectPath, "src", "night-wave.theme.json")) &&
              File.Exists(Path.Combine(projectPath, ".surfignore")),
            "theme workspace contains metadata, an editable payload, and a SurfCloud ignore file");
        Check(projectPath.StartsWith(Path.Combine(root, "apps", "PackageBuilder"), StringComparison.OrdinalIgnoreCase),
            "package builder workspaces live under apps");
        VirtualFileSystem.InitializeForCurrentUser();
        Check(VirtualFileSystem.TryResolvePackageBuilderFile(
                "/apps/PackageBuilder/night-wave/src/night-wave.theme.json",
                out _,
                out string builderThemeFile) &&
              builderThemeFile.Equals(Path.Combine(projectPath, "src", "night-wave.theme.json"), StringComparison.OrdinalIgnoreCase),
            "Vim can safely resolve Package Builder source files");
        Check(!VirtualFileSystem.TryResolvePackageBuilderFile("/apps/other-app/main.surf", out _, out _),
            "Vim cannot use the Package Builder exception outside its workspace");
        Check(PackageBuilder.TryValidateProject("night-wave", out _, out _),
            "generated package validates successfully");
        Check(PackageBuilder.TryExportProject("night-wave", out string exportPath, out _) &&
              File.Exists(exportPath),
            "valid package exports as a surfpkg archive");
        CLI_Engine.ValidateArchive(File.ReadAllBytes(exportPath));
        Check(true, "archive validation accepts a builder export");
        using (MemoryStream tampered = new())
        {
            tampered.Write(File.ReadAllBytes(exportPath)); tampered.Position = 0;
            using (ZipArchive edit = new(tampered, ZipArchiveMode.Update, leaveOpen: true))
            {
                var manifestEntry = edit.GetEntry("manifest.json")!;
                StorePackageManifest manifest;
                using (var source = manifestEntry.Open()) manifest = System.Text.Json.JsonSerializer.Deserialize<StorePackageManifest>(source)!;
                manifest.Sha256 = "00"; manifestEntry.Delete();
                using var destination = edit.CreateEntry("manifest.json").Open();
                System.Text.Json.JsonSerializer.Serialize(destination, manifest);
            }
            bool rejected = false;
            try { CLI_Engine.ValidateArchive(tampered.ToArray()); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "archive validation rejects payload checksum mismatch");
        }

        using ZipArchive archive = ZipFile.OpenRead(exportPath);
        Check(archive.GetEntry("manifest.json") is not null &&
              archive.GetEntry("payload/night-wave.json") is not null,
            "export includes a store manifest and payload");
        Directory.CreateDirectory(Path.Combine(projectPath, "bin", "Debug"));
        File.WriteAllText(Path.Combine(projectPath, "bin", "Debug", "private.pdb"), "debug");
        File.WriteAllText(Path.Combine(projectPath, "notes.txt"), "ship this");
        Check(PackageBuilder.TryCreateSourceSnapshot("night-wave", out byte[] sourceSnapshot, out _),
            "package builder creates a filtered source snapshot for publishing");
        using (MemoryStream sourceStream = new(sourceSnapshot))
        using (ZipArchive sourceArchive = new(sourceStream, ZipArchiveMode.Read))
        {
            Check(sourceArchive.GetEntry("notes.txt") is not null &&
                  sourceArchive.GetEntry("bin/Debug/private.pdb") is null,
                ".surfignore excludes matching files from SurfCloud source uploads");
        }

        CloudRepositoryManager.SaveInstalledState(new InstalledStorePackageState
        {
            Packages =
            [
                new InstalledStorePackage
                {
                    Id = "night-wave",
                    Name = "Night Wave",
                    Version = "1.0.0"
                }
            ]
        });
        StoreManifest updateManifest = new()
        {
            Packages =
            [
                new StorePackageManifest { Id = "night-wave", Name = "Night Wave", Version = "1.1.0" }
            ]
        };
        Check(CloudRepositoryManager.GetNewUpdateNotifications(updateManifest).Count == 1 &&
              CloudRepositoryManager.GetNewUpdateNotifications(updateManifest).Count == 0,
            "app updates create one notification per newly seen version");
        updateManifest.Packages[0].Version = "1.2.0";
        Check(CloudRepositoryManager.GetNewUpdateNotifications(updateManifest).Count == 1,
            "a later app version creates a new notification");
        Check(!PackageBuilder.TryCreateProject(
                PackageTemplate.Program,
                "../escape",
                "Escape",
                "Builder",
                out _,
                out _),
            "package builder rejects path traversal IDs");
    }

    private static void TestShellCommands(string testRoot)
    {
        string root = Path.Combine(testRoot, "shell");
        Import.Variables.installPath = root;
        Import.Variables.userName = "Limited";
        Import.Variables.uuid = 10;
        Import.Variables.userDatabase =
        [
            new Import.DatabaseRecord { ID = 10, Username = "Limited", Admin = "User" }
        ];
        VirtualFileSystem.InitializeForCurrentUser();
        KernelLog.Initialize(root);
        string optionsPath = Path.Combine(root, "options.json");
        JsonStorage.Write(optionsPath, new Import.SystemOptions
        {
            DefaultTheme = "Custom", MachineName = "KeepThisName"
        });
        Directory.CreateDirectory(Path.Combine(root, "Packages"));
        string themePath = Path.Combine(root, "Packages", "Custom.json");
        File.WriteAllText(themePath, "{}");
        Import.Variables.defaultTheme = "Custom";
        Import.Variables.activeForegroundColor = ConsoleColor.Red;
        Import.Variables.activeBackgroundColor = ConsoleColor.Blue;
        Import.Variables.activePromptStyle = "Custom";
        string unloadOutput = RunShellCommand("theme unload");
        Check(unloadOutput.Contains("Theme unloaded") &&
              Import.Variables.activeForegroundColor == ConsoleColor.Gray &&
              Import.Variables.activeBackgroundColor == ConsoleColor.Black &&
              Import.Variables.activePromptStyle == "Standard",
            "theme unload restores the default appearance");
        Import.SystemOptions? unloadedOptions = JsonStorage.Read<Import.SystemOptions>(optionsPath);
        Check(unloadedOptions?.DefaultTheme == string.Empty &&
              unloadedOptions.MachineName == "KeepThisName" && File.Exists(themePath),
            "theme unload persists without removing packages or unrelated settings");
        Screen_Print.Print_Selected_Package();
        Check(Import.Variables.defaultTheme == string.Empty &&
              Import.Variables.activePromptStyle == "Standard",
            "startup rendering accepts an unloaded theme");
        TextWriter originalOutput = Console.Out;
        using (StringWriter startupOutput = new())
        {
            Console.SetOut(startupOutput);
            try
            {
                CLI_Engine.PrintStartupInfoIfNoTheme();
            }
            finally
            {
                Console.SetOut(originalOutput);
            }

            Check(startupOutput.ToString().Contains("SurfOS 2.0", StringComparison.Ordinal),
                "startup prints system info when no theme is active");
        }
        RunShellCommand("theme unload");
        Check(Import.Variables.defaultTheme == string.Empty, "theme unload can be repeated");
        RunShellCommand("theme default Custom");
        Check(JsonStorage.Read<Import.SystemOptions>(optionsPath)?.DefaultTheme == "Custom",
            "a default theme can be selected again after unloading");
        VirtualFileSystem.WriteFile("lines.txt", "one\n", false, out _);

        string wcOutput = RunShellCommand("wc -l lines.txt");
        Check(wcOutput.Contains("1 lines.txt", StringComparison.Ordinal),
            "wc does not count a phantom line after a trailing newline");
        string tailOutput = RunShellCommand("tail -n 1 lines.txt");
        Check(tailOutput.Contains("one", StringComparison.Ordinal),
            "tail returns the final real line when a file ends with a newline");

        string startingDirectory = VirtualFileSystem.CurrentDirectory;
        RunShellCommand("mkdir child");
        RunShellCommand("cd child");
        RunShellCommand("cd..");
        Check(VirtualFileSystem.CurrentDirectory == startingDirectory,
            "cd.. changes to the parent virtual directory");

        int originalUsers = Import.Variables.userDatabase.Count;
        string forceOutput = RunShellCommand("user add Intruder pass1234 --force");
        Check(Import.Variables.userDatabase.Count == originalUsers &&
              forceOutput.Contains("not an administrator", StringComparison.OrdinalIgnoreCase),
            "non-administrator profiles cannot use --force");

        Import.Variables.userName = "Administrator";
        Import.Variables.userDatabase =
        [
            new Import.DatabaseRecord { ID = 1, Username = "Administrator", Admin = "Administrator" }
        ];
        string uninstallOutput = RunShellCommand("uninstall");
        Check(uninstallOutput.Contains("active install path could not be verified", StringComparison.OrdinalIgnoreCase),
            "uninstall without --force is accepted before safety validation");
        Import.Variables.userName = "Limited";
        Import.Variables.userDatabase =
        [
            new Import.DatabaseRecord { ID = 10, Username = "Limited", Admin = "User" }
        ];
    }

    private static void TestFilesystemInspection(string testRoot)
    {
        string root = Path.Combine(testRoot, "inspection");
        Import.Variables.installPath = root;
        Import.Variables.userName = "Inspector";
        VirtualFileSystem.InitializeForCurrentUser();
        RunShellCommand("mkdir \"sample folder/nested\"");
        VirtualFileSystem.WriteFile("sample folder/nested/report.TXT", "hello", false, out _);
        VirtualFileSystem.WriteFile("sample folder/.hidden", "abc", false, out _);
        string tree = RunShellCommand("tree \"sample folder\"");
        Check(tree.Contains("report.TXT") && tree.Contains("nested") && !tree.Contains(".hidden"),
            "tree traverses quoted paths and hides dot files");
        Check(RunShellCommand("tree -a \"sample folder\"").Contains(".hidden"),
            "tree -a includes hidden entries");
        string found = RunShellCommand("find \"sample folder\" -name \"*.txt\"");
        Check(found.Contains("/sample folder/nested/report.TXT") && !found.Contains(".hidden"),
            "find matches recursive names case-insensitively");
        Check(RunShellCommand("stat \"sample folder/nested/report.TXT\"").Contains("Size: 5 bytes"),
            "stat reports actual byte length");
        Check(RunShellCommand("du \"sample folder\"").Contains("8\t"),
            "du counts nested and hidden file bytes once");
        Check(!RunShellCommand("du -h").Contains("usage:", StringComparison.OrdinalIgnoreCase),
            "du -h executes instead of opening help");
        Check(RunShellCommand("df -h").Contains("unavailable"),
            "df does not invent capacity without a manifest");
        JsonStorage.Write(Partition_Manager.ManifestPath(root), new PartitionManifest
        { CapacityBytes = 10000, SystemReservedBytes = 1000 });
        string df = RunShellCommand("df");
        Check(df.Contains("10000  8  1000  8992"),
            "df excludes reserves and counts file usage once");
        foreach (string command in new[] { "tree", "find", "stat", "du", "df" })
            Check(RunShellCommand(command + " --help").Contains(command), $"{command} has help");
        Check(RunShellCommand("find").Contains("find <path>"), "find requires a path");
        Check(RunShellCommand("tree missing").Contains("tree:"), "missing paths produce a command error");
        string link = Path.Combine(root, "home", "Inspector", "junction");
        bool linkCreated;
        if (OperatingSystem.IsWindows())
        {
            // Junction creation needs no symbolic-link privilege on Windows.
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe", Arguments = $"/c mklink /J \"{link}\" \"{testRoot}\"",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            });
            process!.WaitForExit();
            linkCreated = process.ExitCode == 0;
        }
        else
        {
            Directory.CreateSymbolicLink(link, testRoot);
            linkCreated = true;
        }

        Check(linkCreated, "directory-link test fixture created");
        if (Directory.Exists(link))
        {
            try
            {
                Check(RunShellCommand("du junction").Contains("not followed"),
                    "recursive inspection refuses junctions outside the virtual root");
            }
            finally { Directory.Delete(link); }
        }
    }

    private static void TestExtendedShell(string testRoot)
    {
        Import.Variables.installPath = Path.Combine(testRoot, "extended-shell");
        Import.Variables.userName = "ShellUser";
        Import.Variables.safeMode = false;
        Import.Variables.networkProfile = "Private";
        VirtualFileSystem.InitializeForCurrentUser();
        CLI_Engine.ResetShellSession();
        ServiceManager.Initialize();
        ServiceManager.StartAutostartServices();
        string systemCheck = RunShellCommand("check --system");
        Check(systemCheck.Contains("Result: HEALTHY", StringComparison.Ordinal) &&
              systemCheck.Contains("Services", StringComparison.Ordinal) &&
              CLI_Engine.LastExitCode == 0,
            "check --system reports core filesystem, storage, session, and service health");
        string appFolder = Path.Combine(Import.Variables.installPath, "apps", "demo-app");
        Directory.CreateDirectory(appFolder);
        string appFile = Path.Combine(appFolder, "demo-app.pkg");
        File.WriteAllText(appFile, "demo");
        CloudRepositoryManager.SaveInstalledState(new InstalledStorePackageState
        {
            Packages =
            [
                new InstalledStorePackage
                {
                    Id = "demo-app",
                    Name = "Demo App",
                    Version = "1.0.0",
                    Command = "echo demo-app-launched",
                    InstalledFiles = [appFile]
                }
            ]
        });
        string appList = RunShellCommand("app --list");
        Check(appList.Contains("demo-app", StringComparison.Ordinal) &&
              appList.Contains("Demo App", StringComparison.Ordinal) &&
              appList.Contains("ready", StringComparison.OrdinalIgnoreCase),
            "app --list shows installed launchable apps");
        Check(RunShellCommand("app --open \"Demo App\"").Contains("demo-app-launched", StringComparison.Ordinal) &&
              CLI_Engine.LastExitCode == 0,
            "app --open resolves display names and runs package launch commands");
        Check(RunShellCommand("app --open missing-app").Contains("not installed", StringComparison.OrdinalIgnoreCase) &&
              CLI_Engine.LastExitCode == 1,
            "app --open rejects unknown apps");
        string appConfig = RunShellCommand("app --config");
        Check(appConfig.Contains("/apps/installed_packages.json", StringComparison.Ordinal) &&
              appConfig.Contains("Installed apps : 1", StringComparison.Ordinal) &&
              CLI_Engine.LastExitCode == 0,
            "app --config reports the app registry configuration");
        Check(RunShellCommand("app --help").Contains("app --list", StringComparison.Ordinal) &&
              CLI_Engine.LastExitCode == 0,
            "app exposes command help");
        string appCheck = RunShellCommand("check --app \"Demo App\"");
        Check(appCheck.Contains("Result: HEALTHY", StringComparison.Ordinal) &&
              appCheck.Contains("no process is currently active", StringComparison.OrdinalIgnoreCase) &&
              CLI_Engine.LastExitCode == 0,
            "check --app accepts a display name and treats an installed idle app as healthy");
        File.Delete(appFile);
        Check(RunShellCommand("check --app demo-app").Contains("Result: UNHEALTHY", StringComparison.Ordinal) &&
              CLI_Engine.LastExitCode == 1,
            "check --app detects missing registered files and returns failure");
        Check(RunShellCommand("check --app missing-app").Contains("not installed", StringComparison.OrdinalIgnoreCase) &&
              CLI_Engine.LastExitCode == 1,
            "check --app rejects unknown applications");
        Check(RunShellCommand("check").Contains("check --system", StringComparison.Ordinal) &&
              CLI_Engine.LastExitCode == 2,
            "check requires exactly one supported target");
        Check(RunShellCommand("check --help").Contains("check --system", StringComparison.Ordinal) &&
              CLI_Engine.LastExitCode == 0,
            "check exposes command help");
        RunShellCommand("echo 'first error' > \"input file.txt\"");
        RunShellCommand("echo second >> \"input file.txt\"");
        string filtered = RunShellCommand("cat \"input file.txt\" | grep error");
        Check(filtered.Contains("first error") && !filtered.Contains("second"), "pipeline passes cat output to grep");
        Check(RunShellCommand("wc -l < \"input file.txt\"").Contains("2 -"), "input redirection feeds wc");
        RunShellCommand("sys --json > system.json");
        Check(VirtualFileSystem.ReadFile("system.json", out string json, out _) && System.Text.Json.JsonDocument.Parse(json).RootElement.ValueKind == System.Text.Json.JsonValueKind.Object,
            "general redirection preserves JSON output");
        RunShellCommand("cat missing && echo wrong > should-not-exist.txt || echo recovered > recovered.txt");
        Check(!VirtualFileSystem.ReadFile("should-not-exist.txt", out _, out _) && VirtualFileSystem.ReadFile("recovered.txt", out string recovery, out _) && recovery.Contains("recovered"), "conditional chains follow filesystem failure status");
        Check(RunShellCommand("echo ok | grep missing || echo no-match").Contains("no-match"), "grep no-match has a failure exit status");
        Check(RunShellCommand("echo ok && echo next || echo wrong").Contains("next") && CLI_Engine.LastExitCode == 0, "successful AND/OR chain retains success");
        RunShellCommand("echo 'a|b > c && d' > operators.txt");
        Check(VirtualFileSystem.ReadFile("operators.txt", out string literal, out _) && literal.Trim() == "a|b > c && d", "quoted operators remain literal text");
        RunShellCommand("echo x > malformed.txt |");
        Check(CLI_Engine.LastExitCode != 0 && !VirtualFileSystem.ReadFile("malformed.txt", out _, out _), "entire malformed command is rejected before mutations");
        Check(RunShellCommand("touch unexpected.txt > /system/blocked.txt || echo blocked").Contains("blocked") && !VirtualFileSystem.ReadFile("unexpected.txt", out _, out _), "invalid output destination prevents command side effects");
        Check(RunShellCommand("cat < missing-input.txt || echo recovered").Contains("recovered"), "input redirection errors allow OR recovery");
        RunShellCommand("echo original > preserve.txt");
        RunShellCommand("cat missing > preserve.txt");
        Check(VirtualFileSystem.ReadFile("preserve.txt", out string preserved, out _) && preserved.Trim() == "original", "failed redirected command preserves existing destination");
        RunShellCommand("setenv MESSAGE 'hello world' && echo \"$MESSAGE\" > variable.txt");
        Check(VirtualFileSystem.ReadFile("variable.txt", out string variable, out _) && variable.Trim() == "hello world", "variables expand at stage execution after setenv");
        Check(RunShellCommand("echo '$MESSAGE'").Contains("$MESSAGE"), "single quotes suppress variable expansion");
        RunShellCommand("alias say='echo hello'");
        Check(RunShellCommand("say world").Contains("hello world"), "aliases retain arguments");
        Check(RunShellCommand("which say").Contains("alias for"), "which resolves an alias");
        RunShellCommand("alias cycle='cycle'");
        Check(RunShellCommand("cycle").Contains("expansion limit"), "alias cycles stop without recursion overflow");
        RunShellCommand("unalias say");
        RunShellCommand("which say");
        Check(CLI_Engine.LastExitCode != 0, "unalias removes command resolution");
        RunShellCommand("setenv EMPTY ''");
        Check(RunShellCommand("env EMPTY").Trim() == "", "empty quoted arguments are preserved");
        RunShellCommand("sleep 0");
        Check(CLI_Engine.LastExitCode == 0, "zero-second sleep succeeds");
        RunShellCommand("sleep NaN");
        Check(CLI_Engine.LastExitCode != 0, "sleep rejects non-finite durations");
        RunShellCommand("hostname forbidden");
        Check(CLI_Engine.LastExitCode != 0, "hostname mutation requires administrator force");
        RunShellCommand("publish no-project");
        Check(CLI_Engine.LastExitCode != 0, "publishing invalid builds fails validation");
        RunShellCommand("sandbox create demo");
        Check(RunShellCommand("sandbox list").Contains("demo"), "sandbox staging folders can be created and listed");
        RunShellCommand("sandbox run demo");
        Check(CLI_Engine.LastExitCode == 69, "sandbox never claims unsupported isolation");
        RunShellCommand("echo linked > link-source.txt");
        RunShellCommand("ln link-source.txt link-copy.txt");
        Check(CLI_Engine.LastExitCode == 0 && RunShellCommand("cat link-copy.txt").Contains("linked"), "ln creates a working hard link");
        RunShellCommand("chmod 400 link-source.txt");
        Check(CLI_Engine.LastExitCode == 0 && RunShellCommand("stat link-source.txt").Contains("Mode: 400"), "chmod persists and reports virtual mode bits");
        RunShellCommand("chmod 600 link-source.txt");
        List<Import.DatabaseRecord> savedUsers = Import.Variables.userDatabase;
        Import.Variables.userDatabase =
        [
            new Import.DatabaseRecord { Username = "ShellUser", Admin = "Administrator" },
            new Import.DatabaseRecord { Username = "FileOwner", Admin = "User" }
        ];
        RunShellCommand("chown FileOwner link-source.txt --force");
        Check(CLI_Engine.LastExitCode == 0 && RunShellCommand("stat link-source.txt").Contains("Owner: FileOwner"), "chown persists and reports a SurfOS owner");
        Check(RunShellCommand("kernel modules").Contains("Service"), "kernel modules exposes service-backed runtime modules");
        Check(RunShellCommand("lsmod").Contains("Module") && CLI_Engine.LastExitCode == 0, "lsmod lists service-backed runtime modules");
        RunShellCommand("modload ClockService --force");
        Check(CLI_Engine.LastExitCode == 0 && ServiceManager.GetStatus("ClockService")?.IsRunning == true, "modload starts a service-backed module");
        RunShellCommand("modunload ClockService --force");
        Check(CLI_Engine.LastExitCode == 0 && ServiceManager.GetStatus("ClockService")?.IsRunning == false, "modunload stops a service-backed module");
        Import.Variables.userDatabase = savedUsers;
        RunShellCommand("modinfo missing");
        Check(CLI_Engine.LastExitCode != 0, "modinfo rejects unknown modules");
        foreach (string command in new[] { "mount device /target", "umount /target", "os boot other", "kernel boot other" })
        { RunShellCommand(command); Check(CLI_Engine.LastExitCode == 69, command + " reports unavailable backend"); }
        foreach (string command in new[] { "ln", "chmod", "chown", "history", "which", "alias", "unalias", "env", "setenv", "unsetenv", "hostname", "uptime", "uname", "version", "reboot", "sleep", "ip", "netstat", "dns", "traceroute", "curl", "wget", "network", "kernel", "lsmod", "modinfo", "modload", "modunload", "devices", "mount", "umount", "fsck", "meminfo", "cpuinfo", "hwinfo", "os", "sandbox", "validate", "publish", "sign" })
        { string help = RunShellCommand(command + " --help"); Check(help.Contains(command) && CLI_Engine.LastExitCode == 0, command + " exposes successful help"); }
        byte[] bytes = [0, 255, 1, 2, 3];
        VirtualFileSystem.WriteBytes("binary.pkg", bytes);
        Check(VirtualFileSystem.ReadBytes("binary.pkg", 10).SequenceEqual(bytes), "binary VFS preserves download bytes");
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var signature = CLI_Engine.SignBytes(bytes, rsa);
        Check(CLI_Engine.VerifyBytes(bytes, signature) && !CLI_Engine.VerifyBytes([9, 8, 7], signature), "signatures verify bytes and reject tampering");
        VirtualFileSystem.WriteBytes("binary.pkg.sig.json", System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(signature));
        Check(RunShellCommand("sign verify binary.pkg").Contains("Signature valid"), "sign verify reads detached signatures");
        RunShellCommand("network disconnect");
        RunShellCommand("curl https://example.com");
        Check(CLI_Engine.LastExitCode != 0, "offline network policy blocks fetching before network access");
        RunShellCommand("network connect Public");
        Check(Import.Variables.networkProfile == "Public" && JsonStorage.Read<Import.SystemOptions>(Path.Combine(Import.Variables.installPath, "options.json"))?.NetworkProfile == "Public", "network profile updates persist");
        RunShellCommand("history clear");
        Check(!RunShellCommand("history").Contains("network connect"), "history clear removes prior commands");
        CLI_Engine.ResetShellSession();
        Check(!RunShellCommand("env").Contains("MESSAGE="), "login-session reset clears variables");
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            bool running = true;
            CLI_Engine.ExecuteCommand("reboot", ref running, true);
            Check(!running && SurfOsApplication.RebootRequested, "reboot requests the SurfOS boot loop without restarting the host");
            SurfOsApplication.RebootRequested = false;
        }
    }

    private static async Task TestHttpCommands(string testRoot)
    {
        byte[] body = [0, 1, 255, 42];
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        Task server = Task.Run(async () =>
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
            for (int i = 0; i < 3; i++)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, leaveOpen: true);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
                string status = i == 1 ? "404 Not Found" : "200 OK";
                long length = i == 2 ? CLI_Engine.MaximumDownloadBytes + 1 : body.Length;
                byte[] header = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: {length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, timeout.Token);
                if (i == 0) await stream.WriteAsync(body, timeout.Token);
            }
        });
        RunShellCommand($"wget http://127.0.0.1:{port}/fixture.bin -o downloaded.bin");
        Check(CLI_Engine.LastExitCode == 0 && VirtualFileSystem.ReadBytes("downloaded.bin", 10).SequenceEqual(body), "wget downloads actual HTTP bytes into virtual storage");
        RunShellCommand($"wget http://127.0.0.1:{port}/missing -o downloaded.bin");
        Check(CLI_Engine.LastExitCode != 0 && VirtualFileSystem.ReadBytes("downloaded.bin", 10).SequenceEqual(body), "HTTP errors preserve the existing download destination");
        RunShellCommand($"curl http://127.0.0.1:{port}/oversized");
        Check(CLI_Engine.LastExitCode != 0, "HTTP downloads enforce the declared size limit");
        await server.WaitAsync(TimeSpan.FromSeconds(10));
        RunShellCommand("curl file:///private");
        Check(CLI_Engine.LastExitCode != 0, "HTTP commands reject non-HTTP schemes");
    }

    private static string RunShellCommand(string command)
    {
        TextWriter original = Console.Out;
        using StringWriter capture = new();
        try
        {
            Console.SetOut(capture);
            bool running = true;
            CLI_Engine.ExecuteCommand(command, ref running, isScriptExecution: false);
            return capture.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static void Check(bool condition, string description)
    {
        if (condition)
        {
            Console.WriteLine($"PASS: {description}");
            return;
        }

        failures++;
        Console.WriteLine($"FAIL: {description}");
    }
}
