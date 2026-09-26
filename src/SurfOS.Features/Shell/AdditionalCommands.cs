using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SurfOS2;

internal partial class CLI_Engine
{
    private static readonly Dictionary<string, (string Usage, string Details)> AdditionalUsage = new()
    {
        ["ln"] = ("ln [-s] <target> <link>", "Create a native hard link, or a symbolic link with -s, inside the virtual filesystem."),
        ["chmod"] = ("chmod <mode> <path>", "Persist a three-digit octal virtual mode and update the closest host file permissions."),
        ["chown"] = ("chown <user> <path> --force", "Assign a path to an existing SurfOS profile in virtual filesystem metadata."),
        ["history"] = ("history [clear]", "Show or clear the last 1000 commands in this login session."),
        ["which"] = ("which <command>", "Resolve aliases and shell built-ins."),
        ["alias"] = ("alias [name='command']", "List or define session aliases; quote multiword definitions."),
        ["unalias"] = ("unalias <name>", "Remove a session alias."),
        ["env"] = ("env [name]", "Show shell variables, including HOME, PWD, and USER."),
        ["setenv"] = ("setenv <name> <value>", "Set a session variable. Expand with $NAME or ${NAME}."),
        ["unsetenv"] = ("unsetenv <name>", "Remove a session variable."),
        ["hostname"] = ("hostname [new-name --force]", "Display or persist the SurfOS machine name, without changing the host OS."),
        ["uptime"] = ("uptime", "Show SurfOS session uptime and active simulated process count."),
        ["uname"] = ("uname [-a|-r|-m]", "Show SurfOS runtime and architecture information."),
        ["version"] = ("version", "Show SurfOS version, assembly build, and update channel."),
        ["reboot"] = ("reboot [--force]", "Stop SurfOS services and return through its boot/login sequence."),
        ["sleep"] = ("sleep <seconds>", "Pause a shell or script for 0 through 86400 seconds; decimals supported."),
        ["ip"] = ("ip [addr|route|status]", "Read host interface addresses and default gateways; does not change host networking."),
        ["netstat"] = ("netstat [-a]", "Read host TCP connections; -a adds TCP and UDP listeners."),
        ["dns"] = ("dns lookup <host>", "Resolve a hostname using the host resolver."),
        ["traceroute"] = ("traceroute <host>", "Probe IPv4 hops with ICMP TTL, up to 30 hops."),
        ["curl"] = ("curl <url>", "Fetch HTTP(S) text, with a 30-second timeout and 16 MiB limit."),
        ["wget"] = ("wget <url> [-o file]", "Download HTTP(S) bytes into the virtual filesystem (maximum 16 MiB)."),
        ["network"] = ("network [status|profiles|connect [Private|Public]|disconnect]", "Manage SurfOS's persisted network policy; does not disable host adapters."),
        ["kernel"] = ("kernel [info|modules|config|verify|boot <os-id>]", "Inspect the embedded runtime; external Surf Kernel hosting is unavailable."),
        ["lsmod"] = ("lsmod", "List SurfOS service-backed runtime modules and their state."),
        ["modinfo"] = ("modinfo <module>", "Show information about a service-backed runtime module."),
        ["modload"] = ("modload <module> --force", "Load and enable a service-backed runtime module."),
        ["modunload"] = ("modunload <module> --force", "Stop and disable a service-backed runtime module."),
        ["devices"] = ("devices [list|info <id>]", "Inspect SurfOS storage, host CPU, and host network devices."),
        ["mount"] = ("mount [list|<device> <path>]", "List virtual mappings. Additional mounts require an unavailable mount backend."),
        ["umount"] = ("umount <path>", "Requires a dynamic virtual mount backend, currently unavailable."),
        ["fsck"] = ("fsck [path]", "Read-only virtual traversal and file-length check; does not repair the host filesystem."),
        ["meminfo"] = ("meminfo", "Show host-process memory, managed heap, and simulated SurfOS swap."),
        ["cpuinfo"] = ("cpuinfo", "Show host architecture, logical processor count, and process CPU time."),
        ["hwinfo"] = ("hwinfo", "Combine CPU, memory, and device information."),
        ["os"] = ("os list|info <id>|boot <id>|stop <id>|install <package>|remove <id> --force", "Show SurfOS; managing other systems requires the unavailable Surf Kernel bridge."),
        ["sandbox"] = ("sandbox create <name>|list|run <name>|delete <name> --force", "Manage local staging folders. Enforced execution isolation is unavailable."),
        ["validate"] = ("validate <builder-project-id|package>", "Validate a builder project or a SurfOS .surfpkg archive without executing it."),
        ["publish"] = ("publish <builder-project|package.surfpkg> [--public|--private]", "Validate and upload an app to the configured SurfCloud publisher. Private is the default."),
        ["surfcloud"] = ("surfcloud status|signin|signout", "Manage the verified SurfCloud session. signin reads SURFCLOUD_TOKEN from the process environment."),
        ["sign"] = ("sign <package> | sign verify <package>", "Create or verify a detached RSA-SHA256 signature using a local signing key.")
    };

