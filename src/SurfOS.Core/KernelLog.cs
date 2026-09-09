namespace SurfOS2;

internal enum KernelLogLevel
{
    Info,
    Warning,
    Error,
    Success
}

internal sealed record KernelLogEntry(
    DateTime Timestamp,
    KernelLogLevel Level,
    string Category,
    string Message,
    int BootId)
{
    public string Format()
    {
        return $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level.ToString().ToUpperInvariant(),-7}] [boot:{BootId}] [{Category}] {Message}";
    }
}

internal static class KernelLog
{
    private const int MaxRecentEntries = 300;
    private static readonly object SyncRoot = new();
    private static readonly Queue<KernelLogEntry> RecentEntries = new();
    private static readonly int CurrentBootId = Environment.TickCount;
    private static string _logPath = Path.Combine(Environment.CurrentDirectory, "logs", "kernel.log");

    public static void Initialize(string installPath)
    {
        lock (SyncRoot)
        {
            string root = string.IsNullOrWhiteSpace(installPath)
                ? Environment.CurrentDirectory
                : installPath;

            _logPath = Path.Combine(root, "logs", "kernel.log");
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath) ?? root);
        }

        Info("kernel", $"kernel log online at {_logPath}");
    }

    public static void Info(string category, string message)
    {
        Write(KernelLogLevel.Info, category, message);
    }

    public static void Warning(string category, string message)
    {
        Write(KernelLogLevel.Warning, category, message);
    }

    public static void Error(string category, string message)
    {
        Write(KernelLogLevel.Error, category, message);
    }

    public static void Success(string category, string message)
    {
        Write(KernelLogLevel.Success, category, message);
    }

    public static IReadOnlyList<KernelLogEntry> Recent(
        KernelLogLevel? level = null,
        bool currentBootOnly = false)
    {
        lock (SyncRoot)
        {
            return RecentEntries
                .Where(entry => level is null || entry.Level == level)
                .Where(entry => !currentBootOnly || entry.BootId == CurrentBootId)
                .ToList();
        }
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            RecentEntries.Clear();
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath) ?? Environment.CurrentDirectory);
            File.WriteAllText(_logPath, string.Empty);
        }

        Info("kernel", "kernel log cleared");
    }

    private static void Write(KernelLogLevel level, string category, string message)
    {
        KernelLogEntry entry = new(
            DateTime.Now,
            level,
            string.IsNullOrWhiteSpace(category) ? "kernel" : category,
            message,
            CurrentBootId);

        lock (SyncRoot)
        {
            RecentEntries.Enqueue(entry);
            while (RecentEntries.Count > MaxRecentEntries)
            {
                RecentEntries.Dequeue();
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath) ?? Environment.CurrentDirectory);
                File.AppendAllText(_logPath, entry.Format() + Environment.NewLine);
            }
            catch
            {
                // Logging must never take down the shell.
            }
        }
    }
}
