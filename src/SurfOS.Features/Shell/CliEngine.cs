using SurfOS2.os_Apps;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;


namespace SurfOS2
{
    internal partial class CLI_Engine
    {
        private static readonly string[] SurfboardLogo =
        [
            "          -.--.",
            "          )  \" '-,",
            "          ',' 2  \\_",
            "           \\q \\ .  \\",
            "        _.--'  '----.__",
            "       /  ._      _.__ \\__",
            "    *.'*.'  \\_ .-._\\_ '-, }",
            "   (,/ _.---;-(  . \\ \\   ~",
            " ____ (  .___\\_\\  \\/_/",
            "(      '-._ \\   \\ |",
            " '._       ),> _) >",
            "    '-._   '       -._",
            "        '-._           '.",
            "            '-._         `\\",
            "                '-._       '.",
            "                    '-._     \\",
            "                        `~---'"
        ];

        private static readonly IReadOnlyDictionary<string, (string Usage, string Details)> CommandUsage =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["help"] = ("help [section|command]", "Show all help, one section, or detailed usage for one command."),
                ["pwd"] = ("pwd", "Print the current virtual directory."),
                ["tree"] = ("tree [-a] [path]", "Show a directory tree; -a includes hidden entries. Links are not followed."),
                ["find"] = ("find <path> [-name pattern]", "Recursively match names with * and ? (case-insensitive). Links are not followed."),
                ["stat"] = ("stat <path>", "Show file type, byte size, timestamps, and host attributes."),
                ["du"] = ("du [-h] [path]", "Show total apparent file bytes, including hidden entries; -h uses readable units."),
                ["df"] = ("df [-h]", "Show the shared SurfOS partition capacity, used, reserved, and available space."),
                ["ls"] = ("ls [-alh] [path]", "-a includes hidden files; -l uses long format; -h uses readable sizes."),
                ["cd"] = ("cd [path]", "Change virtual directory. With no path, return home."),
                ["mkdir"] = ("mkdir [-p] <directory>...", "Create one or more directories. Parent creation is supported."),
                ["touch"] = ("touch <file>...", "Create files or update their modification time."),
                ["cat"] = ("cat [-n] <file>...", "Concatenate files; -n numbers output lines."),
                ["echo"] = ("echo [text...] [> file | >> file]", "Print text, overwrite a file with >, or append with >>."),
                ["head"] = ("head [-n count] <file>", "Print the first lines of a file; the default count is 10."),
                ["tail"] = ("tail [-n count] <file>", "Print the last lines of a file; the default count is 10."),
                ["grep"] = ("grep [-in] <pattern> <file>", "-i ignores case; -n prints matching line numbers."),
                ["wc"] = ("wc [-lwc] <file>", "Count lines, words, and UTF-8 bytes."),
                ["rm"] = ("rm [-rf] <path>...", "-r removes directories recursively; -f ignores missing paths."),
                ["rmdir"] = ("rmdir <directory>", "Remove an empty virtual directory."),
                ["cp"] = ("cp [-rf] <source> <destination>", "-r copies directories; -f replaces existing content."),
                ["mv"] = ("mv [-f] <source> <destination>", "Move or rename a path; -f replaces an existing file."),
                ["backup"] = ("backup <name> | backup list", "Create a recovery backup or list existing backups."),
                ["clear"] = ("clear [-d seconds]", "Clear immediately or after displaying the header for a delay."),
                ["cleart"] = ("clearT <seconds>", "Compatibility alias for clear -d <seconds>."),
                ["whoami"] = ("whoami [-u|--id]", "Show session details; -u prints only the name and --id only the UUID."),
                ["info"] = ("info [--json]", "Show installation and partition details; --json emits machine-readable output."),
                ["anim"] = ("anim [status|on|off]", "Inspect or change retro output animations."),
                ["fontsize"] = ("fontsize <number>", "Change the console font size."),
                ["game"] = ("game [list|play <name>]", "List or play installed SurfOS games."),
                ["calc"] = ("calc [<number> <operator> <number>]", "Calculate inline with +, -, *, /, or ^, or open interactive mode."),
                ["sys"] = ("sys [--json]", "Show runtime CPU, memory, user, and uptime information."),
                ["free"] = ("free [--bytes]", "Show SurfOS simulated memory and disk-backed swap usage."),
                ["swapon"] = ("swapon --show", "Show the active SurfOS swap file and paging policy."),
                ["swap"] = ("swap [status|set <0-95>|trim]", "Inspect or tune disk-backed simulated swap pressure."),
                ["store"] = ("store", "Open the Surf Store package browser."),
                ["mail"] = ("mail [list] | mail send <user> <message> | mail delete <id> | mail clear", "Read and manage profile messages."),
                ["music"] = ("music [open|list|cloud|play <file>|pause|stop|download <song>|playlist ...]", "Control local/cloud music and playlists."),
                ["run"] = ("run [-v] <script_file.txt>", "Execute shell commands from a script; -v prints each command."),
                ["clock"] = ("clock [-u|--iso]", "Show local configured time; -u uses UTC and --iso uses ISO 8601."),
                ["calendar"] = ("calendar [month] [year]", "Show a calendar for the current or requested month and year."),
                ["alarm"] = ("alarm [list|add <HH:mm> [message]|remove <id>|enable <id>|disable <id>]", "List, create, remove, enable, or disable scheduled alarms."),
                ["ping"] = ("ping [-c count] [-W timeout_ms] <address>", "Send one or more latency probes with a configurable timeout."),
                ["todo"] = ("todo [list|add <text>|complete <id>|remove <id>|clear]", "Manage the persistent task list."),
                ["edit"] = ("edit <filename.txt>", "Open the Vim-style SurfEdit text editor."),
                ["vim"] = ("vim <file>", "Open any virtual text file in the Vim-style editor."),
                ["code"] = ("code [workspace]", "Open the installed SurfCode IDE."),
                ["builder"] = ("builder [new <type> <id>|list|validate <id>|export <id>]", "Create themes, games, programs, and extensions."),
                ["surfai"] = ("surfai [question]", "Ask the local SurfOS/SurfCloud assistant."),
                ["surf"] = ("surf <store|search [name]|install <package>|remove <package>|list|update|info <package>>", "Manage SurfCloud packages."),
                ["ps"] = ("ps [-a] [-r]", "-a includes completed processes; -r shows only running processes."),
                ["top"] = ("top", "Open the refreshing process monitor; press Escape to exit."),
                ["kill"] = ("kill [-s TERM] <pid>", "Request a safe simulated termination. TERM is the supported signal."),
                ["service"] = ("service [list|status <name>|start <name>|stop <name>|restart <name>]", "Inspect and control background services."),
                ["dmesg"] = ("dmesg [all|boot|-b|errors|--errors|clear|-c]", "Read or clear kernel messages, optionally filtered."),
                ["user"] = ("user list | user add <name> <password> --force | user remove <name> --force", "List or administer local profiles; --force is required for administrator actions."),
                ["theme"] = ("theme list | theme <load|default|remove> <name> | theme unload | theme link", "List, apply, persist, unload to OS defaults, remove, or browse themes."),
                ["logout"] = ("logout", "End the active user shell session."),
                ["shutdown"] = ("shutdown", "Stop services and shut down SurfOS."),
                ["exit"] = ("exit", "Alias for shutdown."),
                ["uninstall"] = ("uninstall [--force]", "Permanently remove the verified active SurfOS installation; --force skips confirmation prompts.")
            };

