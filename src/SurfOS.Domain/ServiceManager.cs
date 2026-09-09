using System.Diagnostics;
using System.Runtime.Versioning;

namespace SurfOS2;

internal interface ISurfService
{
    string Name { get; }
    string Description { get; }
    TimeSpan Interval { get; }
    Task ExecuteAsync(CancellationToken cancellationToken);
}

internal sealed class ServiceSettings
{
    public Dictionary<string, ServiceSetting> Services { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class ServiceSetting
{
    public bool Enabled { get; set; }
    public bool AutoStart { get; set; }
}

internal sealed class ServiceState
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsRunning { get; init; }
    public bool Enabled { get; init; }
    public bool AutoStart { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? LastHeartbeat { get; init; }
    public string Status { get; init; } = "Stopped";
    public string LastError { get; init; } = string.Empty;
}

internal static class ServiceManager
{
    private sealed class ServiceRuntime(ISurfService service)
    {
        public ISurfService Service { get; } = service;
        public CancellationTokenSource? Cancellation { get; set; }
        public Task? Task { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? LastHeartbeat { get; set; }
        public string LastError { get; set; } = string.Empty;
    }

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, ServiceRuntime> Services =
        new(StringComparer.OrdinalIgnoreCase);

    private static ServiceSettings Settings = new();
    private static bool _initialized;

    public static void Initialize()
    {
        lock (SyncRoot)
        {
            RegisterBuiltIns();
            LoadSettings();
            _initialized = true;
            KernelLog.Success("service", "service manager initialized");
        }
    }

    public static bool Register(ISurfService service)
    {
        EnsureSettingsContainer();

        lock (SyncRoot)
        {
            bool added = Services.TryAdd(service.Name, new ServiceRuntime(service));
            if (added)
            {
                KernelLog.Info("service", $"registered {service.Name}");
            }
            Settings.Services.TryAdd(service.Name, new ServiceSetting
            {
                Enabled = true,
                AutoStart = true
            });

            if (_initialized)
            {
                SaveSettings();
            }

            return added;
        }
    }

    public static void StartAutostartServices()
    {
        EnsureInitialized();
        KernelLog.Info("service", "starting autostart services");

        foreach (ServiceState service in ListServices())
        {
            if (service.Enabled && service.AutoStart)
            {
                StartService(service.Name, persist: false);
            }
        }
    }

    public static void StartSafeModeServices()
    {
        EnsureInitialized();
        KernelLog.Warning("service", "safe mode service profile active");

        StartService("ClockService", persist: false);
    }

    public static bool StartService(string name, bool persist = true)
    {
        EnsureInitialized();

        ServiceRuntime runtime;
        lock (SyncRoot)
        {
            if (!Services.TryGetValue(name, out runtime!))
            {
                KernelLog.Error("service", $"start failed: {name} not registered");
                return false;
            }

            if (IsRunning(runtime))
            {
                KernelLog.Info("service", $"{runtime.Service.Name} already running");
                return true;
            }

            runtime.Cancellation?.Dispose();
            runtime.Cancellation = new CancellationTokenSource();
            runtime.StartedAt = DateTime.Now;
            runtime.LastHeartbeat = null;
            runtime.LastError = string.Empty;

            if (persist)
            {
                ServiceSetting setting = GetSetting(runtime.Service.Name);
                setting.Enabled = true;
                setting.AutoStart = true;
                SaveSettings();
            }

            CancellationToken token = runtime.Cancellation.Token;
            runtime.Task = Task.Run(() => RunServiceLoopAsync(runtime, token), CancellationToken.None);
            KernelLog.Success("service", $"started {runtime.Service.Name}");
            return true;
        }
    }

    public static bool StopService(string name, bool persist = true)
    {
        EnsureInitialized();

        ServiceRuntime runtime;
        lock (SyncRoot)
        {
            if (!Services.TryGetValue(name, out runtime!))
            {
                KernelLog.Error("service", $"stop failed: {name} not registered");
                return false;
            }

            if (persist)
            {
                ServiceSetting setting = GetSetting(runtime.Service.Name);
                setting.Enabled = false;
                setting.AutoStart = false;
                SaveSettings();
            }
        }

        StopRuntime(runtime);
        KernelLog.Success("service", $"stopped {runtime.Service.Name}");
        return true;
    }

    public static bool RestartService(string name)
    {
        EnsureInitialized();

        if (!StopService(name, persist: false))
        {
            return false;
        }

        KernelLog.Info("service", $"restarting {name}");
        return StartService(name);
    }

    public static IReadOnlyList<ServiceState> ListServices()
    {
        EnsureInitialized();

        lock (SyncRoot)
        {
            return Services.Values
                .OrderBy(runtime => runtime.Service.Name, StringComparer.OrdinalIgnoreCase)
                .Select(CreateState)
                .ToList();
        }
    }

    public static ServiceState? GetStatus(string name)
    {
        EnsureInitialized();

        lock (SyncRoot)
        {
            return Services.TryGetValue(name, out ServiceRuntime? runtime)
                ? CreateState(runtime)
                : null;
        }
    }

    public static void StopAll()
    {
        EnsureInitialized();

        List<ServiceRuntime> runtimes;
        lock (SyncRoot)
        {
            runtimes = Services.Values.ToList();
        }

        foreach (ServiceRuntime runtime in runtimes)
        {
            StopRuntime(runtime);
        }
    }

    private static async Task RunServiceLoopAsync(
        ServiceRuntime runtime,
        CancellationToken cancellationToken)
    {
        try
        {
            await runtime.Service.ExecuteAsync(cancellationToken);
            runtime.LastHeartbeat = DateTime.Now;

            using PeriodicTimer timer = new(runtime.Service.Interval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await runtime.Service.ExecuteAsync(cancellationToken);
                runtime.LastHeartbeat = DateTime.Now;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            runtime.LastError = ex.Message;
            KernelLog.Error("service", $"{runtime.Service.Name} faulted: {ex}");
            Debug.WriteLine($"[Service:{runtime.Service.Name}] {ex}");
        }
    }

    private static void StopRuntime(ServiceRuntime runtime)
    {
        CancellationTokenSource? cancellation;
        Task? task;

        lock (SyncRoot)
        {
            cancellation = runtime.Cancellation;
            task = runtime.Task;
        }

        if (cancellation is null)
        {
            return;
        }

        KernelLog.Info("service", $"stopping {runtime.Service.Name}");

        try
        {
            cancellation.Cancel();
            task?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException ex) when (
            ex.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
        }
        finally
        {
            lock (SyncRoot)
            {
                runtime.Cancellation?.Dispose();
                runtime.Cancellation = null;
                runtime.Task = null;
                runtime.StartedAt = null;
            }
        }
    }

    private static ServiceState CreateState(ServiceRuntime runtime)
    {
        ServiceSetting setting = GetSetting(runtime.Service.Name);
        bool running = IsRunning(runtime);
        string status = running ? "Running" : "Stopped";

        if (!running && runtime.Task is { IsFaulted: true })
        {
            status = "Faulted";
        }
        else if (!string.IsNullOrWhiteSpace(runtime.LastError))
        {
            status = running ? "Running with errors" : "Stopped with errors";
        }

        return new ServiceState
        {
            Name = runtime.Service.Name,
            Description = runtime.Service.Description,
            IsRunning = running,
            Enabled = setting.Enabled,
            AutoStart = setting.AutoStart,
            StartedAt = runtime.StartedAt,
            LastHeartbeat = runtime.LastHeartbeat,
            Status = status,
            LastError = runtime.LastError
        };
    }

    private static bool IsRunning(ServiceRuntime runtime)
    {
        return runtime.Task is { IsCompleted: false } &&
               runtime.Cancellation is { IsCancellationRequested: false };
    }

    private static void RegisterBuiltIns()
    {
        Register(new ClockService());
        Register(new AlarmService());
        Register(new MailService());
        Register(new CloudPresenceService());
        Register(new ThemeWatcherService());
    }

    private static void LoadSettings()
    {
        string path = GetSettingsPath();
        if (File.Exists(path))
        {
            Settings = JsonStorage.Read<ServiceSettings>(path) ?? new ServiceSettings();
        }

        foreach (string serviceName in Services.Keys)
        {
            Settings.Services.TryAdd(serviceName, new ServiceSetting
            {
                Enabled = true,
                AutoStart = true
            });
        }

        SaveSettings();
    }

    private static ServiceSetting GetSetting(string serviceName)
    {
        if (!Settings.Services.TryGetValue(serviceName, out ServiceSetting? setting))
        {
            setting = new ServiceSetting { Enabled = true, AutoStart = true };
            Settings.Services[serviceName] = setting;
        }

        return setting;
    }

    private static void SaveSettings()
    {
        string path = GetSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);
        JsonStorage.Write(path, Settings);
    }

    private static string GetSettingsPath()
    {
        string root = string.IsNullOrWhiteSpace(Import.Variables.installPath)
            ? Environment.CurrentDirectory
            : Import.Variables.installPath;

        return Path.Combine(root, "services.json");
    }

    private static void EnsureSettingsContainer()
    {
        Settings.Services ??= new Dictionary<string, ServiceSetting>(
            StringComparer.OrdinalIgnoreCase);
    }

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        Initialize();
    }
}

internal sealed class ClockService : ISurfService
{
    public string Name => "ClockService";
    public string Description => "Keeps timezone-aware OS clock state warm.";
    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _ = Time_Manager.GetCurrentTime();
        return Task.CompletedTask;
    }
}

internal sealed class AlarmService : ISurfService
{
    public string Name => "AlarmService";
    public string Description => "Checks persisted alarms and triggers due alerts.";
    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    [SupportedOSPlatform("windows")]
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        Time_Manager.TriggerDueAlarms();
        return Task.CompletedTask;
    }
}

