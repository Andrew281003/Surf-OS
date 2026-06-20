using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;


namespace SurfOS2
{
    internal class CLI_Engine
    {
        private static readonly IReadOnlyDictionary<string, int> ShopCatalog =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["VIP"] = 200,
                ["Hacker"] = 500,
                ["Coder"] = 800,
                ["SysAdmin"] = 1500
            };

        [SupportedOSPlatform("windows")]
        public static void StartTerminal()
        {
            Console.Clear();
            Time_Manager.StartAlarmDaemon();
            Screen_Print.Print_Selected_Package();
            RetroConsole.TypeLine("\nCOMMAND PROCESSOR READY", 4);
            RetroConsole.TypeLine("Type 'help' to see a list of commands.", 2);

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

        // 🌟 NEW: Process Router lets the normal prompt AND script engines run commands identically!
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
            using IDisposable? commandOutputAnimation =
                UsesInteractiveScreen(mainCommand)
                    ? null
                    : RetroConsole.BeginCommandOutput();

            switch (mainCommand)
            {
                case "help":
                    ShowHelp();
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
                        Console.WriteLine($"✅ Font size successfully changed to {newSize}.");
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

                // 🌟 NEW APP: System & Specs Monitor (Neofetch style!)
                case "sys":
                    RunSysMonitor();
                    break;

                // 🌟 NEW APP: Virtual Coin Mining Simulator
                case "mine":
                    RunMiningSimulator();
                    break;

                // 🌟 NEW APP: Economy Shop Engine
                case "shop":
                    RunEconomyShop(commandParts, cmdIndex);
                    break;

                // 🌟 NEW APP: Internal Multi-User Mailbox System
                case "mail":
                    RunMailSystem(commandParts, cmdIndex);
                    break;

                // 🌟 NEW APP: Script Automation Runner
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

                case "whoami":
                    Import.DatabaseRecord? currentProfile = GetCurrentUser();
                    Console.WriteLine($"Current User: {Import.Variables.userName}");
                    Console.WriteLine($"Rank/Status : {currentProfile?.CurrentRank ?? "User"}");
                    Console.WriteLine($"Wallet      : {currentProfile?.SurfCoins ?? 0} SurfCoins 🪙");
                    Console.WriteLine($"Permission  : Admin ({Import.Variables.uuid})");
                    if (isSudo) Console.WriteLine("Sudo status : GRANTED 👑");
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

                // 🌟 FIX: Fully functional user tracking control branch
                case "user":
                    {
                        // If they typed just "user", default the action to "list" so it doesn't stay blank!
                        string action = commandParts.Length > cmdIndex + 1
                            ? commandParts[cmdIndex + 1].ToLowerInvariant()
                            : "list";

                        // ACTION: ADD USER
                        if (action == "add")
                        {
                            if (commandParts.Length > cmdIndex + 3)
                            {
                                if (!isSudo)
                                {
                                    Console.WriteLine("🚨 You must use 'sudo' to add new users!");
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
                                        SurfCoins = 0,
                                        CurrentRank = "User",
                                        Mailbox = new List<Import.MailMessage>()
                                    });

                                    SaveUserDatabase();
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"✅ New user '{newName}' created successfully!");
                                }
                            }
                            else
                            {
                                Console.WriteLine("Usage: sudo user add <username> <password>");
                            }
                        }
                        // ACTION: REMOVE USER
                        else if (action == "remove")
                        {
                            if (commandParts.Length > cmdIndex + 2)
                            {
                                if (!isSudo)
                                {
                                    Console.WriteLine("🚨 You must use 'sudo' to remove users!");
                                    break;
                                }

                                string targetName = commandParts[cmdIndex + 2];
                                if (targetName.Equals(
                                    Import.Variables.userName,
                                    StringComparison.OrdinalIgnoreCase))
                                {
                                    Console.WriteLine("🚨 Safety Lock: You cannot delete your own account while logged into it!");
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
                                        Console.WriteLine($"🗑️ User '{targetName}' removed!");
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
                        // ACTION: LIST USERS
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
                        // UNRECOGNIZED ACTION
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
                                    Console.WriteLine("🚨 Core system themes cannot be deleted without 'sudo'!");
                                else
                                {
                                    File.Delete(packagePath);
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"🗑️ Theme '{targetTheme}' was successfully removed!");
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
                                Console.WriteLine($"✅ '{targetTheme}' is now your default boot theme.");
                                Screen_Print.ResetColors();
                            }
                            else Console.WriteLine($"[SurfOS] Theme pack '{targetTheme}' does not exist.");
                        }
                    }
                    break;