        [SupportedOSPlatform("windows")]
        public static void StartTerminal()
        {
            try
            {
                using ProcessHandle terminalProcess = ProcessManager.StartProcess(
                    "Terminal",
                    32,
                    isProtected: true);

                Console.Clear();
                ServiceManager.Initialize();
                if (Import.Variables.safeMode)
                {
                    ServiceManager.StartSafeModeServices();
                }
                else
                {
                    ServiceManager.StartAutostartServices();
                }

                VirtualFileSystem.InitializeForCurrentUser();
                Swap_Manager.Initialize();
                Screen_Print.Print_Selected_Package();
                PrintStartupInfoIfNoTheme();
                if (Import.Variables.safeMode)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\nSAFE MODE: cloud presence, animations, and non-essential services are disabled.");
                    Console.ResetColor();
                }
                RetroConsole.TypeLine("Type 'help' to see a list of commands.", 2);
                KernelLog.Success("shell", "command processor ready");

                bool isRunning = true;

                while (isRunning)
                {
                    Console.ForegroundColor = ConsoleColor.Green;

                    if (Import.Variables.activePromptStyle == "Linux") Console.Write($"\n{Import.Variables.userName}:~$ ");
                    else if (Import.Variables.activePromptStyle == "Minimal") Console.Write($"\n> ");
                    else if (Import.Variables.activePromptStyle == "Hacker")
                    {
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                        Console.Write($"\nroot@sys# ");
                    }
                    else
                    {
                        // Dynamic prompt using your earned shop title/rank!
                        Import.DatabaseRecord? profile = GetCurrentUser();
                        string rankLabel = (profile != null && profile.CurrentRank != "User") ? $"[{profile.CurrentRank}] " : "";
                        Console.Write($"\n{rankLabel}{Import.Variables.userName}@SurfOS> ");
                    }

                    Screen_Print.ResetColors();

                    string input = RetroConsole.ReadShellInput();
                    ExecuteCommand(input, ref isRunning, false); // Route inputs through main process router
                }
            }
            catch (Exception ex)
            {
                KernelPanic.ShowAndHandle(
                    ex,
                    "src/SurfOS.Features/Shell/CliEngine.cs",
                    "Reboot SurfOS. If the shell keeps crashing, boot Safe Mode and run dmesg errors.");
            }
        }

        internal static void PrintStartupInfoIfNoTheme()
        {
            if (string.IsNullOrEmpty(Import.Variables.defaultTheme))
            {
                ShowSystemInfo(["info"], 0);
            }
        }

        //  NEW: Process Router lets the normal prompt AND script engines run commands identically!
        private static void ExecuteSimpleCommand(string[] commandParts, ref bool isRunning, bool isScriptExecution)
        {
            if (commandParts.Length == 0) return;

            // Accept the common compact spelling of "cd .." as a parent-directory shortcut.
            if (commandParts[0].Equals("cd..", StringComparison.OrdinalIgnoreCase))
            {
                commandParts = ["cd", "..", .. commandParts[1..]];
            }

            int cmdIndex = 0;
            bool isForced = commandParts[^1].Equals("--force", StringComparison.OrdinalIgnoreCase);
            if (isForced)
            {
                commandParts = commandParts[..^1];
                if (commandParts.Length == 0)
                {
                    Console.WriteLine("usage: <command> --force");
                    return;
                }
                if (!AccountSecurity.IsAdministrator(GetCurrentUser()))
                {
                    Console.WriteLine("--force: the current profile is not an administrator.");
                    LastExitCode = 1;
                    return;
                }
            }

            string mainCommand = commandParts[cmdIndex].ToLowerInvariant();
            if (commandParts.Length == cmdIndex + 2 &&
                (commandParts[cmdIndex + 1] == "--help" ||
                 (commandParts[cmdIndex + 1] == "-h" && mainCommand is not ("du" or "df"))))
            {
                PrintCommandUsage(mainCommand);
                LastExitCode = 0;
                return;
            }

            if (TryExecuteAdditional(mainCommand, commandParts[1..], isForced, ref isRunning)) return;

            bool usesFullScreenApp = UsesFullScreenApp(
                mainCommand,
                commandParts,
                cmdIndex,
                isForced,
                isScriptExecution);
            using IDisposable? alternateScreen = usesFullScreenApp
                ? RetroConsole.EnterAlternateScreen()
                : null;
            using IDisposable? commandOutputAnimation =
                UsesInteractiveScreen(mainCommand)
                    ? null
                    : RetroConsole.BeginCommandOutput();
            using ProcessHandle? commandProcess = ShouldTrackCommand(mainCommand)
                ? ProcessManager.StartProcess(
                    GetProcessName(mainCommand),
                    EstimateProcessMemory(mainCommand),
                    supportsKill: SupportsSafeKill(mainCommand))
                : null;

            KernelLog.Info("command", $"execute '{mainCommand}' as {Import.Variables.userName}");
            try
            {
                switch (mainCommand)
                {
                    case "pwd":
                        if (commandParts.Length != cmdIndex + 1)
                        {
                            PrintCommandUsage("pwd");
                            break;
                        }
                        Console.WriteLine(VirtualFileSystem.CurrentDirectory);
                        break;

                    case "ls":
                        ListVirtualDirectory(commandParts, cmdIndex);
                        break;

                    case "tree":
                    case "find":
                    case "stat":
                    case "du":
                    case "df":
                        InspectFilesystem(mainCommand, commandParts[(cmdIndex + 1)..]);
                        break;

                    case "cd":
                        ChangeVirtualDirectory(commandParts, cmdIndex);
                        break;
                        
                    case "mkdir":
                        CreateVirtualDirectory(commandParts, cmdIndex);
                        break;

                    case "touch":
                        TouchVirtualFile(commandParts, cmdIndex);
                        break;

                    case "cat":
                        ReadVirtualFile(commandParts, cmdIndex);
                        break;

                    case "echo":
                        Echo(commandParts, cmdIndex);
                        break;

                    case "head":
                        ReadFileLines(commandParts, cmdIndex, fromEnd: false);
                        break;

                    case "tail":
                        ReadFileLines(commandParts, cmdIndex, fromEnd: true);
                        break;

                    case "grep":
                        SearchVirtualFile(commandParts, cmdIndex);
                        break;

                    case "wc":
                        CountVirtualFile(commandParts, cmdIndex);
                        break;

                    case "rm":
                        RemoveVirtualFile(commandParts, cmdIndex);
                        break;

                    case "rmdir":
                        RemoveVirtualDirectory(commandParts, cmdIndex);
                        break;

                    case "backup":
                        ManageBackups(commandParts, cmdIndex);
                        break;

                    case "cp":
                        CopyVirtualPath(commandParts, cmdIndex);
                        break;

                    case "mv":
                        MoveVirtualPath(commandParts, cmdIndex);
                        break;

                    case "ps":
                        ShowProcesses(commandParts, cmdIndex);
                        break;

                    case "top":
                        ShowTop();
                        break;

                    case "kill":
                        KillProcess(commandParts, cmdIndex);
                        break;

                    case "help":
                        ShowHelp(commandParts, cmdIndex);
                        break;

                    case "surfai":
                    case "ai":
                        SurfAI.HandleCommand(commandParts, cmdIndex);
                        break;

                    case "clear":
                        ClearScreen(commandParts, cmdIndex);
                        break;

                    case "cleart":
                        if (commandParts.Length > cmdIndex + 1 &&
                            int.TryParse(commandParts[cmdIndex + 1], out int clearDelay) &&
                            clearDelay >= 0)
                        {
                            Console.Clear();
                            DrawHeader();
                            Thread.Sleep(TimeSpan.FromSeconds(clearDelay));
                            Console.Clear();
                            Screen_Print.Print_Selected_Package();
                        }
                        else ShellError("Usage: clearT <time>", 2);
                        break;

                    case "fontsize":
                        if (commandParts.Length > cmdIndex + 1 && short.TryParse(commandParts[cmdIndex + 1], out short newSize))
                        {
                            Core_Engine.ChangeFontSize(newSize);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($" Font size successfully changed to {newSize}.");
                        }
                        else PrintCommandUsage("fontsize");
                        break;

                    case "info":
                        ShowSystemInfo(commandParts, cmdIndex);
                        break;

                    case "anim":
                    case "animations":
                        ConfigureAnimations(commandParts, cmdIndex);
                        break;

                    case "sys":
                        RunSysMonitor(commandParts, cmdIndex);
                        break;

                    case "free":
                        ShowMemoryUsage(commandParts, cmdIndex);
                        break;

                    case "swapon":
                        ShowSwapDevice(commandParts, cmdIndex);
                        break;

                    case "swap":
                        ManageSwap(commandParts, cmdIndex);
                        break;

                    case "store":
                    case "shop":
                        SurfStore.Open();
                        break;

                    case "game":
                        ManageGames(commandParts, cmdIndex, ref isRunning, isScriptExecution);
                        break;

                    case "calc":
                    case "calculator":
                        Calculator(commandParts, cmdIndex);
                        break;

                    case "mail":
                        RunMailSystem(commandParts, cmdIndex);
                        break;

                    case "music":
                        MusicPlayer.HandleCommand(commandParts, cmdIndex);
                        break;

                    case "run":
                        RunScriptCommand(commandParts, cmdIndex, ref isRunning);
                        break;

                    case "clock":
                        Time_Manager.ShowClock(commandParts, cmdIndex);
                        break;

                    case "calendar":
                        Time_Manager.ShowCalendar(commandParts, cmdIndex);
                        break;

                    case "alarm":
                        Time_Manager.ManageAlarm(commandParts, cmdIndex);
                        break;

                    case "service":
                        ManageServices(commandParts, cmdIndex);
                        break;

                    case "surf":
                    case "surfos":
                        Package_Manager.ManageSurfCommand(commandParts, cmdIndex);
                        break;

                    case "dmesg":
                        ShowKernelLog(commandParts, cmdIndex);
                        break;

                    case "whoami":
                        Import.DatabaseRecord? currentProfile = GetCurrentUser();
                        if (commandParts.Length > cmdIndex + 1 &&
                            commandParts[cmdIndex + 1] == "-u")
                        {
                            Console.WriteLine(Import.Variables.userName);
                            break;
                        }
                        if (commandParts.Length > cmdIndex + 1 &&
                            commandParts[cmdIndex + 1] == "--id")
                        {
                            Console.WriteLine(Import.Variables.uuid);
                            break;
                        }
                        if (commandParts.Length != cmdIndex + 1)
                        {
                            PrintCommandUsage("whoami");
                            break;
                        }
                        Console.WriteLine($"Current User: {Import.Variables.userName}");
                        Console.WriteLine($"Rank/Status : {currentProfile?.CurrentRank ?? "User"}");
                        Console.WriteLine(
                            $"Permission  : {(AccountSecurity.IsAdministrator(currentProfile) ? "Administrator" : "User")} ({Import.Variables.uuid})");
                        if (isForced) Console.WriteLine("Force status: GRANTED ");
                        break;

                    case "ping":
                        RunPing(commandParts, cmdIndex);
                        break;

                    case "todo":
                        ManageTodo(commandParts, cmdIndex);
                        break;

                    case "edit":
                        if (commandParts.Length > cmdIndex + 1) RunTextEditor(commandParts[cmdIndex + 1]);
                        else PrintCommandUsage("edit");
                        break;

                    case "vim":
                        if (commandParts.Length == cmdIndex + 2) VimEditor.EditVirtualFile(commandParts[cmdIndex + 1]);
                        else PrintCommandUsage("vim");
                        break;

                    case "code":
                    case "ide":
                        if (!CloudRepositoryManager.IsPackageInstalled("surfcode-ide"))
                        {
                            Console.WriteLine("SurfCode IDE is a SurfCloud app.");
                            Console.WriteLine("Install it with: surf install surfcode-ide");
                            Console.WriteLine("Or open the catalog with: surf store");
                            break;
                        }

                        CodeEditor.Launch(commandParts.Length > cmdIndex + 1 ? commandParts[cmdIndex + 1] : null);
                        break;

                    case "builder":
                    case "package-builder":
                        PackageBuilder.HandleCommand(commandParts, cmdIndex);
                        break;

                    case "user":
                        {
                            string action = commandParts.Length > cmdIndex + 1
                                ? commandParts[cmdIndex + 1].ToLowerInvariant()
                                : "list";

                            if (action == "add")
                            {
                                if (commandParts.Length > cmdIndex + 3)
                                {
                                    if (!isForced)
                                    {
                                        Console.WriteLine(" You must append '--force' to add new users!");
                                        break;
                                    }

                                    string newName = commandParts[cmdIndex + 2];
                                    string newPass = commandParts[cmdIndex + 3];

                                    if (!PathSafety.IsSafeFileName(newName) ||
                                        Recovery_Manager.IsRecoveryUsername(newName))
                                    {
                                        ShellError("Usernames cannot be reserved names or contain path symbols.");
                                    }
                                    else if (string.IsNullOrWhiteSpace(newPass) || newPass.Length < 4)
                                    {
                                        Console.WriteLine("Passwords must contain at least 4 characters.");
                                    }
                                    else if (FindUser(newName) is not null)
                                    {
                                        Console.WriteLine($"User '{newName}' already exists.");
                                    }
                                    else
                                    {
                                        int nextId = Import.Variables.userDatabase.Count > 0
                                            ? checked(Import.Variables.userDatabase.Max(user => user.ID) + 1)
                                            : 1;
                                        Import.DatabaseRecord newAccount = new()
                                        {
                                            ID = nextId,
                                            Username = newName,
                                            Admin = "User",
                                            CurrentRank = "User",
                                            Mailbox = new List<Import.MailMessage>()
                                        };
                                        AccountSecurity.SetPassword(newAccount, newPass);
                                        Import.Variables.userDatabase.Add(newAccount);

                                        SaveUserDatabase();
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine($" New user '{newName}' created successfully!");
                                    }
                                }
                                else
                                {
                                    PrintCommandUsage("user");
                                }
                            }
                            else if (action == "remove")
                            {
                                if (commandParts.Length > cmdIndex + 2)
                                {
                                    if (!isForced)
                                    {
                                        Console.WriteLine(" You must append '--force' to remove users!");
                                        break;
                                    }

                                    string targetName = commandParts[cmdIndex + 2];
                                    if (targetName.Equals(
                                        Import.Variables.userName,
                                        StringComparison.OrdinalIgnoreCase))
                                    {
                                        ShellError(" Safety Lock: You cannot delete your own account while logged into it!");
                                    }
                                    else
                                    {
                                        int removed = Import.Variables.userDatabase.RemoveAll(
                                            user => user.Username.Equals(
                                                targetName,
                                                StringComparison.OrdinalIgnoreCase));
                                        if (removed > 0)
                                        {
                                            SaveUserDatabase();
                                            Console.ForegroundColor = ConsoleColor.Green;
                                            Console.WriteLine($" User '{targetName}' removed!");
                                        }
                                        else
                                        {
                                            ShellError("User not found.");
                                        }
                                    }
                                }
                                else
                                {
                                    PrintCommandUsage("user");
                                }
                            }
                            else if (action == "list")
                            {
                                Console.WriteLine("\n--- Registered SurfOS Users ---");
                                if (Import.Variables.userDatabase == null || Import.Variables.userDatabase.Count == 0)
                                {
                                    Console.WriteLine("No registered profiles found in storage banks.");
                                }
                                else
                                {
                                    foreach (var u in Import.Variables.userDatabase)
                                    {
                                        Console.WriteLine($"- {u.Username} (ID: {u.ID})");
                                    }
                                }
                            }
                            else
                            {
                                PrintCommandUsage("user");
                            }
                        }
                        break;

                    case "theme":
                        if (commandParts.Length > cmdIndex + 1)
                        {
                            string action = commandParts[cmdIndex + 1].ToLowerInvariant();
                            if (action == "load" && commandParts.Length > cmdIndex + 2)
                            {
                                Screen_Print.LoadAndApplyTheme(commandParts[cmdIndex + 2], isForced);
                            }
                            else if (action == "unload" && commandParts.Length == cmdIndex + 2)
                            {
                                string optionsFile = Path.Combine(Import.Variables.installPath, "options.json");
                                if (File.Exists(optionsFile))
                                {
                                    Import.SystemOptions options = JsonStorage.Read<Import.SystemOptions>(optionsFile)
                                        ?? throw new IOException("Could not read system options.");
                                    options.DefaultTheme = string.Empty;
                                    JsonStorage.Write(optionsFile, options);
                                }
                                Import.Variables.defaultTheme = string.Empty;
                                Screen_Print.ResetToDefaultOSTheme();
                                Console.WriteLine("Theme unloaded. Default OS appearance restored.");
                            }
                            else if (action == "remove" && commandParts.Length > cmdIndex + 2)
                            {
                                string targetTheme = commandParts[cmdIndex + 2];
                                if (!PathSafety.IsSafeFileName(targetTheme))
                                {
                                    ShellError("Invalid theme name.");
                                    break;
                                }
                                string packagePath = Path.Combine(Import.Variables.installPath, "Packages", $"{targetTheme}.json");
                                if (File.Exists(packagePath))
                                {
                                    if ((targetTheme == "HolySurf" || targetTheme == "UnHolySurf") && !isForced)
                                        ShellError(" Core system themes cannot be deleted without '--force'!");
                                    else
                                    {
                                        File.Delete(packagePath);
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine($" Theme '{targetTheme}' was successfully removed!");
                                    }
                                }
                                else ShellError($"[SurfOS] Theme pack '{targetTheme}' was not found.");
                            }
                            else if (action == "default" && commandParts.Length > cmdIndex + 2)
                            {
                                string targetTheme = commandParts[cmdIndex + 2];
                                if (!PathSafety.IsSafeFileName(targetTheme))
                                {
                                    ShellError("Invalid theme name.");
                                    break;
                                }
                                string packagePath = Path.Combine(Import.Variables.installPath, "Packages", $"{targetTheme}.json");
                                if (File.Exists(packagePath))
                                {
                                    Import.Variables.defaultTheme = targetTheme;
                                    string optionsFile = Path.Combine(Import.Variables.installPath, "options.json");
                                    if (File.Exists(optionsFile))
                                    {
                                        Import.SystemOptions? options =
                                            JsonStorage.Read<Import.SystemOptions>(optionsFile);
                                        if (options != null)
                                        {
                                            options.DefaultTheme = targetTheme;
                                            JsonStorage.Write(optionsFile, options);
                                        }
                                    }
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"'{targetTheme}' is now your default boot theme.");
                                    Screen_Print.ResetColors();
                                }
                                else Console.WriteLine($"[SurfOS] Theme pack '{targetTheme}' does not exist.");
                            }
                            else if (action == "link")
                            {
                                Console.WriteLine("https://surfos.netlify.app/");
                            }
                            else if (action == "list")
                            {
                                string themesPath = Path.Combine(
                                    Import.Variables.installPath,
                                    "Packages");
                                string[] themes = Directory.Exists(themesPath)
                                    ? Directory.GetFiles(themesPath, "*.json")
                                        .Select(Path.GetFileNameWithoutExtension)
                                        .Where(name => !string.IsNullOrWhiteSpace(name))
                                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                                        .Select(name => name!)
                                        .ToArray()
                                    : [];
                                Console.WriteLine(themes.Length == 0
                                    ? "No installed themes."
                                    : string.Join(Environment.NewLine, themes));
                            }
                            else
                            {
                                PrintCommandUsage("theme");
                            }
                        }

                        else
                        {
                            PrintCommandUsage("theme");
                        }
                        break;

                    case "uninstall":
                        EngageUninstallSequence(commandParts, cmdIndex, isForced, isScriptExecution);
                        break;

                    case "logout":
                        KernelLog.Info("session", $"logout requested by {Import.Variables.userName}");
                        ProcessManager.StopAllUserProcesses();
                        ServiceManager.StopAll();
                        isRunning = false;
                        break;

                    case "exit":
                    case "shutdown":
                        KernelLog.Warning("session", $"shutdown requested by {Import.Variables.userName}");
                        ProcessManager.StopAllUserProcesses();
                        ServiceManager.StopAll();
                        RetroConsole.ShutdownSequence();
                        Environment.Exit(0);
                        break;

                    default:
                        LastExitCode = 127;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Command not recognized: '{mainCommand}'. Type 'help' for a list of commands.");
                        KernelLog.Error("command", $"command not recognized: {mainCommand}");
                        break;
                }
            }
            catch (Exception ex)
            {
                LastExitCode = 1;
                KernelLog.Error("crash", $"command '{mainCommand}' crashed: {ex}");
                ShellError($"{mainCommand}: {ex.Message}");
            }

            Screen_Print.ResetColors();
        }

        private static bool TryTokenizeCommand(
            string input,
            out string[] tokens,
            out string error)
        {
            List<string> parsed = [];
            System.Text.StringBuilder current = new();
            char quote = '\0';
            bool escaping = false;

            foreach (char character in input)
            {
                if (escaping)
                {
                    current.Append(character);
                    escaping = false;
                    continue;
                }

                if (character == '\\' && quote != '\'')
                {
                    escaping = true;
                    continue;
                }

                if (quote != '\0')
                {
                    if (character == quote)
                    {
                        quote = '\0';
                    }
                    else
                    {
                        current.Append(character);
                    }

                    continue;
                }

                if (character is '"' or '\'')
                {
                    quote = character;
                }
                else if (char.IsWhiteSpace(character))
                {
                    if (current.Length > 0)
                    {
                        parsed.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(character);
                }
            }

            if (escaping)
            {
                current.Append('\\');
            }

            if (quote != '\0')
            {
                tokens = [];
                error = $"unclosed {quote} quote";
                return false;
            }

            if (current.Length > 0)
            {
                parsed.Add(current.ToString());
            }

            tokens = parsed.ToArray();
            error = string.Empty;
            return true;
        }

        private static void ListVirtualDirectory(string[] args, int cmdIndex)
        {
            bool showAll = false;
            bool longFormat = false;
            bool humanReadable = false;
            string path = string.Empty;

            foreach (string argument in args.Skip(cmdIndex + 1))
            {
                if (argument.StartsWith('-') && argument.Length > 1)
                {
                    foreach (char option in argument[1..])
                    {
                        switch (option)
                        {
                            case 'a': showAll = true; break;
                            case 'l': longFormat = true; break;
                            case 'h': humanReadable = true; break;
                            default:
                                ShellError($"ls: invalid option -- '{option}'");
                                ShellError("Usage: ls [-alh] [path]", 2);
                                return;
                        }
                    }
                }
                else if (string.IsNullOrEmpty(path))
                {
                    path = argument;
                }
                else
                {
                    ShellError("Usage: ls [-alh] [path]", 2);
                    return;
                }
            }

            RunVfsCommand(() =>
            {
                IReadOnlyList<VirtualDirectoryEntry> entries =
                    VirtualFileSystem.List(path, out string error);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    ShellError(error);
                    return;
                }

                foreach (VirtualDirectoryEntry entry in entries
                    .Where(entry => showAll || !entry.Name.StartsWith('.')))
                {
                    if (!longFormat)
                    {
                        Console.WriteLine(entry.Name + (entry.Type == "dir" ? "/" : ""));
                        continue;
                    }

                    string listedPath = string.IsNullOrWhiteSpace(path) ? VirtualFileSystem.CurrentDirectory : path;
                    VirtualPathInfo listedInfo = VirtualFileSystem.Inspect(listedPath);
                    string entryPath = listedInfo.IsDirectory
                        ? listedPath.TrimEnd('/', '\\') + "/" + entry.Name
                        : listedPath;
                    var metadata = VirtualFileSystem.GetMetadata(entryPath);
                    string permissions = VirtualFileSystem.FormatMode(entry.Type == "dir", metadata.Mode);
                    string size = humanReadable ? FormatFileSize(entry.Size) : entry.Size.ToString();
                    string modified = entry.Modified == default
                        ? "------------"
                        : entry.Modified.ToString("MMM dd HH:mm");
                    Console.WriteLine($"{permissions}  {metadata.Owner,-12}  {size,8}  {modified}  {entry.Name}");
                }
            });
        }

        private static void ChangeVirtualDirectory(string[] args, int cmdIndex)
        {
            string path = args.Length > cmdIndex + 1 ? args[cmdIndex + 1] : "~";
            RunVfsCommand(() =>
            {
                if (!VirtualFileSystem.ChangeDirectory(path, out string error))
                {
                    ShellError(error);
                }
            });
        }

        private static void CreateVirtualDirectory(string[] args, int cmdIndex)
        {
            string[] paths = args.Skip(cmdIndex + 1)
                .Where(argument => !argument.Equals("-p", StringComparison.Ordinal))
                .ToArray();
            bool invalidOption = args.Skip(cmdIndex + 1)
                .Any(argument => argument.StartsWith('-') && argument != "-p");

            if (paths.Length == 0 || invalidOption)
            {
                ShellError("Usage: mkdir [-p] <directory>...", 2);
                return;
            }

            RunVfsCommand(() =>
            {
                foreach (string path in paths)
                {
                    VirtualFileSystem.CreateDirectory(path, out string error);
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        ShellError(error);
                    }
                }
            });
        }

        private static void TouchVirtualFile(string[] args, int cmdIndex)
        {
            if (args.Length <= cmdIndex + 1)
            {
                ShellError("Usage: touch <file>...", 2);
                return;
            }

            RunVfsCommand(() =>
            {
                foreach (string path in args.Skip(cmdIndex + 1))
                {
                    VirtualFileSystem.Touch(path, out string error);
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        ShellError(error);
                    }
                }
            });
        }

        private static void ReadVirtualFile(string[] args, int cmdIndex)
        {
            bool numberLines = args.Skip(cmdIndex + 1).Any(argument => argument is "-n" or "--number");
            string[] paths = args.Skip(cmdIndex + 1)
                .Where(argument => argument is not "-n" and not "--number")
                .ToArray();
            if (paths.Length == 0)
            {
                if (ShellInput != null) paths = ["-"];
                else { ShellError("Usage: cat [-n] <file>...", 2); return; }
            }

            RunVfsCommand(() =>
            {
                foreach (string path in paths)
                {
                    if (!ReadCommandFile(path, out string content, out string error))
                    {
                        ShellError(error);
                        continue;
                    }

                    if (!numberLines)
                    {
                        Console.Write(content);
                        continue;
                    }

                    string[] lines = content.Replace("\r\n", "\n").Split('\n');
                    for (int index = 0; index < lines.Length; index++)
                    {
                        Console.WriteLine($"{index + 1,6}  {lines[index]}");
                    }
                }
            });
        }

        private static void RemoveVirtualFile(string[] args, int cmdIndex)
        {
            bool recursive = false;
            bool force = false;
            List<string> paths = [];

            foreach (string argument in args.Skip(cmdIndex + 1))
            {
                if (argument.StartsWith('-') && argument.Length > 1)
                {
                    foreach (char option in argument[1..])
                    {
                        if (option is 'r' or 'R') recursive = true;
                        else if (option == 'f') force = true;
                        else
                        {
                            ShellError($"rm: invalid option -- '{option}'");
                            ShellError("Usage: rm [-rf] <path>...", 2);
                            return;
                        }
                    }
                }
                else
                {
                    paths.Add(argument);
                }
            }

            if (paths.Count == 0)
            {
                ShellError("Usage: rm [-rf] <path>...", 2);
                return;
            }

            RunVfsCommand(() =>
            {
                foreach (string path in paths)
                {
                    bool removed = VirtualFileSystem.RemoveFile(path, out string error);
                    if (!removed && recursive &&
                        error.StartsWith("rm only removes files", StringComparison.Ordinal))
                    {
                        removed = VirtualFileSystem.RemoveDirectory(path, recursive: true, out error);
                    }

                    if (!removed && !(force &&
                        (error.StartsWith("File not found:", StringComparison.Ordinal) ||
                         error.StartsWith("Directory not found:", StringComparison.Ordinal))))
                    {
                        ShellError(error);
                    }
                }
            });
        }

        private static void RemoveVirtualDirectory(string[] args, int cmdIndex)
        {
            if (args.Length <= cmdIndex + 1)
            {
                ShellError("Usage: rmdir <directory>", 2);
                return;
            }
            RunVfsCommand(() =>
            {
                if (!VirtualFileSystem.RemoveDirectory(args[cmdIndex + 1], out string error))
                {
                    ShellError(error);
                }
            });
        }

        private static void ManageBackups(string[] args, int cmdIndex)
        {
            if (args.Length <= cmdIndex + 1)
            {
                ShellError("Usage: backup <name> | backup list", 2);
                return;
            }

            if (args[cmdIndex + 1].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<BackupInfo> backups = Backup_Manager.ListBackups();
                Console.WriteLine("\n--- SurfOS Recovery Backups ---");
                if (backups.Count == 0)
                {
                    Console.WriteLine("No backups were found in preVersions.");
                    return;
                }

                foreach (BackupInfo backup in backups)
                {
                    Console.WriteLine(
                        $"{backup.Name,-28} {Backup_Manager.FormatSize(backup.Size),10}  {backup.Created:g}");
                }

                return;
            }

            string backupName = string.Join(" ", args.Skip(cmdIndex + 1));
            Console.WriteLine($"Creating partition backup '{backupName}'...");
            bool success = Backup_Manager.CreateBackup(backupName, out string message);
            Console.ForegroundColor = success ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(message);
            Screen_Print.ResetColors();
        }

        private static void CopyVirtualPath(string[] args, int cmdIndex)
        {
            bool recursive = false;
            bool overwrite = false;
            List<string> operands = [];
            foreach (string argument in args.Skip(cmdIndex + 1))
            {
                if (argument.StartsWith('-') && argument.Length > 1)
                {
                    foreach (char option in argument[1..])
                    {
                        if (option is 'r' or 'R') recursive = true;
                        else if (option == 'f') overwrite = true;
                        else
                        {
                            ShellError($"cp: invalid option -- '{option}'");
                            ShellError("Usage: cp [-rf] <source> <destination>", 2);
                            return;
                        }
                    }
                }
                else
                {
                    operands.Add(argument);
                }
            }

            if (operands.Count != 2)
            {
                ShellError("Usage: cp [-rf] <source> <destination>", 2);
                return;
            }

            RunVfsCommand(() =>
            {
                if (!VirtualFileSystem.Copy(
                    operands[0],
                    operands[1],
                    recursive,
                    overwrite,
                    out string error))
                {
                    ShellError(error);
                }
            });
        }

        private static void MoveVirtualPath(string[] args, int cmdIndex)
        {
            bool overwrite = false;
            List<string> operands = [];
            foreach (string argument in args.Skip(cmdIndex + 1))
            {
                if (argument == "-f")
                {
                    overwrite = true;
                }
                else if (argument.StartsWith('-'))
                {
                    ShellError($"mv: invalid option -- '{argument}'");
                    ShellError("Usage: mv [-f] <source> <destination>", 2);
                    return;
                }
                else
                {
                    operands.Add(argument);
                }
            }

            if (operands.Count != 2)
            {
                ShellError("Usage: mv [-f] <source> <destination>", 2);
                return;
            }

            RunVfsCommand(() =>
            {
                if (!VirtualFileSystem.Move(
                    operands[0],
                    operands[1],
                    overwrite,
                    out string error))
                {
                    ShellError(error);
                }
            });
        }

        private static void Echo(string[] args, int cmdIndex)
        {
            Console.WriteLine(string.Join(" ", args.Skip(cmdIndex + 1)));
        }

        private static void ReadFileLines(string[] args, int cmdIndex, bool fromEnd)
        {
            int count = 10;
            string? path = null;
            string command = fromEnd ? "tail" : "head";
            for (int index = cmdIndex + 1; index < args.Length; index++)
            {
                if (args[index] == "-n" && index + 1 < args.Length &&
                    int.TryParse(args[++index], out int requested) && requested >= 0)
                {
                    count = requested;
                }
                else if (path is null)
                {
                    path = args[index];
                }
                else
                {
                    ShellError($"Usage: {command} [-n count] <file>", 2);
                    return;
                }
            }

            if (path is null && ShellInput != null) path = "-";
            if (path is null)
            {
                ShellError($"Usage: {command} [-n count] <file>", 2);
                return;
            }

            RunVfsCommand(() =>
            {
                if (!ReadCommandFile(path, out string content, out string error))
                {
                    ShellError(error);
                    return;
                }

                string[] lines = SplitTextLines(content);
                IEnumerable<string> selected = fromEnd
                    ? lines.TakeLast(count)
                    : lines.Take(count);
                Console.WriteLine(string.Join(Environment.NewLine, selected));
            });
        }

        private static void SearchVirtualFile(string[] args, int cmdIndex)
        {
            bool ignoreCase = false;
            bool numberLines = false;
            List<string> operands = [];
            foreach (string argument in args.Skip(cmdIndex + 1))
            {
                if (argument.StartsWith('-') && argument.Length > 1)
                {
                    foreach (char option in argument[1..])
                    {
                        if (option == 'i') ignoreCase = true;
                        else if (option == 'n') numberLines = true;
                        else
                        {
                            ShellError($"grep: invalid option -- '{option}'");
                            ShellError("Usage: grep [-in] <pattern> <file>", 2);
                            return;
                        }
                    }
                }
                else
                {
                    operands.Add(argument);
                }
            }

            if (operands.Count == 1 && ShellInput != null) operands.Add("-");
            if (operands.Count != 2)
            {
                ShellError("Usage: grep [-in] <pattern> <file>", 2);
                return;
            }

            RunVfsCommand(() =>
            {
                if (!ReadCommandFile(operands[1], out string content, out string error))
                {
                    ShellError(error);
                    return;
                }

                StringComparison comparison = ignoreCase
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                string[] lines = SplitTextLines(content);
                bool matched = false;
                for (int index = 0; index < lines.Length; index++)
                {
                    if (lines[index].Contains(operands[0], comparison))
                    {
                        matched = true;
                        Console.WriteLine(numberLines
                            ? $"{index + 1}:{lines[index]}"
                            : lines[index]);
                    }
                }
                if (!matched) LastExitCode = 1;
            });
        }

        private static void CountVirtualFile(string[] args, int cmdIndex)
        {
            bool linesOnly = false;
            bool wordsOnly = false;
            bool bytesOnly = false;
            string? path = null;
            foreach (string argument in args.Skip(cmdIndex + 1))
            {
                if (argument.StartsWith('-') && argument.Length > 1)
                {
                    foreach (char option in argument[1..])
                    {
                        if (option == 'l') linesOnly = true;
                        else if (option == 'w') wordsOnly = true;
                        else if (option == 'c') bytesOnly = true;
                        else
                        {
                            ShellError($"wc: invalid option -- '{option}'");
                            ShellError("Usage: wc [-lwc] <file>", 2);
                            return;
                        }
                    }
                }
                else if (path is null)
                {
                    path = argument;
                }
                else
                {
                    ShellError("Usage: wc [-lwc] <file>", 2);
                    return;
                }
            }

            if (path is null && ShellInput != null) path = "-";
            if (path is null)
            {
                ShellError("Usage: wc [-lwc] <file>", 2);
                return;
            }

            RunVfsCommand(() =>
            {
                if (!ReadCommandFile(path, out string content, out string error))
                {
                    ShellError(error);
                    return;
                }

                int lineCount = content.Count(character => character == '\n');
                if (content.Length > 0 && content[^1] != '\n')
                {
                    lineCount++;
                }
                int wordCount = content.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries).Length;
                int byteCount = System.Text.Encoding.UTF8.GetByteCount(content);
                bool noSelection = !linesOnly && !wordsOnly && !bytesOnly;
                List<string> counts = [];
                if (linesOnly || noSelection) counts.Add(lineCount.ToString());
                if (wordsOnly || noSelection) counts.Add(wordCount.ToString());
                if (bytesOnly || noSelection) counts.Add(byteCount.ToString());
                Console.WriteLine($"{string.Join(" ", counts)} {path}");
            });
        }

        private static string FormatFileSize(long bytes)
        {
            string[] units = ["B", "K", "M", "G", "T"];
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return unit == 0 ? $"{bytes}B" : $"{size:0.#}{units[unit]}";
        }

        private static string[] SplitTextLines(string content)
        {
            if (content.Length == 0)
            {
                return [];
            }

            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            return lines.Length > 0 && lines[^1].Length == 0
                ? lines[..^1]
                : lines;
        }

        private static void ClearScreen(string[] args, int cmdIndex)
        {
            if (args.Length == cmdIndex + 1)
            {
                Console.Clear();
                Screen_Print.Print_Selected_Package();
                return;
            }

            if (args.Length == cmdIndex + 3 &&
                args[cmdIndex + 1] is "-d" or "--delay" &&
                int.TryParse(args[cmdIndex + 2], out int delay) &&
                delay >= 0)
            {
                Console.Clear();
                DrawHeader();
                Thread.Sleep(TimeSpan.FromSeconds(delay));
                Console.Clear();
                Screen_Print.Print_Selected_Package();
                return;
            }

            PrintCommandUsage("clear");
        }

        private static void RunPing(string[] args, int cmdIndex)
        {
            int count = 1;
            int timeout = 2000;
            string? host = null;
            for (int index = cmdIndex + 1; index < args.Length; index++)
            {
                if (args[index] == "-c" && index + 1 < args.Length &&
                    int.TryParse(args[++index], out int parsedCount) &&
                    parsedCount is >= 1 and <= 20)
                {
                    count = parsedCount;
                }
                else if (args[index] == "-W" && index + 1 < args.Length &&
                         int.TryParse(args[++index], out int parsedTimeout) &&
                         parsedTimeout is >= 100 and <= 30000)
                {
                    timeout = parsedTimeout;
                }
                else if (!args[index].StartsWith('-') && host is null)
                {
                    host = args[index];
                }
                else
                {
                    PrintCommandUsage("ping");
                    return;
                }
            }

            if (host is null)
            {
                PrintCommandUsage("ping");
                return;
            }

            try
            {
                RequireNetwork();
                using Ping pingSender = new();
                Console.WriteLine($"PING {host}: 32 data bytes, {count} probe(s)");
                int received = 0;
                long totalTime = 0;
                for (int sequence = 1; sequence <= count; sequence++)
                {
                    PingReply reply = pingSender.Send(host, timeout);
                    if (reply.Status == IPStatus.Success)
                    {
                        received++;
                        totalTime += reply.RoundtripTime;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine(
                            $"32 bytes from {reply.Address}: seq={sequence} time={reply.RoundtripTime}ms");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"probe {sequence}: {reply.Status}");
                    }
                    Screen_Print.ResetColors();
                }

                int loss = (count - received) * 100 / count;
                if (received == 0) LastExitCode = 1;
                Console.WriteLine(
                    $"--- {host} ping statistics ---\n{count} sent, {received} received, {loss}% loss" +
                    (received > 0 ? $", avg {totalTime / received}ms" : ""));
            }
            catch (Exception ex)
            {
                ShellError($"Ping error: {ex.Message}");
            }
        }

        private static void RunVfsCommand(Action command)
        {
            try
            {
                command();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                ShellError($"VFS error: {ex.Message}");
            }
        }

        private static void ShowProcesses(string[] args, int cmdIndex)
        {
            bool includeCompleted = args.Skip(cmdIndex + 1).Contains("-a");
            bool runningOnly = args.Skip(cmdIndex + 1).Contains("-r");
            if (args.Skip(cmdIndex + 1).Any(argument => argument is not "-a" and not "-r"))
            {
                PrintCommandUsage("ps");
                return;
            }

            IReadOnlyList<ProcessInfo> processes =
                ProcessManager.ListProcesses(includeCompleted && !runningOnly);
            Console.WriteLine("\n PID   STATUS      MEM(MB)  STARTED   FLAGS       NAME");
            Console.WriteLine("---------------------------------------------------------------");

            foreach (ProcessInfo process in processes)
            {
                Console.WriteLine(FormatProcessRow(process));
            }
        }

        private static void ShowTop()
        {
            ConsoleKey key = ConsoleKey.NoName;
            Console.CursorVisible = false;

            try
            {
                while (key != ConsoleKey.Escape)
                {
                    Console.Clear();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("SurfOS Process Monitor - press ESC to exit");
                    Screen_Print.ResetColors();
                    Console.WriteLine($"Updated: {DateTime.Now:T}\n");
                    Console.WriteLine(" PID   STATUS      MEM(MB)  UPTIME    FLAGS       NAME");
                    Console.WriteLine("---------------------------------------------------------------");

                    foreach (ProcessInfo process in ProcessManager.ListProcesses())
                    {
                        TimeSpan uptime = DateTime.Now - process.StartTime;
                        string flags = FormatProcessFlags(process);
                        Console.WriteLine(
                            $"{process.Pid,-5} {process.Status,-11} {process.MemoryEstimateMb,7}  {uptime:mm\\:ss}   {flags,-10} {process.Name}");
                    }

                    long totalMemory = ProcessManager.ListProcesses().Sum(process => process.MemoryEstimateMb);
                    SwapSnapshot swap = Swap_Manager.GetSnapshot();
                    Console.WriteLine($"\nSimulated process memory: {totalMemory} MB");
                    Console.WriteLine(
                        $"Swap: {Swap_Manager.FormatBytes(swap.UsedBytes)} / " +
                        $"{Swap_Manager.FormatBytes(swap.CapacityBytes)} " +
                        $"({swap.UsedPercent:0}%)");

                    DateTime refreshUntil = DateTime.Now.AddSeconds(1);
                    while (DateTime.Now < refreshUntil)
                    {
                        if (Console.KeyAvailable)
                        {
                            key = Console.ReadKey(intercept: true).Key;
                            break;
                        }

                        Thread.Sleep(50);
                    }
                }
            }
            finally
            {
                Console.CursorVisible = true;
                Console.Clear();
                Screen_Print.Print_Selected_Package();
            }
        }

        private static void KillProcess(string[] args, int cmdIndex)
        {
            int operandIndex = cmdIndex + 1;
            if (args.Length > operandIndex && args[operandIndex] == "-s")
            {
                if (args.Length <= operandIndex + 1 ||
                    !args[operandIndex + 1].Equals("TERM", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("kill: SurfOS only supports the TERM signal.");
                    PrintCommandUsage("kill");
                    return;
                }
                operandIndex += 2;
            }

            if (args.Length != operandIndex + 1 ||
                !int.TryParse(args[operandIndex], out int pid))
            {
                PrintCommandUsage("kill");
                return;
            }

            bool killed = ProcessManager.TryKillProcess(pid, out string message);
            if (!killed) LastExitCode = 1;
            Console.ForegroundColor = killed ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(message);
            Screen_Print.ResetColors();
        }

        private static string FormatProcessRow(ProcessInfo process)
        {
            return $"{process.Pid,-5} {process.Status,-11} {process.MemoryEstimateMb,7}  {process.StartTime:HH:mm:ss}  {FormatProcessFlags(process),-10} {process.Name}";
        }

        private static string FormatProcessFlags(ProcessInfo process)
        {
            if (process.IsProtected)
            {
                return "kernel";
            }

            return process.SupportsKill ? "killable" : "safe";
        }

        private static bool ShouldTrackCommand(string command)
        {
            return command is not ("logout" or "shutdown" or "exit");
        }

        private static bool SupportsSafeKill(string command)
        {
            return command is "top" or "shop" or "mail" or "code" or "ide";
        }

        private static int EstimateProcessMemory(string command)
        {
            return command switch
            {
                "top" => 18,
                "ps" => 8,
                "kill" => 6,
                "pwd" => 4,
                "ls" => 8,
                "cd" => 4,
                "mkdir" or "touch" or "cat" or "rm" or "rmdir" or "cp" or "mv" => 10,
                "backup" => 48,
                "code" or "ide" => 96,
                "shop" or "store" => 24,
                "mail" => 22,
                "music" => 28,
                "sys" => 26,
                "run" => 30,
                "edit" => 34,
                "clock" or "calendar" or "alarm" => 14,
                "service" => 18,
                "dmesg" => 16,
                "free" or "swapon" or "swap" => 6,
                "surf" or "surfos" => 24,
                _ => 12
            };
        }

        private static string GetProcessName(string command)
        {
            return command switch
            {
                "code" or "ide" => "SurfCode IDE",
                "mail" => "Mailbox",
                "music" => "Music Player",
                "shop" or "store" => "Surf Store",
                "sys" => "System Monitor",
                "calc" or "calculator" => "Calculator",
                "ps" => "Process List",
                "top" => "Process Monitor",
                "kill" => "Process Killer",
                "free" => "Memory Report",
                "swapon" or "swap" => "Swap Control",
                "pwd" => "VFS PWD",
                "ls" => "VFS List",
                "cd" => "VFS Change Directory",
                "mkdir" => "VFS Make Directory",
                "touch" => "VFS Touch",
                "cat" => "VFS Cat",
                "rm" => "VFS Remove",
                "rmdir" => "VFS Remove Directory",
                "backup" => "Recovery Backup",
                "cp" => "VFS Copy",
                "mv" => "VFS Move",
                "uninstall" => "Uninstaller",
                "service" => "Service Control",
                "dmesg" => "Kernel Log",
                "surf" or "surfos" => "Surf Package Manager",
                _ => $"Command:{command}"
            };
        }

        private static void ManageServices(string[] args, int cmdIndex)
        {
            string action = args.Length > cmdIndex + 1
                ? args[cmdIndex + 1].ToLowerInvariant()
                : "list";

            if (action == "list")
            {
                Console.WriteLine("\n--- SurfOS Background Services ---");
                foreach (ServiceState service in ServiceManager.ListServices())
                {
                    string enabled = service.Enabled ? "enabled" : "disabled";
                    string autostart = service.AutoStart ? "autostart" : "manual";
                    Console.WriteLine(
                        $"{service.Name,-22} {service.Status,-20} {enabled}, {autostart}");
                }
                return;
            }

            if (args.Length <= cmdIndex + 2)
            {
                PrintCommandUsage("service");
                return;
            }

            string serviceName = args[cmdIndex + 2];
            bool success = action switch
            {
                "start" => ServiceManager.StartService(serviceName),
                "stop" => ServiceManager.StopService(serviceName),
                "restart" => ServiceManager.RestartService(serviceName),
                "status" => ServiceManager.GetStatus(serviceName) is not null,
                _ => false
            };

            if (!success)
            {
                PrintCommandUsage("service");
                return;
            }

            ServiceState? state = ServiceManager.GetStatus(serviceName);
            if (state is null)
            {
                ShellError($"Service '{serviceName}' was not found.");
                return;
            }

            if (action == "status")
            {
                PrintServiceStatus(state);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"{state.Name}: {state.Status}");
                Screen_Print.ResetColors();
            }
        }

        private static void PrintServiceStatus(ServiceState service)
        {
            Console.WriteLine($"\nService    : {service.Name}");
            Console.WriteLine($"Description: {service.Description}");
            Console.WriteLine($"Status     : {service.Status}");
            Console.WriteLine($"Enabled    : {(service.Enabled ? "yes" : "no")}");
            Console.WriteLine($"Autostart  : {(service.AutoStart ? "yes" : "no")}");
            Console.WriteLine($"Started    : {FormatServiceTime(service.StartedAt)}");
            Console.WriteLine($"Heartbeat  : {FormatServiceTime(service.LastHeartbeat)}");

            if (!string.IsNullOrWhiteSpace(service.LastError))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Last error : {service.LastError}");
                Screen_Print.ResetColors();
            }
        }

        private static string FormatServiceTime(DateTime? value)
        {
            return value is null ? "-" : value.Value.ToString("g");
        }

        private static void ShowKernelLog(string[] args, int cmdIndex)
        {
            string action = args.Length > cmdIndex + 1
                ? args[cmdIndex + 1].ToLowerInvariant()
                : "all";
            action = action switch
            {
                "-c" => "clear",
                "-b" => "boot",
                "--errors" => "errors",
                _ => action
            };

            IReadOnlyList<KernelLogEntry> entries;
            switch (action)
            {
                case "all":
                    entries = KernelLog.Recent();
                    break;

                case "clear":
                    KernelLog.Clear();
                    Console.WriteLine("Kernel log cleared.");
                    return;

                case "errors":
                    entries = KernelLog.Recent(KernelLogLevel.Error);
                    break;

                case "boot":
                    entries = KernelLog.Recent(currentBootOnly: true);
                    break;

                default:
                    KernelLog.Warning("command", $"invalid dmesg option: {action}");
                    PrintCommandUsage("dmesg");
                    return;
            }

            if (entries.Count == 0)
            {
                Console.WriteLine("No kernel log entries matched.");
                return;
            }

            foreach (KernelLogEntry entry in entries)
            {
                Console.ForegroundColor = entry.Level switch
                {
                    KernelLogLevel.Success => ConsoleColor.Green,
                    KernelLogLevel.Warning => ConsoleColor.Yellow,
                    KernelLogLevel.Error => ConsoleColor.Red,
                    _ => ConsoleColor.Gray
                };
                Console.WriteLine(entry.Format());
            }

            Screen_Print.ResetColors();
        }

        private static void EngageUninstallSequence(
            string[] args,
            int cmdIndex,
            bool isForced,
            bool isScriptExecution)
        {
            if (args.Length > cmdIndex + 1)
            {
                PrintCommandUsage("uninstall");
                return;
            }

            if (isScriptExecution)
            {
                ShellError("Uninstall cannot be launched from a script.");
                return;
            }

            if (!AccountSecurity.IsAdministrator(GetCurrentUser()))
            {
                ShellError("Uninstall requires an administrator profile.");
                return;
            }

            string installPath = Import.Variables.installPath;
            KernelLog.Warning(
                "uninstall",
                $"requested by {Import.Variables.userName}; force={isForced}");
            if (!IsValidInstallPath(installPath))
            {
                KernelLog.Error("uninstall", $"install path validation failed: {installPath}");
                Console.ForegroundColor = ConsoleColor.Red;
                ShellError("Uninstall aborted: active install path could not be verified.");
                Console.WriteLine($"Path: {installPath}");
                Console.ResetColor();
                return;
            }

            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Red;
            RetroConsole.TypeLine("======================================", 2);
            RetroConsole.TypeLine("        SURFOS UNINSTALL SEQUENCE      ", 3);
            RetroConsole.TypeLine("======================================", 2);
            Console.ResetColor();
            bool isVhdxInstall = Partition_Manager.IsConfiguredVhdxInstall(installPath);
            Console.WriteLine("This will permanently remove:");
            Console.WriteLine($"  {installPath}");
            if (isVhdxInstall)
            {
                Console.WriteLine($"  {Import.Variables.vhdxPath}");
                Console.WriteLine("The SurfOS drive will be detached and its VHDX backing file deleted.");
            }
            Console.WriteLine("\nUser accounts, themes, BIOS settings, mail, and local files in this install will be deleted.");
            if (!isForced)
            {
                Console.Write("\nType UNINSTALL SURFOS to continue: ");

                string confirmation = Console.ReadLine() ?? string.Empty;
                if (!confirmation.Equals("UNINSTALL SURFOS", StringComparison.Ordinal))
                {
                    Console.WriteLine("Uninstall cancelled.");
                    return;
                }

                Console.Write("Final confirmation, press Y to erase SurfOS: ");
                if (Console.ReadKey(intercept: true).Key != ConsoleKey.Y)
                {
                    Console.WriteLine("\nUninstall cancelled.");
                    return;
                }
            }
            else
            {
                Console.WriteLine("\nForce mode enabled; confirmation prompts skipped.");
            }

            Console.WriteLine();
            try
            {
                KernelLog.Warning("uninstall", "removal started");
                ProcessManager.StopAllUserProcesses();
                ServiceManager.StopAll();
                RetroConsole.Spinner("STOPPING SURFOS SERVICES", 250);
                RetroConsole.Spinner("REMOVING SYSTEM FILES", 250);

                if (isVhdxInstall)
                {
                    if (!Partition_Manager.RemoveConfiguredVhdx(installPath, out string removeError))
                    {
                        throw new IOException(removeError);
                    }
                }
                else
                {
                    Directory.Delete(installPath, recursive: true);
                }

                Import.Variables.installPath = string.Empty;
                Import.Variables.vhdxPath = string.Empty;
                Import.Variables.vhdxHostDirectory = string.Empty;
                Import.Variables.userDatabase.Clear();
                Import.Variables.userName = string.Empty;

                Console.ForegroundColor = ConsoleColor.Green;
                RetroConsole.TypeLine("\nSurfOS has been uninstalled from this machine.", 4);
                Console.ResetColor();
                RetroConsole.ShutdownSequence();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                KernelLog.Error("uninstall", ex.ToString());
                Console.ForegroundColor = ConsoleColor.Red;
                ShellError($"\nUninstall failed: {ex.Message}");
                Console.ResetColor();
                Console.WriteLine("Some files may still be present. Close running SurfOS windows and try again.");
            }
        }

        private static bool IsValidInstallPath(string installPath)
        {
            if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            {
                return false;
            }

            string fullPath = Path.GetFullPath(installPath).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string rootPath = (Path.GetPathRoot(fullPath) ?? string.Empty).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            if (fullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return Partition_Manager.IsConfiguredVhdxInstall(installPath);
            }

            return Path.GetFileName(fullPath).Equals("SurfOS", StringComparison.OrdinalIgnoreCase) &&
                   File.Exists(Path.Combine(fullPath, "installer_feedback.json")) &&
                   File.Exists(Path.Combine(fullPath, "options.json")) &&
                   File.Exists(Path.Combine(fullPath, "database.json"));
        }

        private static void Calculator(string[] args, int cmdIndex)
        {
            if (args.Length > cmdIndex + 1)
            {
                if (args.Length != cmdIndex + 4 ||
                    !double.TryParse(args[cmdIndex + 1], out double left) ||
                    !double.TryParse(args[cmdIndex + 3], out double right))
                {
                    PrintCommandUsage("calc");
                    return;
                }

                PrintCalculation(left, args[cmdIndex + 2], right);
                return;
            }

            Console.Write("1st num: ");
            if (!double.TryParse(Console.ReadLine(), out double num1))
            {
                Console.WriteLine("You can only input numbers!");
                return;
            }

            Console.Write("Operators: (+ , - , * ,  / ,^)\n\nUser option: ");
            string operation = Console.ReadLine() ?? "";

            Console.Write("2nd num: ");
            if (!double.TryParse(Console.ReadLine(), out double num2))
            {
                Console.WriteLine("You can only input numbers!");
                return;
            }

            PrintCalculation(num1, operation, num2);
        }

        private static void PrintCalculation(double left, string operation, double right)
        {
            double result = operation switch
            {
                "+" => left + right,
                "-" => left - right,
                "*" => left * right,
                "/" => right == 0 ? double.NaN : left / right,
                "^" => Math.Pow(left, right),
                _ => double.NaN
            };

            if (double.IsNaN(result))
            {
                Console.WriteLine(operation == "/" && right == 0
                    ? "Cannot divide by zero."
                    : "Operation is invalid");
                return;
            }

            Console.WriteLine($"\nResult: {result}");
        }

        private static string CanonicalCommand(string command)
        {
            return command.Trim().ToLowerInvariant() switch
            {
                "ai" => "surfai",
                "animations" => "anim",
                "calculator" => "calc",
                "ide" => "code",
                "shop" => "store",
                "surfos" => "surf",
                _ => command.Trim().ToLowerInvariant()
            };
        }

        private static string UsageOf(string command)
        {
            string canonical = CanonicalCommand(command);
            return CommandUsage.TryGetValue(canonical, out var entry)
                ? entry.Usage
                : command;
        }

        private static void PrintCommandUsage(string command)
        {
            LastExitCode = 2;
            string canonical = CanonicalCommand(command);
            if (!CommandUsage.TryGetValue(canonical, out var entry))
            {
                Console.WriteLine($"No usage information is available for '{command}'.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            ShellError($"Usage: {entry.Usage}", 2);
            Console.ResetColor();
            Console.WriteLine(entry.Details);
            Console.WriteLine($"Try 'help {canonical}' or '{canonical} --help'.");
        }

        private static void ShowHelp(string[] args, int cmdIndex)
        {
            var sections = new (string Key, string Title, (string Command, string Description)[] Commands)[]
            {
                ("extended", "Shell, Network & Runtime", AdditionalUsage.Values.Select(v => (v.Usage, v.Details)).ToArray()),
                ("general", "Getting Around", [
                    (UsageOf("help"), "Shows this menu, a section, or command details"),
                    (UsageOf("clear"), "Clears terminal view, optionally after a delay"),
                    (UsageOf("whoami"), "Shows current user/session details"),
                    (UsageOf("info"), "Shows SurfOS install information")
                ]),
                ("files", "Files", [
                    (UsageOf("pwd"), "Prints current virtual directory"),
                    (UsageOf("ls"), "Lists files; long, hidden, and human-size modes"),
                    (UsageOf("tree"), "Displays a directory tree"),
                    (UsageOf("find"), "Searches file and directory names"),
                    (UsageOf("stat"), "Displays file metadata"),
                    (UsageOf("du"), "Totals file sizes recursively"),
                    (UsageOf("df"), "Shows shared partition space"),
                    (UsageOf("cd"), "Changes directory; defaults to your home"),
                    (UsageOf("mkdir"), "Creates one or more directories"),
                    (UsageOf("touch"), "Creates or updates one or more files"),
                    (UsageOf("cat"), "Prints files, optionally with line numbers"),
                    (UsageOf("echo"), "Prints text or writes/appends with > or >>"),
                    (UsageOf("head"), "Prints the first lines of a file"),
                    (UsageOf("tail"), "Prints the last lines of a file"),
                    (UsageOf("grep"), "Finds matching lines"),
                    (UsageOf("wc"), "Counts lines, words, and UTF-8 bytes"),
                    (UsageOf("rm"), "Removes files or recursive directories"),
                    (UsageOf("rmdir"), "Removes an empty directory"),
                    (UsageOf("backup"), "Creates or lists partition backups in preVersions"),
                    (UsageOf("cp"), "Copies paths; -r for directories, -f to replace"),
                    (UsageOf("mv"), "Moves paths; -f replaces existing files"),
                    (UsageOf("edit"), "Opens Vim-style text editor"),
                    (UsageOf("vim"), "Opens a virtual file in the Vim-style editor")
                ]),
                 ("apps", "Apps & Productivity", [
                     (UsageOf("code"), "Opens installed SurfCode IDE workspace"),
                     (UsageOf("builder"), "Creates and exports themes, games, programs, and extensions"),
                     (UsageOf("game"), "Lists or launches installed games"),
                     (UsageOf("calc"), "Calculates inline or interactively"),
                    (UsageOf("todo"), "Manages task list"),
                    (UsageOf("mail"), "Sends and manages profile notifications"),
                    (UsageOf("music"), "Controls music, cloud downloads, and playlists"),
                    (UsageOf("run"), "Runs macro scripts, optionally verbosely"),
                    (UsageOf("surfai"), "Answers SurfOS cloud questions from local data"),
                    (UsageOf("surf"), "Installs/removes local and SurfCloud packages")
                ]),
                ("time", "Time & Tools", [
                    (UsageOf("clock"), "Displays configured, UTC, or ISO time"),
                    (UsageOf("calendar"), "Displays current or requested month"),
                    (UsageOf("alarm"), "Configures alert background tasks"),
                    (UsageOf("ping"), "Checks network latency with count/timeout controls")
                ]),
                ("system", "System & Diagnostics", [
                    (UsageOf("sys"), "Displays dashboard or JSON runtime metrics"),
                    (UsageOf("free"), "Shows simulated memory and swap pressure"),
                    (UsageOf("swapon"), "Shows the active disk-backed swap device"),
                    (UsageOf("swap"), "Tunes logical disk paging pressure"),
                    (UsageOf("ps"), "Lists running or completed simulated processes"),
                    (UsageOf("top"), "Opens refreshing process monitor"),
                    (UsageOf("kill"), "Safely stops a killable simulated process"),
                    (UsageOf("service"), "Manages background services"),
                    (UsageOf("dmesg"), "Shows or filters the kernel log")
                ]),
                ("customization", "Customization & Account", [
                    (UsageOf("anim"), "Inspects or changes retro animations"),
                    (UsageOf("fontsize"), "Scales text engine bounds"),
                    (UsageOf("theme"), "Manages system styling components"),
                    (UsageOf("user"), "Manages profile accounts")
                ]),
                ("cloud", "SurfCloud Store", [
                    (UsageOf("store"), "Opens the Surf Store app"),
                    ("shop", "Alias for store")
                ]),
                ("session", "Session & Safety", [
                    (UsageOf("logout"), "Locks active shell space"),
                    (UsageOf("shutdown"), "Halts runtime operations"),
                    (UsageOf("uninstall"), "Removes this SurfOS installation")
                ])
            };

            string rawRequested = args.Length > cmdIndex + 1
                ? args[cmdIndex + 1]
                : "all";
            string requestedCommand = CanonicalCommand(rawRequested);
            if (!rawRequested.Equals("all", StringComparison.OrdinalIgnoreCase) &&
                CommandUsage.ContainsKey(requestedCommand))
            {
                PrintCommandUsage(requestedCommand);
                LastExitCode = 0;
                return;
            }

            string requested = NormalizeHelpSection(rawRequested);

            Console.WriteLine("\n=== SurfOS Help ===");
            Console.WriteLine("Paths may be quoted: mkdir \"My Projects\". Combine short flags: ls -lah.");
            Console.WriteLine("Admin actions require a trailing '--force'. Example: user add alex pass123 --force\n");

            if (requested == "all")
            {
                foreach (var section in sections)
                {
                    PrintHelpSection(section.Title, section.Commands);
                }

                Console.WriteLine("\nSections: general, files, apps, time, system, customization, cloud, session");
                Console.WriteLine("Example: help files");
                return;
            }

            var selectedSection = sections.FirstOrDefault(section => section.Key == requested);
            if (selectedSection.Commands is null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                ShellError($"Unknown help section: {args[cmdIndex + 1]}");
                Console.ResetColor();
                Console.WriteLine("Available sections: general, files, apps, time, system, customization, cloud, session");
                return;
            }

            PrintHelpSection(selectedSection.Title, selectedSection.Commands);
            Console.WriteLine("\nUse 'help' to show every section.");
        }

        private static string NormalizeHelpSection(string section)
        {
            return section.Trim().ToLowerInvariant() switch
            {
                "all" => "all",
                "general" or "getting" or "navigation" or "basics" => "general",
                "file" or "files" or "filesystem" => "files",
                "app" or "apps" or "productivity" => "apps",
                "time" or "tool" or "tools" => "time",
                "system" or "diagnostic" or "diagnostics" => "system",
                "custom" or "customization" or "account" or "accounts" => "customization",
                "cloud" or "store" or "surfcloud" => "cloud",
                "session" or "safety" => "session",
                _ => section.Trim().ToLowerInvariant()
            };
        }

        private static void PrintHelpSection(
            string title,
            IReadOnlyList<(string Command, string Description)> commands)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n[{title}]");
            Console.ResetColor();

            foreach ((string command, string description) in commands)
            {
                Console.WriteLine($"  {command,-25} - {description}");
            }
        }

        private static void ManageGames(
            string[] args,
            int cmdIndex,
            ref bool isRunning,
            bool isScriptExecution)
        {
            List<InstalledStorePackage> games = CloudRepositoryManager.LoadInstalledState()
                .Packages
                .Where(package =>
                    package.Category.Equals("Game", StringComparison.OrdinalIgnoreCase) ||
                    package.Category.Equals("Games", StringComparison.OrdinalIgnoreCase))
                .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string action = args.Length > cmdIndex + 1
                ? args[cmdIndex + 1].ToLowerInvariant()
                : "list";

            if (action == "list" && args.Length == cmdIndex + 2 ||
                args.Length == cmdIndex + 1)
            {
                Console.WriteLine("\n--- Installed games ---");
                if (games.Count == 0)
                {
                    Console.WriteLine("No games installed. Use 'surf search games' to find one.");
                    return;
                }

                foreach (InstalledStorePackage game in games)
                {
                    Console.WriteLine($"{game.Id,-18} {game.Version,-8} {game.Name}");
                }

                return;
            }

            if (action != "play" || args.Length != cmdIndex + 3)
            {
                PrintCommandUsage("game");
                return;
            }

            string requestedGame = args[cmdIndex + 2];
            InstalledStorePackage? gameToPlay = games.FirstOrDefault(game =>
                game.Id.Equals(requestedGame, StringComparison.OrdinalIgnoreCase) ||
                game.Name.Equals(requestedGame, StringComparison.OrdinalIgnoreCase));
            if (gameToPlay is null)
            {
                ShellError($"Game '{requestedGame}' is not installed.");
                return;
            }

            if (string.IsNullOrWhiteSpace(gameToPlay.Command) ||
                !TryTokenizeCommand(gameToPlay.Command, out string[] gameCommand, out _) ||
                gameCommand.Length == 0 ||
                gameCommand[0].Equals("game", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Game '{gameToPlay.Name}' does not define a valid launch command.");
                return;
            }

            KernelLog.Info("game", $"launch '{gameToPlay.Id}'");
            ExecuteCommand(gameToPlay.Command, ref isRunning, isScriptExecution);
        }

        private static void ShowSystemInfo(string[] args, int cmdIndex)
        {
            bool json = args.Length == cmdIndex + 2 && args[cmdIndex + 1] == "--json";
            if (args.Length != cmdIndex + 1 && !json)
            {
                PrintCommandUsage("info");
                return;
            }

            PartitionManifest? partition = Partition_Manager.Load(Import.Variables.installPath);
            SwapSnapshot swap = Swap_Manager.GetSnapshot();
            if (json)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    osVersion = "SurfOS 2.0",
                    host = Import.Variables.machineName,
                    installDirectory = Import.Variables.installPath,
                    setupMode = Import.Variables.setupMode,
                    footprint = Import.Variables.systemFootprint,
                    bootCount = Import.Variables.numRun,
                    partition,
                    swap = new
                    {
                        swap.Enabled,
                        swap.CapacityBytes,
                        swap.UsedBytes,
                        swap.FreeBytes,
                        swap.TargetPercent,
                        swap.PageOutBytes,
                        swap.PageInBytes
                    }
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            List<(string Label, string Value)> details = [];
            string userName = string.IsNullOrWhiteSpace(Import.Variables.userName)
                ? "guest"
                : Import.Variables.userName;
            string hostName = string.IsNullOrWhiteSpace(Import.Variables.machineName)
                ? Environment.MachineName
                : Import.Variables.machineName;
            TimeSpan uptime = Import.Variables.sessionStartTime == DateTime.MinValue
                ? TimeSpan.Zero
                : DateTime.Now - Import.Variables.sessionStartTime;
            string processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
                ?? $"{Environment.ProcessorCount} logical processors";
            long workingSet = Process.GetCurrentProcess().WorkingSet64;

            details.Add(("OS", "SurfOS 2.0"));
            details.Add(("Host", hostName));
            details.Add(("Kernel", $"SurfKernel 3.0 / .NET {Environment.Version}"));
            details.Add(("Uptime", FormatUptime(uptime)));
            details.Add(("Shell", "SurfShell"));
            details.Add(("Theme", Import.Variables.defaultTheme));
            details.Add(("Mode", $"{Import.Variables.setupMode} ({Import.Variables.systemFootprint})"));
            details.Add(("CPU", processor));
            details.Add(("Memory", $"{Swap_Manager.FormatBytes(workingSet)} process working set"));
            details.Add(("Boots", Import.Variables.numRun.ToString()));

            if (partition is not null)
            {
                long usedBytes = Partition_Manager.GetUsedBytes(Import.Variables.installPath);
                long usableBytes = Math.Max(
                    0,
                    partition.CapacityBytes - partition.SystemReservedBytes);
                long freeBytes = Math.Max(0, usableBytes - usedBytes);
                details.Add(("Volume", $"{partition.Label} ({partition.FileSystem}) at {partition.MountPoint}"));
                details.Add(("Storage", $"{FormatPartitionSize(usedBytes)} / {FormatPartitionSize(usableBytes)}"));
                details.Add(("Free", FormatPartitionSize(freeBytes)));
                details.Add(("Swap", $"{Swap_Manager.FormatBytes(swap.UsedBytes)} / {Swap_Manager.FormatBytes(swap.CapacityBytes)}"));
                details.Add(("Encryption", partition.EncryptionEnabled ? "Enabled (simulated)" : "Disabled"));
                IReadOnlyList<BackupInfo> backups = Backup_Manager.ListBackups();
                details.Add(("Backups", $"{backups.Count} ({Backup_Manager.FormatSize(backups.Sum(backup => backup.Size))})"));
            }

            details.Add(("Install", Import.Variables.installPath));
            RenderSystemInfo(userName, hostName, details);
        }

        private static void RenderSystemInfo(
            string userName,
            string hostName,
            IReadOnlyList<(string Label, string Value)> details)
        {
            const int gap = 4;
            int logoWidth = SurfboardLogo.Max(line => line.Length);
            int contentLineCount = details.Count + 5;
            int lineCount = Math.Max(SurfboardLogo.Length, contentLineCount);
            ConsoleColor originalForeground = Console.ForegroundColor;
            ConsoleColor originalBackground = Console.BackgroundColor;

            Console.WriteLine();
            try
            {
                for (int index = 0; index < lineCount; index++)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(index < SurfboardLogo.Length
                        ? SurfboardLogo[index].PadRight(logoWidth + gap)
                        : new string(' ', logoWidth + gap));

                    if (index == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write($"{userName}@{hostName}");
                    }
                    else if (index == 1)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write(new string('-', Math.Max(16, userName.Length + hostName.Length + 1)));
                    }
                    else if (index - 2 < details.Count)
                    {
                        (string label, string value) = details[index - 2];
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write($"{label}: ");
                        Console.ForegroundColor = ConsoleColor.Gray;
                        Console.Write(value);
                    }
                    else if (index == details.Count + 3)
                    {
                        WriteColorPalette(bright: false);
                    }
                    else if (index == details.Count + 4)
                    {
                        WriteColorPalette(bright: true);
                    }

                    Console.WriteLine();
                }
            }
            finally
            {
                Console.ForegroundColor = originalForeground;
                Console.BackgroundColor = originalBackground;
            }
        }

        private static void WriteColorPalette(bool bright)
        {
            ConsoleColor[] colors = bright
                ?
                [
                    ConsoleColor.DarkGray,
                    ConsoleColor.Red,
                    ConsoleColor.Green,
                    ConsoleColor.Yellow,
                    ConsoleColor.Blue,
                    ConsoleColor.Magenta,
                    ConsoleColor.Cyan,
                    ConsoleColor.White
                ]
                :
                [
                    ConsoleColor.Black,
                    ConsoleColor.DarkRed,
                    ConsoleColor.DarkGreen,
                    ConsoleColor.DarkYellow,
                    ConsoleColor.DarkBlue,
                    ConsoleColor.DarkMagenta,
                    ConsoleColor.DarkCyan,
                    ConsoleColor.Gray
                ];

            foreach (ConsoleColor color in colors)
            {
                Console.ForegroundColor = color;
                Console.Write("██");
            }
        }

        private static string FormatUptime(TimeSpan uptime)
        {
            if (uptime < TimeSpan.Zero)
            {
                uptime = TimeSpan.Zero;
            }

            return uptime.TotalDays >= 1
                ? $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m"
                : $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s";
        }

        private static string FormatPartitionSize(long bytes)
        {
            double gb = bytes / (double)Partition_Manager.BytesPerGb;
            return gb >= 1 ? $"{gb:0.00} GB" : $"{bytes / (1024d * 1024d):0.00} MB";
        }

        // ==========================================
        // MULTI-APP UPGRADES SYSTEM IMPLEMENTATION
        // ==========================================

        private static void RunSysMonitor(string[] args, int cmdIndex)
        {
            bool json = args.Length == cmdIndex + 2 && args[cmdIndex + 1] == "--json";
            if (args.Length != cmdIndex + 1 && !json)
            {
                PrintCommandUsage("sys");
                return;
            }

            TimeSpan uptime = DateTime.Now - Import.Variables.sessionStartTime;
            long processMemory = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024); // Memory in MBs
            SwapSnapshot swap = Swap_Manager.GetSnapshot();
            if (json)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    user = Import.Variables.userName,
                    uptimeSeconds = (long)uptime.TotalSeconds,
                    processMemoryMb = processMemory,
                    swapCapacityBytes = swap.CapacityBytes,
                    swapUsedBytes = swap.UsedBytes,
                    swapTargetPercent = swap.TargetPercent,
                    logicalProcessors = Environment.ProcessorCount,
                    platform = Environment.OSVersion.ToString()
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            RetroConsole.Spinner("POLLING HARDWARE BUS", 300, ConsoleColor.Cyan);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n   === SurfOS Performance Dashboard ===");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  Current Active User : {Import.Variables.userName}");
            Console.WriteLine($" ⏳ Active Shell Uptime : {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s");
            Console.WriteLine($"  Allocated OS RAM    : {processMemory} MB");
            Console.WriteLine(
                $"  SurfOS Swap         : {Swap_Manager.FormatBytes(swap.UsedBytes)} / " +
                $"{Swap_Manager.FormatBytes(swap.CapacityBytes)} ({swap.UsedPercent:0}%)");
            Console.WriteLine($"  Logical CPU Cores   : {Environment.ProcessorCount} Threads");
            Console.WriteLine($"  Operating Platform  : {Environment.OSVersion}");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(" ========================================");
        }

        private static void ShowMemoryUsage(string[] args, int cmdIndex)
        {
            bool bytes = args.Length == cmdIndex + 2 &&
                         args[cmdIndex + 1] == "--bytes";
            if (args.Length != cmdIndex + 1 && !bytes)
            {
                PrintCommandUsage("free");
                return;
            }

            SwapSnapshot swap = Swap_Manager.GetSnapshot();
            long simulatedMemoryUsed =
                ProcessManager.ListProcesses().Sum(process => process.MemoryEstimateMb) *
                1024L * 1024L;
            long simulatedMemoryTotal = Math.Max(
                256L * 1024L * 1024L,
                simulatedMemoryUsed + 64L * 1024L * 1024L);

            string Format(long value) => bytes
                ? value.ToString()
                : Swap_Manager.FormatBytes(value);

            Console.WriteLine($"{"",-8} {"total",12} {"used",12} {"free",12}");
            Console.WriteLine(
                $"{"Mem:",-8} {Format(simulatedMemoryTotal),12} " +
                $"{Format(simulatedMemoryUsed),12} " +
                $"{Format(simulatedMemoryTotal - simulatedMemoryUsed),12}");
            Console.WriteLine(
                $"{"Swap:",-8} {Format(swap.CapacityBytes),12} " +
                $"{Format(swap.UsedBytes),12} {Format(swap.FreeBytes),12}");
            Console.WriteLine(
                $"Swap pressure target: {swap.TargetPercent}% (disk-backed simulation)");
        }

        private static void ShowSwapDevice(string[] args, int cmdIndex)
        {
            if (args.Length != cmdIndex + 2 || args[cmdIndex + 1] != "--show")
            {
                PrintCommandUsage("swapon");
                return;
            }

            SwapSnapshot swap = Swap_Manager.GetSnapshot();
            if (!swap.Enabled)
            {
                Console.WriteLine("No SurfOS swap device is enabled.");
                return;
            }

            Console.WriteLine("NAME                          TYPE  SIZE       USED       PRIO");
            Console.WriteLine(
                $"/system/swap/swapfile.sys     file  " +
                $"{Swap_Manager.FormatBytes(swap.CapacityBytes),-10} " +
                $"{Swap_Manager.FormatBytes(swap.UsedBytes),-10} -2");
        }

        private static void ManageSwap(string[] args, int cmdIndex)
        {
            string action = args.Length > cmdIndex + 1
                ? args[cmdIndex + 1].ToLowerInvariant()
                : "status";

            if (action == "trim")
            {
                bool trimmed = Swap_Manager.SetTargetPercent(0, out string message);
                Console.ForegroundColor = trimmed ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine(message);
                Screen_Print.ResetColors();
                return;
            }

            if (action == "set" &&
                args.Length == cmdIndex + 3 &&
                int.TryParse(args[cmdIndex + 2], out int percent))
            {
                bool changed = Swap_Manager.SetTargetPercent(percent, out string message);
                Console.ForegroundColor = changed ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine(message);
                Screen_Print.ResetColors();
                return;
            }

            if (action == "status" && args.Length <= cmdIndex + 2)
            {
                ShowMemoryUsage(["free"], 0);
                SwapSnapshot snapshot = Swap_Manager.GetSnapshot();
                Console.WriteLine(
                    $"Paged out total: {Swap_Manager.FormatBytes(snapshot.PageOutBytes)}");
                Console.WriteLine(
                    $"Paged in total : {Swap_Manager.FormatBytes(snapshot.PageInBytes)}");
                return;
            }

            PrintCommandUsage("swap");
        }

        private static void RunMailSystem(string[] args, int cmdIndex)
        {
            Import.DatabaseRecord? currentUser = GetCurrentUser();
            if (currentUser == null) return;

            string action = args.Length > cmdIndex + 1
                ? args[cmdIndex + 1].ToLowerInvariant()
                : "list";

            if (action == "send" && args.Length > cmdIndex + 3)
            {
                string recipientName = args[cmdIndex + 2];
                string messageBody = string.Join(" ", args, cmdIndex + 3, args.Length - (cmdIndex + 3));

                Import.DatabaseRecord? receiver = FindUser(recipientName);
                if (receiver == null)
                {
                    Console.WriteLine($" Mail failure: Destination account '{recipientName}' could not be resolved.");
                    return;
                }

                if (receiver.Mailbox == null) receiver.Mailbox = new List<Import.MailMessage>();

                receiver.Mailbox.Add(new Import.MailMessage
                {
                    Sender = Import.Variables.userName,
                    Timestamp = DateTime.Now.ToString("g"),
                    MessageText = messageBody
                });

                RetroConsole.Spinner("TRANSMITTING MESSAGE", 350, ConsoleColor.Cyan);
                SaveUserDatabase();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($" Message routed successfully to '{recipientName}' storage banks.");
            }
            else if (action == "clear")
            {
                currentUser.Mailbox.Clear();
                SaveUserDatabase();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" Mailbox cleared clean.");
            }
            else if (action == "delete" &&
                     args.Length == cmdIndex + 3 &&
                     int.TryParse(args[cmdIndex + 2], out int messageId) &&
                     messageId >= 1 &&
                     messageId <= currentUser.Mailbox.Count)
            {
                currentUser.Mailbox.RemoveAt(messageId - 1);
                SaveUserDatabase();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Message {messageId} deleted.");
            }
            else if (action is "list" or "inbox")
            {
                Console.WriteLine($"\n === {Import.Variables.userName}'s Inbox Bank ===");
                if (currentUser.Mailbox == null || currentUser.Mailbox.Count == 0)
                {
                    Console.WriteLine("Inbox container empty. No new unread messages.");
                    return;
                }

                for (int i = 0; i < currentUser.Mailbox.Count; i++)
                {
                    var m = currentUser.Mailbox[i];
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[{i + 1}] From: {m.Sender} ({m.Timestamp})");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"    Msg: {m.MessageText}\n");
                }
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Type 'mail clear' to delete inbox components.");
            }
            else
            {
                PrintCommandUsage("mail");
            }
        }

        private static void RunScriptCommand(
            string[] args,
            int cmdIndex,
            ref bool isRunning)
        {
            bool verbose = args.Skip(cmdIndex + 1).Contains("-v");
            string[] operands = args.Skip(cmdIndex + 1)
                .Where(argument => argument != "-v")
                .ToArray();
            if (operands.Length != 1)
            {
                PrintCommandUsage("run");
                return;
            }

            RunScriptAutomation(operands[0], verbose, ref isRunning);
        }

        private static void RunScriptAutomation(
            string fileName,
            bool verbose,
            ref bool isRunning)
        {
            if (!VirtualFileSystem.ReadFile(fileName, out string script, out string readError))
            {
                ShellError($"Execution Error: {readError}");
                return;
            }

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            RetroConsole.Spinner($"PARSING {fileName}", 300, ConsoleColor.DarkYellow);

            foreach (string macroLine in script.Replace("\r\n", "\n").Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(macroLine) || macroLine.Trim().StartsWith("#")) continue; // Skip comments/blanks

                if (verbose)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"+ {macroLine}");
                    Screen_Print.ResetColors();
                }

                ExecuteCommand(macroLine, ref isRunning, true);
                if (!isRunning)
                {
                    break;
                }
            }
            Console.ForegroundColor = ConsoleColor.Green;
            RetroConsole.TypeLine("SCRIPT EXECUTION COMPLETE.", 4);
        }

        private static void ManageTodo(string[] args, int cmdIndex)
        {
            string todoPath = Path.Combine(Import.Variables.installPath, "todo.json");
            List<Import.TodoItem> tasks = new List<Import.TodoItem>();
            if (File.Exists(todoPath))
            {
                tasks = JsonStorage.Read<List<Import.TodoItem>>(todoPath) ?? [];
            }

            string action = args.Length > cmdIndex + 1
                ? args[cmdIndex + 1].ToLowerInvariant()
                : "list";

            if (action == "add" && args.Length > cmdIndex + 2)
            {
                string taskDesc = string.Join(" ", args, cmdIndex + 2, args.Length - (cmdIndex + 2));
                int nextId = tasks.Count > 0 ? checked(tasks.Max(task => task.ID) + 1) : 1;
                tasks.Add(new Import.TodoItem { ID = nextId, Task = taskDesc, Done = false });
                JsonStorage.Write(todoPath, tasks);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($" Added task: {taskDesc}");
            }
            else if (action == "complete" && args.Length > cmdIndex + 2 && int.TryParse(args[cmdIndex + 2], out int compId))
            {
                var task = tasks.Find(t => t.ID == compId);
                if (task != null)
                {
                    task.Done = true;
                    JsonStorage.Write(todoPath, tasks);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($" Task {compId} marked complete!");
                }
                else
                {
                    ShellError($"Task {compId} was not found.");
                }
            }
            else if (action == "remove" && args.Length > cmdIndex + 2 && int.TryParse(args[cmdIndex + 2], out int remId))
            {
                int removed = tasks.RemoveAll(t => t.ID == remId);
                if (removed == 0)
                {
                    ShellError($"Task {remId} was not found.");
                }
                else
                {
                    JsonStorage.Write(todoPath, tasks);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($" Task {remId} removed.");
                }
            }
            else if (action == "clear")
            {
                int removed = tasks.RemoveAll(task => task.Done);
                JsonStorage.Write(todoPath, tasks);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Removed {removed} completed task(s).");
            }
            else if (action == "list")
            {
                Console.WriteLine("\n---  To-Do List ---");
                if (tasks.Count == 0) Console.WriteLine("No active tasks found.");
                foreach (var t in tasks)
                {
                    Console.ForegroundColor = t.Done ? ConsoleColor.DarkGray : ConsoleColor.White;
                    Console.WriteLine($"{t.ID}. {(t.Done ? "[X]" : "[ ]")} {t.Task}");
                }
            }
            else
            {
                PrintCommandUsage("todo");
            }
        }

        private static void RunTextEditor(string fileName) => VimEditor.EditVirtualFile(fileName);

        private static void SaveUserDatabase()
        {
            string dbPath = Path.Combine(Import.Variables.installPath, "database.json");
            JsonStorage.Write(dbPath, Import.Variables.userDatabase);
        }

        private static void ConfigureAnimations(string[] args, int commandIndex)
        {
            if (args.Length <= commandIndex + 1 ||
                args[commandIndex + 1].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(
                    $"Retro animations are {(RetroConsole.AnimationsEnabled ? "ON" : "OFF")}.");
                return;
            }

            string setting = args[commandIndex + 1].ToLowerInvariant();
            if (setting is not ("on" or "off"))
            {
                PrintCommandUsage("anim");
                return;
            }

            RetroConsole.AnimationsEnabled = setting == "on";
            RetroConsole.TypeLine(
                $"RETRO DISPLAY EFFECTS {(RetroConsole.AnimationsEnabled ? "ENABLED" : "DISABLED")}.",
                4);
        }

        private static bool UsesInteractiveScreen(string command)
        {
            return command is "edit" or "vim" or "code" or "ide" or "builder" or "package-builder" or "uninstall" or "store" or "shop" or "surf" or "surfos";
        }

        private static bool UsesFullScreenApp(
            string command,
            string[] args,
            int cmdIndex,
            bool isForced,
            bool isScriptExecution)
        {
            return command switch
            {
                "top" or "store" or "shop" => true,
                "edit" => args.Length > cmdIndex + 1,
                "code" or "ide" => CloudRepositoryManager.IsPackageInstalled("surfcode-ide"),
                "builder" or "package-builder" => true,
                "uninstall" => !isScriptExecution,
                _ => false
            };
        }

        private static Import.DatabaseRecord? GetCurrentUser()
        {
            return FindUser(Import.Variables.userName);
        }

        private static Import.DatabaseRecord? FindUser(string username)
        {
            return Import.Variables.userDatabase.Find(
                user => user.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Draws a colored status bar at the top of the terminal.
        /// </summary>
        public static void DrawHeader()
        {
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.ForegroundColor = ConsoleColor.White;

            // Calculate uptime safely
            TimeSpan uptime = Import.Variables.sessionStartTime != DateTime.MinValue
                ? DateTime.Now - Import.Variables.sessionStartTime
                : TimeSpan.Zero;

            // Leave the terminal's final column unused. Writing into that column
            // triggers an automatic wrap in Windows Terminal; WriteLine would then
            // advance once more and can leave a clipped ghost row at the viewport edge.
            string status = $"  SurfOS Kernel v3.0  |  User: {Import.Variables.userName}  |  Cloud: ONLINE  |  Uptime: {uptime:hh\\:mm\\:ss}  ";
            int drawableWidth = Math.Max(1, Console.WindowWidth - 1);
            string visibleStatus = status.Length > drawableWidth
                ? status[..drawableWidth]
                : status.PadRight(drawableWidth);
            Console.WriteLine(visibleStatus);

            Console.ResetColor();
        }
    }
}