    static CLI_Engine()
    {
        foreach (var entry in AdditionalUsage)
            ((Dictionary<string, (string Usage, string Details)>)CommandUsage).Add(entry.Key, entry.Value);
    }

    private static void Require(bool condition, string usage)
    { if (!condition) throw new ArgumentException("Usage: " + usage); }
    private static void RequireForce(bool forced)
    { if (!forced) throw new UnauthorizedAccessException("An administrator and trailing --force are required."); }
    private static void BackendUnavailable(string feature) => ShellError($"{feature}: backend unavailable in standalone SurfOS. No changes were made.", 69);
    private static void UpdateOptions(Action<Import.SystemOptions> update)
    {
        string path = Path.Combine(Import.Variables.installPath, "options.json");
        var options = File.Exists(path) ? JsonStorage.Read<Import.SystemOptions>(path) ?? new() : new Import.SystemOptions();
        update(options); JsonStorage.Write(path, options);
    }

    private static bool TryExecuteAdditional(string command, string[] args, bool forced, ref bool running)
    {
        if (!AdditionalUsage.ContainsKey(command)) return false;
        string usage = AdditionalUsage[command].Usage;
        try
        {
            switch (command)
            {
                case "surfcloud":
                    Require(args.Length == 1 && args[0] is "status" or "signin" or "signout", usage);
                    if (args[0] == "signout")
                    {
                        Cloud_Manager.SignOut();
                        Console.WriteLine("SurfCloud signed out. Private session state cleared.");
                    }
                    else if (args[0] == "status")
                        Console.WriteLine(Cloud_Manager.VerifiedSubject is null ? "SurfCloud: signed out or offline." : "SurfCloud: verified session active.");
                    else
                    {
                        string? idToken = Environment.GetEnvironmentVariable("SURFCLOUD_TOKEN");
                        bool signedIn = !string.IsNullOrWhiteSpace(idToken) &&
                            Cloud_Manager.SignInAsync(idToken, CancellationToken.None).GetAwaiter().GetResult();
                        Console.WriteLine(signedIn ? "SurfCloud identity verified." : "SurfCloud sign-in unavailable or token rejected.");
                    }
                    break;
                case "history":
                    Require(args.Length == 0 || args is ["clear"], usage);
                    if (args.Length > 0) History.Clear();
                    else for (int i = 0; i < History.Count; i++) Console.WriteLine($"{i + 1,4}  {History[i]}");
                    break;
                case "which":
                    Require(args.Length == 1, usage);
                    if (Aliases.TryGetValue(args[0], out string? alias)) Console.WriteLine($"{args[0]}: alias for {alias}");
                    else if (CommandUsage.ContainsKey(CanonicalCommand(args[0]))) Console.WriteLine($"builtin:{CanonicalCommand(args[0])}");
                    else ShellError($"which: command not found: {args[0]}");
                    break;
                case "alias":
                    Require(args.Length <= 1, usage);
                    if (args.Length == 0) foreach (var item in Aliases.OrderBy(x => x.Key)) Console.WriteLine($"{item.Key}='{item.Value}'");
                    else
                    {
                        int equals = args[0].IndexOf('=');
                        Require(equals > 0 && equals < args[0].Length - 1, usage);
                        string name = args[0][..equals];
                        Require(Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_-]*$"), usage);
                        Aliases[name] = args[0][(equals + 1)..];
                    }
                    break;
                case "unalias":
                    Require(args.Length == 1, usage);
                    if (!Aliases.Remove(args[0])) ShellError($"unalias: no alias named {args[0]}");
                    break;
                case "env":
                    Require(args.Length <= 1, usage);
                    if (args.Length == 1)
                    {
                        if (!EnvironmentVariables.ContainsKey(args[0]) && args[0] is not ("HOME" or "PWD" or "USER")) ShellError($"env: variable not found: {args[0]}");
                        else Console.WriteLine(EnvironmentValue(args[0]));
                    }
                    else foreach (string name in EnvironmentVariables.Keys.Concat(["HOME", "PWD", "USER"]).Order()) Console.WriteLine($"{name}={EnvironmentValue(name)}");
                    break;
                case "setenv":
                case "unsetenv":
                    Require(args.Length == (command == "setenv" ? 2 : 1), usage);
                    Require(Regex.IsMatch(args[0], "^[A-Za-z_][A-Za-z0-9_]*$") && args[0] is not ("HOME" or "PWD" or "USER"), "HOME, PWD, and USER are read-only; use a valid variable name.");
                    if (command == "setenv") EnvironmentVariables[args[0]] = args[1];
                    else if (!EnvironmentVariables.Remove(args[0])) ShellError($"unsetenv: variable not found: {args[0]}");
                    break;
                case "hostname":
                    Require(args.Length <= 1, usage);
                    if (args.Length == 0) Console.WriteLine(Import.Variables.machineName);
                    else
                    {
                        RequireForce(forced);
                        Require(Regex.IsMatch(args[0], "^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$"), usage);
                        UpdateOptions(o => o.MachineName = args[0]); Import.Variables.machineName = args[0];
                        Console.WriteLine(args[0]);
                    }
                    break;
                case "uptime":
                    Require(args.Length == 0, usage);
                    Console.WriteLine($"SurfOS session: {DateTime.Now - Import.Variables.sessionStartTime:c}; {ProcessManager.ListProcesses().Count} active simulated processes. Host load average unavailable.");
                    break;
                case "uname":
                    Require(args.Length == 0 || args.Length == 1 && args[0] is "-a" or "-r" or "-m", usage);
                    Console.WriteLine(args.FirstOrDefault() switch { "-m" => RuntimeInformation.ProcessArchitecture.ToString(), "-r" => Import.Variables.version, "-a" => $"SurfOS {Import.Variables.version} {RuntimeInformation.ProcessArchitecture} .NET {Environment.Version} (standalone runtime)", _ => "SurfOS" });
                    break;
                case "version":
                    Require(args.Length == 0, usage);
                    Console.WriteLine($"SurfOS {Import.Variables.version}; assembly {typeof(CLI_Engine).Assembly.GetName().Version}; channel {Import.Variables.updateChannel}; .NET {Environment.Version}. External Surf Kernel: not connected.");
                    break;
                case "reboot":
                    Require(args.Length == 0, usage);
                    ProcessManager.StopAllUserProcesses(); ServiceManager.StopAll();
                    SurfOsApplication.RebootRequested = true; running = false;
                    ResetShellSession(); Console.WriteLine("Restarting SurfOS...");
                    break;
                case "sleep":
                    Require(args.Length == 1 && double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && double.IsFinite(seconds) && seconds >= 0 && seconds <= 86400, usage);
                    Thread.Sleep(TimeSpan.FromSeconds(double.Parse(args[0], CultureInfo.InvariantCulture)));
                    break;
                case "ip": case "netstat": case "dns": case "traceroute": case "curl": case "wget": case "network":
                    ExecuteNetwork(command, args); break;
                case "cpuinfo":
                    Require(args.Length == 0, usage); PrintCpuInfo(); break;
                case "meminfo":
                    Require(args.Length == 0, usage); PrintMemoryInfo(); break;
                case "hwinfo":
                    Require(args.Length == 0, usage); PrintCpuInfo(); PrintMemoryInfo(); PrintDevices([]); break;
                case "devices": PrintDevices(args); break;
                case "fsck":
                    Require(args.Length <= 1, usage);
                    int entries = 0; long bytes = 0;
                    foreach (var node in VirtualFileSystem.Walk(args.FirstOrDefault() ?? "/", true)) { entries++; bytes = checked(bytes + node.Info.Size); }
                    Console.WriteLine($"Virtual traversal passed: {entries} entries, {bytes} file bytes. Read-only; host filesystem integrity is not tested.");
                    break;
                case "ln":
                    Require(args.Length == 2 || args.Length == 3 && args[0] == "-s", usage);
                    bool symbolic = args.Length == 3;
                    int offset = symbolic ? 1 : 0;
                    if (!VirtualFileSystem.CreateLink(args[offset], args[offset + 1], symbolic, out string linkError)) ShellError($"ln: {linkError}");
                    break;
                case "chmod":
                    Require(args.Length == 2 && Regex.IsMatch(args[0], "^[0-7]{3}$"), usage);
                    int mode = Convert.ToInt32(args[0], 8);
                    if (!VirtualFileSystem.ChangeMode(args[1], mode, out string modeError)) ShellError($"chmod: {modeError}");
                    break;
                case "chown":
                    Require(args.Length == 2, usage); RequireForce(forced);
                    Require(FindUser(args[0]) is not null, $"Unknown SurfOS profile: {args[0]}");
                    if (!VirtualFileSystem.ChangeOwner(args[1], args[0], out string ownerError)) ShellError($"chown: {ownerError}");
                    break;
                case "kernel": ExecuteKernel(args); break;
                case "os": ExecuteOs(args, forced); break;
                case "sandbox": ExecuteSandbox(args, forced); break;
                case "validate":
                    Require(args.Length == 1, usage); ValidateBuild(args[0]); break;
                case "publish":
                    ExecutePublish(args); break;
                case "sign": ExecuteSign(args); break;
                case "lsmod":
                    Require(args.Length == 0, usage);
                    Console.WriteLine("Module                    State      Autostart");
                    foreach (ServiceState module in ServiceManager.ListServices())
                        Console.WriteLine($"{module.Name,-25} {(module.IsRunning ? "loaded" : "unloaded"),-10} {(module.AutoStart ? "yes" : "no")}");
                    break;
                case "modinfo":
                    Require(args.Length == 1, usage);
                    ServiceState? moduleInfo = ServiceManager.GetStatus(args[0]);
                    if (moduleInfo is null) ShellError($"modinfo: module not found: {args[0]}");
                    else Console.WriteLine($"Name: {moduleInfo.Name}\nDescription: {moduleInfo.Description}\nState: {(moduleInfo.IsRunning ? "loaded" : "unloaded")}\nEnabled: {moduleInfo.Enabled}\nAutostart: {moduleInfo.AutoStart}\nStatus: {moduleInfo.Status}");
                    break;
                case "modload":
                case "modunload":
                    Require(args.Length == 1, usage); RequireForce(forced);
                    bool changed = command == "modload"
                        ? ServiceManager.StartService(args[0])
                        : ServiceManager.StopService(args[0]);
                    if (!changed) ShellError($"{command}: module not found: {args[0]}");
                    else Console.WriteLine($"{args[0]} {(command == "modload" ? "loaded" : "unloaded")}.");
                    break;
                case "mount":
                    Require(args.Length == 0 || args is ["list"] || args.Length == 2, usage);
                    if (args.Length == 2) BackendUnavailable("Dynamic virtual mounts");
                    else Console.WriteLine("/ -> SurfOS installation\n/home -> home\n/system -> system\n/apps -> apps\n/projects -> Projects\n/themes -> Packages\n/music -> music\n/logs -> logs\n/temp -> temp\n/mail -> mail\nBuilt-in mappings share one partition.");
                    break;
                default:
                    int required = 1;
                    Require(args.Length == required, usage);
                    if (command is "chown" or "modload" or "modunload") RequireForce(forced);
                    BackendUnavailable(command); break;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or FormatException or System.Net.Sockets.SocketException or System.Net.NetworkInformation.NetworkInformationException or System.Net.NetworkInformation.PingException or System.Security.Cryptography.CryptographicException or HttpRequestException or OperationCanceledException)
        { ShellError($"{command}: {ex.Message}", ex is ArgumentException ? 2 : 1); }
        return true;
    }

    private static void PrintCpuInfo()
    {
        using Process process = Process.GetCurrentProcess();
        Console.WriteLine($"Host CPU: {RuntimeInformation.OSArchitecture}; runtime: {RuntimeInformation.ProcessArchitecture}; logical processors: {Environment.ProcessorCount}; SurfOS process CPU: {process.TotalProcessorTime:c}");
    }
    private static void PrintMemoryInfo()
    {
        using Process process = Process.GetCurrentProcess(); var swap = Swap_Manager.GetSnapshot();
        Console.WriteLine($"Host process working set: {process.WorkingSet64} bytes\nManaged heap: {GC.GetTotalMemory(false)} bytes\nGC available memory: {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes} bytes\nSimulated swap: {swap.UsedBytes}/{swap.CapacityBytes} bytes; page-in: {swap.PageInBytes}; page-out: {swap.PageOutBytes}");
    }
    private static void PrintDevices(string[] args)
    {
        Require(args.Length == 0 || args is ["list"] || args.Length == 2 && args[0] == "info", AdditionalUsage["devices"].Usage);
        Dictionary<string, string> devices = new() { ["cpu"] = $"Host CPU: {Environment.ProcessorCount} logical processors, {RuntimeInformation.ProcessArchitecture}", ["disk"] = $"SurfOS storage: {Import.Variables.partitionLabel}; {Import.Variables.partitionFileSystem}" };
        foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()) devices["net:" + nic.Id] = $"Host NIC: {nic.Name}; {nic.OperationalStatus}; {nic.NetworkInterfaceType}";
        if (args.Length == 2)
        { if (devices.TryGetValue(args[1], out string? value)) Console.WriteLine(value); else ShellError($"devices: unknown device {args[1]}"); }
        else foreach (var device in devices) Console.WriteLine($"{device.Key}: {device.Value}");
    }
    private static void ExecuteKernel(string[] args)
    {
        string action = args.FirstOrDefault() ?? "info";
        Require(args.Length <= 1 && action is "info" or "modules" or "config" or "verify" || args.Length == 2 && action == "boot", AdditionalUsage["kernel"].Usage);
        if (action == "boot") { BackendUnavailable("Surf Kernel OS hosting bridge"); return; }
        if (action == "modules")
        {
            Console.WriteLine("SurfOS runtime modules (service-backed):");
            foreach (ServiceState module in ServiceManager.ListServices())
                Console.WriteLine($"{module.Name,-25} {(module.IsRunning ? "loaded" : "unloaded")}");
            return;
        }
        if (action == "config") { Console.WriteLine($"Runtime: standalone\nSafe mode: {Import.Variables.safeMode}\nNetwork policy: {Import.Variables.networkProfile}\nChannel: {Import.Variables.updateChannel}"); return; }
        if (action == "verify")
        {
            string assemblyPath = Path.Combine(
                AppContext.BaseDirectory,
                typeof(CLI_Engine).Assembly.GetName().Name + ".dll");
            string runtimePath = File.Exists(assemblyPath)
                ? assemblyPath
                : Environment.ProcessPath ?? throw new IOException("The runtime executable path is unavailable.");
            Console.WriteLine($"Runtime SHA-256: {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(runtimePath)))}");
            Console.WriteLine("Checksum only. No trusted kernel baseline is configured."); LastExitCode = 69; return;
        }
        Console.WriteLine($"Standalone SurfOS {Import.Variables.version} on .NET {Environment.Version}. External Surf Kernel is not connected.");
    }
    private static void ExecuteOs(string[] args, bool forced)
    {
        Require(args is ["list"] || args.Length == 2 && args[0] is "info" or "boot" or "stop" or "install" or "remove", AdditionalUsage["os"].Usage);
        if (args[0] == "list" || args[0] == "info" && args[1].Equals("surfos", StringComparison.OrdinalIgnoreCase))
        { Console.WriteLine($"surfos  {Import.Variables.version}  running (standalone application)"); return; }
        if (args[0] == "remove") RequireForce(forced);
        BackendUnavailable("Surf Kernel OS hosting bridge");
    }
    private static void ExecuteSandbox(string[] args, bool forced)
    {
        Require(args is ["list"] || args.Length == 2 && args[0] is "create" or "run" or "delete", AdditionalUsage["sandbox"].Usage);
        string parent = "~/sandboxes";
        if (args[0] == "list")
        {
            var entries = VirtualFileSystem.List(parent, out string error);
            if (error.Length > 0) { Console.WriteLine("No staging sandboxes."); return; }
            foreach (var entry in entries.Where(e => e.Type == "dir")) Console.WriteLine(entry.Name + " (staging only; execution isolation unavailable)");
            return;
        }
        Require(PathSafety.IsSafeFileName(args[1]), "sandbox <action> <simple-name>");
        if (args[0] == "run") { BackendUnavailable("Enforced sandbox execution"); return; }
        string path = parent + "/" + args[1];
        if (args[0] == "delete")
        {
            RequireForce(forced);
            // Validate every descendant before allowing recursive deletion.
            foreach (var item in VirtualFileSystem.Walk(path, true)) { }
            if (!VirtualFileSystem.RemoveDirectory(path, true, out string error)) ShellError(error);
        }
        else if (!VirtualFileSystem.CreateDirectory(path, out string error)) ShellError(error);
        else Console.WriteLine($"Created {path} (staging directory; no execution isolation).");
    }
}