                case "logout":
                    isRunning = false;
                    break;

                case "exit":
                case "shutdown":
                    RetroConsole.ShutdownSequence();
                    Environment.Exit(0);
                    break;

                case "startx":
                case "gui":
                    Desktop_Environment.StartGUI();
                    break;

                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Command not recognized: '{mainCommand}'. Type 'help' for a list of commands.");
                    break;
            }
            Screen_Print.ResetColors();
        }

        private static void ShowHelp()
        {
            Console.WriteLine("\n--- SurfOS Available Commands ---");
            Console.WriteLine("  help      - Shows this menu");
            Console.WriteLine("  clear     - Clears terminal view");
            Console.WriteLine("  anim      - Enables or disables retro animations");
            Console.WriteLine("  sys       - Displays hardware tracking dashboard");
            Console.WriteLine("  mine      - Mine virtual SurfCoins 🪙");
            Console.WriteLine("  shop      - Purchase rank flair (shop view, shop buy <name>)");
            Console.WriteLine("  mail      - Send/read profile notifications (mail list, mail send <user> <text>)");
            Console.WriteLine("  run       - Runs macro sequence script profiles");
            Console.WriteLine("  fontsize  - Scale text engine bounds");
            Console.WriteLine("  clock     - Displays clock stats");
            Console.WriteLine("  calendar  - Displays highlighted date grids");
            Console.WriteLine("  alarm     - Configure alerts background tasks");
            Console.WriteLine("  whoami    - Session metrics ledger");
            Console.WriteLine("  ping      - Check hardware latencies");
            Console.WriteLine("  todo      - Target roadmap items log");
            Console.WriteLine("  edit      - Open text utility editor");
            Console.WriteLine("  user      - Profile account directory controls");
            Console.WriteLine("  theme     - System styling components");
            Console.WriteLine("  startx    - Boots the visual Desktop Environment GUI");
            Console.WriteLine("  logout    - Locks active shell space");
            Console.WriteLine("  shutdown  - Halts runtime operations");
        }

        private static void ShowSystemInfo()
        {
            RetroConsole.Spinner("READING SYSTEM TABLES", 250, ConsoleColor.Cyan);
            Console.WriteLine("\n--- System Information ---");
            Console.WriteLine($"OS Version : SurfOS 2.0");
            Console.WriteLine($"Host PC    : {Import.Variables.machineName}");
            Console.WriteLine($"Install Dir: {Import.Variables.installPath}");
            Console.WriteLine($"Times Booted: {Import.Variables.numRun}");
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
            Console.WriteLine("\n 🖥️  === SurfOS Performance Dashboard ===");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($" 👤 Current Active User : {Import.Variables.userName}");
            Console.WriteLine($" ⏳ Active Shell Uptime : {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s");
            Console.WriteLine($" 🧠 Allocated OS RAM    : {processMemory} MB");
            Console.WriteLine($" ⚙️ Logical CPU Cores   : {Environment.ProcessorCount} Threads");
            Console.WriteLine($" 📁 Operating Platform  : {Environment.OSVersion}");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(" ========================================");
        }

        private static void RunMiningSimulator()
        {
            Import.DatabaseRecord? user = GetCurrentUser();
            if (user == null) return;

            Console.Write("⛏️ Mining sequence initiating...");
            Console.WriteLine();
            RetroConsole.ProgressBar("COMPUTING HASH BLOCK", 24, 28);
            int mined = Random.Shared.Next(10, 50);

            user.SurfCoins += mined;
            SaveUserDatabase();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n💎 Success! You mined +{mined} SurfCoins!");
            Console.ResetColor();
            Console.WriteLine($"Wallet balance: {user.SurfCoins} SurfCoins 🪙");
        }

        private static void RunEconomyShop(string[] args, int cmdIndex)
        {
            Import.DatabaseRecord? user = GetCurrentUser();
            if (user == null) return;

            string action = args.Length > cmdIndex + 1
                ? args[cmdIndex + 1].ToLowerInvariant()
                : "view";

            if (action == "buy" && args.Length > cmdIndex + 2)
            {
                string item = args[cmdIndex + 2];
                if (ShopCatalog.TryGetValue(item, out int price))
                {
                    if (user.SurfCoins >= price)
                    {
                        user.SurfCoins -= price;
                        user.CurrentRank = item; // Overwrite current profile flair rank
                        SaveUserDatabase();
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"👑 Congratulations! You bought the [{item}] rank title!");
                    }
                    else Console.WriteLine($"🚨 Core Failure: Insufficient funds! You need {price} SurfCoins.");
                }
                else Console.WriteLine("Rank item not found in catalog listing.");
            }
            else
            {
                Console.WriteLine("\n🛒 === SurfOS Custom Rank Boutique ===");
                Console.WriteLine($"Your balance: {user.SurfCoins} SurfCoins 🪙\n");
                foreach (var product in ShopCatalog)
                {
                    Console.WriteLine($" - Rank: [{product.Key}] {new string(' ', 10 - product.Key.Length)} Cost: {product.Value} SurfCoins");
                }
                Console.WriteLine("\nUsage: shop buy <RankName>");
            }
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
                    Console.WriteLine($"🚨 Mail failure: Destination account '{recipientName}' could not be resolved.");
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
                Console.WriteLine($"📬 Message routed successfully to '{recipientName}' storage banks.");
            }
            else if (action == "clear")
            {
                currentUser.Mailbox.Clear();
                SaveUserDatabase();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("🗑️ Mailbox cleared clean.");
            }
            else
            {
                Console.WriteLine($"\n📩 === {Import.Variables.userName}'s Inbox Bank ===");
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
                Console.WriteLine($"🚨 Execution Error: Script target batch '{fileName}' is completely missing.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            RetroConsole.Spinner($"PARSING {fileName}", 300, ConsoleColor.DarkYellow);

            foreach (string macroLine in File.ReadLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(macroLine) || macroLine.Trim().StartsWith("#")) continue; // Skip comments/blanks

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"⚡ Executing script task: {macroLine}");
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
                Console.WriteLine($"✅ Added task: {taskDesc}");
            }
            else if (action == "complete" && args.Length > cmdIndex + 2 && int.TryParse(args[cmdIndex + 2], out int compId))
            {
                var task = tasks.Find(t => t.ID == compId);
                if (task != null)
                {
                    task.Done = true;
                    JsonStorage.Write(todoPath, tasks);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✅ Task {compId} marked complete!");
                }
            }
            else if (action == "remove" && args.Length > cmdIndex + 2 && int.TryParse(args[cmdIndex + 2], out int remId))
            {
                tasks.RemoveAll(t => t.ID == remId);
                JsonStorage.Write(todoPath, tasks);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"🗑️ Task {remId} removed.");
            }
            else
            {
                Console.WriteLine("\n--- 📝 To-Do List ---");
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
                    Console.WriteLine($"\n💾 File '{fileName}' saved.");
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
            return command is "edit" or "gui" or "startx";
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