internal sealed class MailService : ISurfService
{
    public string Name => "MailService";
    public string Description => "Keeps local mailbox data loaded for the active user.";
    public TimeSpan Interval => TimeSpan.FromSeconds(45);

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        Import.DatabaseRecord? currentUser = Import.Variables.userDatabase.Find(
            user => user.Username.Equals(
                Import.Variables.userName,
                StringComparison.OrdinalIgnoreCase));

        if (currentUser is not null)
        {
            currentUser.Mailbox ??= [];
        }

        return Task.CompletedTask;
    }
}

internal sealed class CloudPresenceService : ISurfService
{
    public string Name => "CloudPresenceService";
    public string Description => "Sends periodic Firestore presence heartbeats.";
    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!Import.Variables.cloudServicesEnabled ||
            !Import.Variables.surfCloudSignedIn ||
            string.IsNullOrWhiteSpace(Import.Variables.surfCloudAccount))
        {
            return;
        }

        if (Cloud_Manager.DB is null)
        {
            Cloud_Manager.InitializeCloud(silent: true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await Cloud_Manager.RegisterUserHeartbeatAsync(Import.Variables.surfCloudAccount);
    }
}

internal sealed class ThemeWatcherService : ISurfService
{
    private DateTime _lastWriteTime;

    public string Name => "ThemeWatcherService";
    public string Description => "Watches the default theme package for file changes.";
    public TimeSpan Interval => TimeSpan.FromSeconds(10);

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        string themePath = Path.Combine(
            Import.Variables.installPath,
            "Packages",
            $"{Import.Variables.defaultTheme}.json");

        if (!File.Exists(themePath))
        {
            return Task.CompletedTask;
        }

        DateTime writeTime = File.GetLastWriteTimeUtc(themePath);
        if (_lastWriteTime == default)
        {
            _lastWriteTime = writeTime;
        }
        else if (writeTime > _lastWriteTime)
        {
            _lastWriteTime = writeTime;
        }

        return Task.CompletedTask;
    }
}
