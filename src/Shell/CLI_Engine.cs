using SurfOS2.os_Apps;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;


namespace SurfOS2
{
    internal class CLI_Engine
    {
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
                Screen_Print.Print_Selected_Package();
                if (Import.Variables.safeMode)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\nSAFE MODE: cloud presence, animations, and non-essential services are disabled.");
                    Console.ResetColor();
                }

                RetroConsole.TypeLine("\nCOMMAND PROCESSOR READY", 4);
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

                    string input = Console.ReadLine() ?? string.Empty;
                    ExecuteCommand(input, ref isRunning, false); // Route inputs through main process router
                }
            }
            catch (Exception ex)
            {
                KernelPanic.ShowAndHandle(
                    ex,
                    "src/Shell/CLI_Engine.cs",
                    "Reboot SurfOS. If the shell keeps crashing, boot Safe Mode and run dmesg errors.");
            }
        }

        //  NEW: Process Router lets the normal prompt AND script engines run commands identically!
        public static void ExecuteCommand(string rawInput, ref bool isRunning, bool isScriptExecution)
        {
            string[] commandParts = rawInput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (commandParts.Length == 0) return;

            bool isSudo = false;
            int cmdIndex = 0;

            if (commandParts[0].Equals("sudo", StringComparison.OrdinalIgnoreCase))
            {
                isSudo = true;
                if (commandParts.Length == 1)
                {
                    Console.WriteLine("usage: sudo <command>");
                    return;
                }
                cmdIndex = 1;
            }

            string mainCommand = commandParts[cmdIndex].ToLowerInvariant();
            bool usesFullScreenApp = UsesFullScreenApp(
                mainCommand,
                commandParts,
                cmdIndex,
                isSudo,
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
                        Console.WriteLine(VirtualFileSystem.CurrentDirectory);
                        break;

                    case "ls":
                        ListVirtualDirectory(commandParts, cmdIndex);
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
                        ShowProcesses();
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
                        Console.Clear();
                        Screen_Print.Print_Selected_Package();
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
                        else Console.WriteLine("Usage: clearT <time>");
                        break;

                    case "fontsize":
                        if (commandParts.Length > cmdIndex + 1 && short.TryParse(commandParts[cmdIndex + 1], out short newSize))
                        {
                            Core_Engine.ChangeFontSize(newSize);
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($" Font size successfully changed to {newSize}.");
                        }
                        else Console.WriteLine("Usage: fontsize <number>");
                        break;

                    case "info":
                        ShowSystemInfo();
                        break;

                    case "anim":
                    case "animations":
                        ConfigureAnimations(commandParts, cmdIndex);
                        break;

                    case "sys":
                        RunSysMonitor();
                        break;

                    case "store":
                    case "shop":
                        SurfStore.Open();
                        break;

                    case "calc":
                    case "calculator":
                        Calculator();
                        break;

                    case "mail":
                        RunMailSystem(commandParts, cmdIndex);
                        break;

                    case "music":
                        MusicPlayer.HandleCommand(commandParts, cmdIndex);
                        break;

                    case "run":
                        if (commandParts.Length > cmdIndex + 1)
                        {
                            RunScriptAutomation(commandParts[cmdIndex + 1], ref isRunning);
                        }
                        else Console.WriteLine("Usage: run <script_file.txt>");
                        break;

                    case "clock":
                        Time_Manager.ShowClock();
                        break;

                    case "calendar":
                        Time_Manager.ShowCalendar();
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
                        Console.WriteLine($"Current User: {Import.Variables.userName}");
                        Console.WriteLine($"Rank/Status : {currentProfile?.CurrentRank ?? "User"}");
                        Console.WriteLine($"Permission  : Admin ({Import.Variables.uuid})");
                        if (isSudo) Console.WriteLine("Sudo status : GRANTED ");
                        break;

                    case "ping":
                        if (commandParts.Length > cmdIndex + 1)
                        {
                            string host = commandParts[cmdIndex + 1];
                            try
                            {
                                using Ping pingSender = new();
                                Console.WriteLine($"Pinging {host} with 32 bytes of data...");
                                PingReply reply = pingSender.Send(host, 2000);
                                if (reply.Status == IPStatus.Success)
                                {
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"Reply from {reply.Address}: time={reply.RoundtripTime}ms");
                                }
                                else
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine($"Ping failed: {reply.Status}");
                                }
                            }
                            catch (Exception ex) { Console.WriteLine($"Ping error: {ex.Message}"); }
                        }
                        else Console.WriteLine("Usage: ping <address>");
                        break;

                    case "todo":
                        ManageTodo(commandParts, cmdIndex);
                        break;

                    case "edit":
                        if (commandParts.Length > cmdIndex + 1) RunTextEditor(commandParts[cmdIndex + 1]);
                        else Console.WriteLine("Usage: edit <filename.txt>");
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

                    case "user":
                        {
                            string action = commandParts.Length > cmdIndex + 1
                                ? commandParts[cmdIndex + 1].ToLowerInvariant()
                                : "list";

                            if (action == "add")
                            {
                                if (commandParts.Length > cmdIndex + 3)
                                {
                                    if (!isSudo)
                                    {
                                        Console.WriteLine(" You must use 'sudo' to add new users!");
                                        break;
                                    }

                                    string newName = commandParts[cmdIndex + 2];
                                    string newPass = commandParts[cmdIndex + 3];

                                    if (FindUser(newName) is not null)
                                    {
                                        Console.WriteLine($"User '{newName}' already exists.");
                                    }
                                    else
                                    {
                                        int nextId = Import.Variables.userDatabase.Count > 0 ? Import.Variables.userDatabase[^1].ID + 1 : 1;
                                        Import.Variables.userDatabase.Add(new Import.DatabaseRecord
                                        {
                                            ID = nextId,
                                            Username = newName,
                                            Password = newPass,
                                            Admin = "User",
                                            CurrentRank = "User",
                                            Mailbox = new List<Import.MailMessage>()
                                        });

                                        SaveUserDatabase();
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine($" New user '{newName}' created successfully!");
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("Usage: sudo user add <username> <password>");
                                }
                            }
                            else if (action == "remove")
                            {
                                if (commandParts.Length > cmdIndex + 2)
                                {
                                    if (!isSudo)
                                    {
                                        Console.WriteLine(" You must use 'sudo' to remove users!");
                                        break;
                                    }

                                    string targetName = commandParts[cmdIndex + 2];
                                    if (targetName.Equals(
                                        Import.Variables.userName,
                                        StringComparison.OrdinalIgnoreCase))
                                    {
                                        Console.WriteLine(" Safety Lock: You cannot delete your own account while logged into it!");
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
                                            Console.WriteLine("User not found.");
                                        }
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("Usage: sudo user remove <username>");
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
                                Console.WriteLine("Usage:\n  user list\n  sudo user add <name> <pass>\n  sudo user remove <name>");
                            }
                        }
                        break;

                    case "theme":
                        if (commandParts.Length > cmdIndex + 1)
                        {
                            string action = commandParts[cmdIndex + 1].ToLowerInvariant();
                            if (action == "load" && commandParts.Length > cmdIndex + 2)
                            {
                                Screen_Print.LoadAndApplyTheme(commandParts[cmdIndex + 2], isSudo);
                            }
                            else if (action == "remove" && commandParts.Length > cmdIndex + 2)
                            {
                                string targetTheme = commandParts[cmdIndex + 2];
                                string packagePath = Path.Combine(Import.Variables.installPath, "Packages", $"{targetTheme}.json");
                                if (File.Exists(packagePath))
                                {
                                    if ((targetTheme == "HolySurf" || targetTheme == "UnHolySurf") && !isSudo)
                                        Console.WriteLine(" Core system themes cannot be deleted without 'sudo'!");
                                    else
                                    {
                                        File.Delete(packagePath);
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine($" Theme '{targetTheme}' was successfully removed!");
                                    }
                                }
                                else Console.WriteLine($"[SurfOS] Theme pack '{targetTheme}' was not found.");
                            }
                            else if (action == "default" && commandParts.Length > cmdIndex + 2)
                            {
                                string targetTheme = commandParts[cmdIndex + 2];
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
                        }

                        else
                        {
                            Console.WriteLine("Usages:" +
                                "\ntheme load <theme_name>" +
                                "\ntheme default <theme_name>" +
                                "\ntheme remove <theme_name>" +
                                "\ntheme link");
                        }
                        break;

                    case "uninstall":
                        EngageUninstallSequence(isSudo, isScriptExecution);
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
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Command not recognized: '{mainCommand}'. Type 'help' for a list of commands.");
                        KernelLog.Error("command", $"command not recognized: {mainCommand}");
                        break;
                }
            }
            catch (Exception ex)
            {
                KernelLog.Error("crash", $"command '{mainCommand}' crashed: {ex}");
                KernelPanic.ShowAndHandle(
                    ex,
                    "src/Shell/CLI_Engine.cs",
                    $"Command '{mainCommand}' crashed. Reboot or use Safe Mode, then inspect dmesg errors.");
            }

            Screen_Print.ResetColors();
        }

        private static void ListVirtualDirectory(string[] args, int cmdIndex)
        {
            string path = args.Length > cmdIndex + 1 ? args[cmdIndex + 1] : string.Empty;
            RunVfsCommand(() =>
            {
                IReadOnlyList<VirtualDirectoryEntry> entries =
                    VirtualFileSystem.List(path, out string error);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Console.WriteLine(error);
                    return;
                }

                foreach (VirtualDirectoryEntry entry in entries)
                {
                    string size = entry.Type == "dir" ? "<DIR>" : $"{entry.Size}b";
                    Console.WriteLine($"{size,8}  {entry.Name}");
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
                    Console.WriteLine(error);
                }
            });
        }

        private static void CreateVirtualDirectory(string[] args, int cmdIndex)
        {
            if (args.Length <= cmdIndex + 1)
            {
                Console.WriteLine("Usage: mkdir <name>");
                return;
            }

            RunVfsCommand(() =>
            {
                VirtualFileSystem.CreateDirectory(args[cmdIndex + 1], out string error);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Console.WriteLine(error);
                }
            });
        }

        private static void TouchVirtualFile(string[] args, int cmdIndex)
        {
            if (args.Length <= cmdIndex + 1)
            {
                Console.WriteLine("Usage: touch <file>");
                return;
            }

            RunVfsCommand(() =>
            {
                VirtualFileSystem.Touch(args[cmdIndex + 1], out string error);
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Console.WriteLine(error);
                }
            });
        }

        private static void ReadVirtualFile(string[] args, int cmdIndex)
        {
            if (args.Length <= cmdIndex + 1)
            {
                Console.WriteLine("Usage: cat <file>");
                return;
            }

            RunVfsCommand(() =>
            {
                if (VirtualFileSystem.ReadFile(args[cmdIndex + 1], out string content, out string error))
                {
                    Console.WriteLine(content);
                }
                else
                {
                    Console.WriteLine(error);
                }
            });
        }

        private static void RemoveVirtualFile(string[] args, int cmdIndex)
        {
            if (args.Length <= cmdIndex + 1)
            {
                Console.WriteLine("Usage: rm <file>");
                return;
            }

            RunVfsCommand(() =>
            {
                if (!VirtualFileSystem.RemoveFile(args[cmdIndex + 1], out string error))
                {
                    Console.WriteLine(error);
                }
            });
        }

        private static void RemoveVirtualDirectory(string[] args, int cmdIndex)
        {
            if (args.Length <= cmdIndex + 1)
            {
                Console.WriteLine("Usage: rmdir <directory>");
                return;
            }
            RunVfsCommand(() =>
            {
                if (!VirtualFileSystem.RemoveDirectory(args[cmdIndex + 1], out string error))
                {
                    Console.WriteLine(error);
                }
            });
        }

        private static void ManageBackups(string[] args, int cmdIndex)
        {
            if (args.Length <= cmdIndex + 1)
            {
                Console.WriteLine("Usage: backup <name> | backup list");
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
            if (args.Length <= cmdIndex + 2)
            {
                Console.WriteLine("Usage: cp <source> <dest>");
                return;
            }

            RunVfsCommand(() =>
            {
                if (!VirtualFileSystem.Copy(args[cmdIndex + 1], args[cmdIndex + 2], out string error))
                {
                    Console.WriteLine(error);
                }
            });
        }

        private static void MoveVirtualPath(string[] args, int cmdIndex)
        {
            if (args.Length <= cmdIndex + 2)
            {
                Console.WriteLine("Usage: mv <source> <dest>");
                return;
            }

            RunVfsCommand(() =>
            {
                if (!VirtualFileSystem.Move(args[cmdIndex + 1], args[cmdIndex + 2], out string error))
                {
                    Console.WriteLine(error);
                }
            });
        }

        private static void RunVfsCommand(Action command)
        {
            try
            {
                command();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"VFS error: {ex.Message}");
            }
        }

        private static void ShowProcesses()
        {
            IReadOnlyList<ProcessInfo> processes = ProcessManager.ListProcesses(includeCompleted: true);
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
                    Console.WriteLine($"\nSimulated process memory: {totalMemory} MB");

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
            if (args.Length <= cmdIndex + 1 ||
                !int.TryParse(args[cmdIndex + 1], out int pid))
            {
                Console.WriteLine("Usage: kill <pid>");
                return;
            }

            bool killed = ProcessManager.TryKillProcess(pid, out string message);
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
                Console.WriteLine("Usage: service <list|start|stop|restart|status> <name>");
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
                Console.WriteLine("Usage: service <list|start|stop|restart|status> <name>");
                return;
            }

            ServiceState? state = ServiceManager.GetStatus(serviceName);
            if (state is null)
            {
                Console.WriteLine($"Service '{serviceName}' was not found.");
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
                    Console.WriteLine("Usage: dmesg [clear|errors|boot]");
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

        private static void EngageUninstallSequence(bool isSudo, bool isScriptExecution)
        {
            if (isScriptExecution)
            {
                Console.WriteLine("Uninstall cannot be launched from a script.");
                return;
            }

            if (!isSudo)
            {
                Console.WriteLine("Usage: sudo uninstall");
                Console.WriteLine("This command permanently removes the active SurfOS installation.");
                return;
            }

            string installPath = Import.Variables.installPath;
            if (!IsValidInstallPath(installPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Uninstall aborted: active install path could not be verified.");
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

            Console.WriteLine();
            try
            {
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
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nUninstall failed: {ex.Message}");
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

        private static void Calculator()
        {
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

            double result = operation switch
            {
                "+" => num1 + num2,
                "-" => num1 - num2,
                "*" => num1 * num2,
                "/" => num2 == 0 ? double.NaN : num1 / num2,
                "^" => Math.Pow(num1, num2),
                _ => double.NaN
            };

            if (double.IsNaN(result))
            {
                Console.WriteLine(operation == "/" && num2 == 0
                    ? "Cannot divide by zero."
                    : "Operation is invalid");
                return;
            }

            Console.WriteLine($"\nResult: {result}");
        }
        private static void ShowHelp(string[] args, int cmdIndex)
        {
            var sections = new (string Key, string Title, (string Command, string Description)[] Commands)[]
            {
                ("general", "Getting Around", [
                    ("help", "Shows this menu; use help <section> to filter"),
                    ("clear", "Clears terminal view"),
                    ("whoami", "Shows current user/session details"),
                    ("info", "Shows SurfOS install information")
                ]),
                ("files", "Files", [
                    ("pwd", "Prints current virtual directory"),
                    ("ls", "Lists virtual files"),
                    ("cd", "Changes virtual directory"),
                    ("mkdir", "Creates a virtual directory"),
                    ("touch", "Creates or updates a virtual file"),
                    ("cat", "Prints a virtual file"),
                    ("rm", "Removes a virtual file"),
                    ("rmdir", "Removes an empty virtual directory"),
                    ("backup", "Creates or lists partition backups in preVersions"),
                    ("cp", "Copies virtual files/directories"),
                    ("mv", "Moves virtual files/directories"),
                    ("edit", "Opens text utility editor")
                ]),
                ("apps", "Apps & Productivity", [
                    ("code", "Opens installed SurfCode IDE workspace"),
                    ("calc", "Opens calculator"),
                    ("todo", "Manages task list"),
                    ("mail", "Send/read profile notifications"),
                    ("music", "Opens SurfOS music player"),
                    ("run", "Runs macro sequence script profiles"),
                    ("surfai", "Answers SurfOS cloud questions from local data"),
                    ("surf", "Installs/removes local and SurfCloud packages"),
                    ("surfos", "Alias for surf package commands")
                ]),
                ("time", "Time & Tools", [
                    ("clock", "Displays clock stats"),
                    ("calendar", "Displays highlighted date grids"),
                    ("alarm", "Configures alert background tasks"),
                    ("ping", "Checks network latency")
                ]),
                ("system", "System & Diagnostics", [
                    ("sys", "Displays hardware tracking dashboard"),
                    ("ps", "Lists simulated processes"),
                    ("top", "Opens refreshing process monitor"),
                    ("kill", "Safely stops a killable simulated process"),
                    ("service", "Manages background services"),
                    ("dmesg", "Shows kernel log; try dmesg errors or dmesg boot")
                ]),
                ("customization", "Customization & Account", [
                    ("anim", "Enables or disables retro animations"),
                    ("fontsize", "Scales text engine bounds"),
                    ("theme", "Manages system styling components"),
                    ("user", "Manages profile accounts")
                ]),
                ("cloud", "SurfCloud Store", [
                    ("store", "Opens the Surf Store app"),
                    ("shop", "Alias for Surf Store")
                ]),
                ("session", "Session & Safety", [
                    ("logout", "Locks active shell space"),
                    ("shutdown", "Halts runtime operations"),
                    ("uninstall", "Removes this SurfOS installation (sudo required)")
                ])
            };

            string requested = args.Length > cmdIndex + 1
                ? NormalizeHelpSection(args[cmdIndex + 1])
                : "all";

            Console.WriteLine("\n=== SurfOS Help ===");
            Console.WriteLine("Tip: admin actions use 'sudo <command>'. Example: sudo user add alex pass123\n");

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
                Console.WriteLine($"Unknown help section: {args[cmdIndex + 1]}");
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
                Console.WriteLine($"  {command,-10} - {description}");
            }
        }

        private static void ShowSystemInfo()
        {
            RetroConsole.Spinner("READING SYSTEM TABLES", 250, ConsoleColor.Cyan);
            Console.WriteLine("\n--- System Information ---");
            Console.WriteLine($"OS Version : SurfOS 2.0");
            Console.WriteLine($"Host PC    : {Import.Variables.machineName}");
            Console.WriteLine($"Install Dir: {Import.Variables.installPath}");
            Console.WriteLine($"Setup Mode : {Import.Variables.setupMode}");
            Console.WriteLine($"Footprint  : {Import.Variables.systemFootprint}");
            Console.WriteLine($"Times Booted: {Import.Variables.numRun}");

            PartitionManifest? partition = Partition_Manager.Load(Import.Variables.installPath);
            if (partition is not null)
            {
                long usedBytes = Partition_Manager.GetUsedBytes(Import.Variables.installPath);
                long usableBytes = Math.Max(
                    0,
                    partition.CapacityBytes - partition.SystemReservedBytes);
                long freeBytes = Math.Max(0, usableBytes - usedBytes);
                Console.WriteLine("\n--- SurfOS Partition ---");
                Console.WriteLine($"Volume      : {partition.Label} ({partition.FileSystem}) mounted at {partition.MountPoint}");
                Console.WriteLine($"Windows FS  : {partition.HostFileSystem}");
                Console.WriteLine($"Capacity    : {FormatPartitionSize(partition.CapacityBytes)}");
                Console.WriteLine($"Used / free : {FormatPartitionSize(usedBytes)} / {FormatPartitionSize(freeBytes)}");
                Console.WriteLine($"Swap reserve: {FormatPartitionSize(partition.SwapBytes)}");
                Console.WriteLine($"Allocation  : {partition.AllocationMode}");
                Console.WriteLine($"Backing file: {partition.BackingFilePath}");
                Console.WriteLine($"Encryption  : {(partition.EncryptionEnabled ? "Enabled (simulated)" : "Disabled")}");
                IReadOnlyList<BackupInfo> backups = Backup_Manager.ListBackups();
                Console.WriteLine($"Backups     : {backups.Count} ({Backup_Manager.FormatSize(backups.Sum(backup => backup.Size))})");
            }
        }

        private static string FormatPartitionSize(long bytes)
        {
            double gb = bytes / (double)Partition_Manager.BytesPerGb;
            return gb >= 1 ? $"{gb:0.00} GB" : $"{bytes / (1024d * 1024d):0.00} MB";
        }

        // ==========================================
        // MULTI-APP UPGRADES SYSTEM IMPLEMENTATION
        // ==========================================

        private static void RunSysMonitor()
        {
            RetroConsole.Spinner("POLLING HARDWARE BUS", 300, ConsoleColor.Cyan);
            TimeSpan uptime = DateTime.Now - Import.Variables.sessionStartTime;
            long processMemory = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024); // Memory in MBs

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n   === SurfOS Performance Dashboard ===");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  Current Active User : {Import.Variables.userName}");
            Console.WriteLine($" ⏳ Active Shell Uptime : {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s");
            Console.WriteLine($"  Allocated OS RAM    : {processMemory} MB");
            Console.WriteLine($"  Logical CPU Cores   : {Environment.ProcessorCount} Threads");
            Console.WriteLine($"  Operating Platform  : {Environment.OSVersion}");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(" ========================================");
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
            else
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
        }

        private static void RunScriptAutomation(string fileName, ref bool isRunning)
        {
            string filePath = Path.Combine(Import.Variables.installPath, fileName);
            if (!File.Exists(filePath))
            {
                Console.WriteLine($" Execution Error: Script target batch '{fileName}' is completely missing.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            RetroConsole.Spinner($"PARSING {fileName}", 300, ConsoleColor.DarkYellow);

            foreach (string macroLine in File.ReadLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(macroLine) || macroLine.Trim().StartsWith("#")) continue; // Skip comments/blanks

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($" Executing script task: {macroLine}");
                Screen_Print.ResetColors();

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
                int nextId = tasks.Count > 0 ? tasks[^1].ID + 1 : 1;
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
            }
            else if (action == "remove" && args.Length > cmdIndex + 2 && int.TryParse(args[cmdIndex + 2], out int remId))
            {
                tasks.RemoveAll(t => t.ID == remId);
                JsonStorage.Write(todoPath, tasks);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($" Task {remId} removed.");
            }
            else
            {
                Console.WriteLine("\n---  To-Do List ---");
                if (tasks.Count == 0) Console.WriteLine("No active tasks found.");
                foreach (var t in tasks)
                {
                    Console.ForegroundColor = t.Done ? ConsoleColor.DarkGray : ConsoleColor.White;
                    Console.WriteLine($"{t.ID}. {(t.Done ? "[X]" : "[ ]")} {t.Task}");
                }
            }
        }

        private static void RunTextEditor(string fileName)
        {
            string filePath = Path.Combine(Import.Variables.installPath, fileName);
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"--- SurfEdit: {fileName} ---");
            Console.WriteLine("Type ':wq' to save and close. Type ':q' to drop changes.");
            Console.WriteLine("-------------------------------------------------------------------------");
            Screen_Print.ResetColors();

            List<string> lines = new List<string>();
            if (File.Exists(filePath))
            {
                foreach (var ln in File.ReadAllLines(filePath))
                {
                    Console.WriteLine(ln);
                    lines.Add(ln);
                }
            }

            while (true)
            {
                string input = Console.ReadLine() ?? string.Empty;
                if (input == ":wq")
                {
                    File.WriteAllLines(filePath, lines);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\n File '{fileName}' saved.");
                    break;
                }
                if (input == ":q") break;
                lines.Add(input);
            }
            Console.Clear();
            Screen_Print.Print_Selected_Package();
        }

        private static void SaveUserDatabase()
        {
            string dbPath = Path.Combine(Import.Variables.installPath, "database.json");
            JsonStorage.Write(dbPath, Import.Variables.userDatabase);
        }

        private static void ConfigureAnimations(string[] args, int commandIndex)
        {
            if (args.Length <= commandIndex + 1)
            {
                Console.WriteLine(
                    $"Retro animations are {(RetroConsole.AnimationsEnabled ? "ON" : "OFF")}.");
                Console.WriteLine("Usage: anim <on|off>");
                return;
            }

            string setting = args[commandIndex + 1].ToLowerInvariant();
            if (setting is not ("on" or "off"))
            {
                Console.WriteLine("Usage: anim <on|off>");
                return;
            }

            RetroConsole.AnimationsEnabled = setting == "on";
            RetroConsole.TypeLine(
                $"RETRO DISPLAY EFFECTS {(RetroConsole.AnimationsEnabled ? "ENABLED" : "DISABLED")}.",
                4);
        }

        private static bool UsesInteractiveScreen(string command)
        {
            return command is "edit" or "code" or "ide" or "uninstall" or "store" or "shop" or "surf" or "surfos";
        }

        private static bool UsesFullScreenApp(
            string command,
            string[] args,
            int cmdIndex,
            bool isSudo,
            bool isScriptExecution)
        {
            return command switch
            {
                "top" or "store" or "shop" => true,
                "edit" => args.Length > cmdIndex + 1,
                "code" or "ide" => CloudRepositoryManager.IsPackageInstalled("surfcode-ide"),
                "uninstall" => isSudo && !isScriptExecution,
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

            // Format the text to stretch across the whole console width
            string status = $"  SurfOS Kernel v3.0  |  User: {Import.Variables.userName}  |  Cloud: ONLINE  |  Uptime: {uptime:hh\\:mm\\:ss}  ";
            Console.WriteLine(status.PadRight(Console.WindowWidth));

            Console.ResetColor();
        }
    }
}
