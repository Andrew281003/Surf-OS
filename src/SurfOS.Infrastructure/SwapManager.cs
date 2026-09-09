namespace SurfOS2;

internal sealed class SwapState
{
    public long CapacityBytes { get; set; }
    public long UsedBytes { get; set; }
    public int TargetPercent { get; set; } = 70;
    public long PageOutBytes { get; set; }
    public long PageInBytes { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

internal readonly record struct SwapSnapshot(
    bool Enabled,
    long CapacityBytes,
    long UsedBytes,
    int TargetPercent,
    long PageOutBytes,
    long PageInBytes)
{
    public long FreeBytes => Math.Max(0, CapacityBytes - UsedBytes);
    public double UsedPercent =>
        CapacityBytes <= 0 ? 0 : UsedBytes * 100d / CapacityBytes;
}

internal static class Swap_Manager
{
    private const int DefaultTargetPercent = 70;
    private const int MaximumTargetPercent = 95;
    private const int MarkerSize = 4096;
    private const int MaximumMarkers = 64;
    private static readonly object SyncRoot = new();
    private static SwapState? _state;

    public static void Initialize()
    {
        lock (SyncRoot)
        {
            long capacity = GetConfiguredCapacity();
            if (capacity <= 0)
            {
                _state = new SwapState { CapacityBytes = 0, TargetPercent = 0 };
                return;
            }

            try
            {
                EnsureSwapFile(capacity);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
                _state = new SwapState { CapacityBytes = 0, TargetPercent = 0 };
                KernelLog.Warning("swap", $"swap could not be activated: {ex.Message}");
                return;
            }
            _state = LoadState() ?? new SwapState
            {
                CapacityBytes = capacity,
                TargetPercent = DefaultTargetPercent
            };
            _state.CapacityBytes = capacity;
            _state.TargetPercent = Math.Clamp(
                _state.TargetPercent,
                0,
                MaximumTargetPercent);
            ApplyTarget(_state.TargetPercent);
            KernelLog.Info(
                "swap",
                $"disk-backed swap online: {FormatBytes(_state.UsedBytes)} / " +
                $"{FormatBytes(_state.CapacityBytes)} logical usage");
        }
    }

    public static SwapSnapshot GetSnapshot()
    {
        lock (SyncRoot)
        {
            EnsureInitialized();
            return new SwapSnapshot(
                _state!.CapacityBytes > 0,
                _state.CapacityBytes,
                _state.UsedBytes,
                _state.TargetPercent,
                _state.PageOutBytes,
                _state.PageInBytes);
        }
    }

    public static bool SetTargetPercent(int percent, out string message)
    {
        lock (SyncRoot)
        {
            EnsureInitialized();
            if (_state!.CapacityBytes <= 0)
            {
                message = "Swap is disabled for this SurfOS installation.";
                return false;
            }

            if (percent is < 0 or > MaximumTargetPercent)
            {
                message = $"Swap pressure must be between 0 and {MaximumTargetPercent} percent.";
                return false;
            }

            ApplyTarget(percent);
            message =
                $"Swap pressure set to {percent}% " +
                $"({FormatBytes(_state.UsedBytes)} logically paged to disk).";
            return true;
        }
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }

    private static void ApplyTarget(int percent)
    {
        long previousUsage = _state!.UsedBytes;
        long targetUsage = _state.CapacityBytes * percent / 100;
        if (targetUsage > previousUsage)
        {
            WritePressureMarkers(targetUsage);
            _state.PageOutBytes += targetUsage - previousUsage;
        }
        else
        {
            _state.PageInBytes += previousUsage - targetUsage;
        }

        _state.TargetPercent = percent;
        _state.UsedBytes = targetUsage;
        _state.UpdatedUtc = DateTime.UtcNow;
        SaveState();
    }

    private static void WritePressureMarkers(long logicalUsage)
    {
        string swapPath = GetSwapFilePath();
        if (!File.Exists(swapPath) || logicalUsage < MarkerSize)
        {
            return;
        }

        byte[] marker = new byte[MarkerSize];
        Random.Shared.NextBytes(marker);
        int markerCount = (int)Math.Clamp(
            logicalUsage / (32L * 1024L * 1024L),
            1,
            MaximumMarkers);
        long interval = Math.Max(MarkerSize, logicalUsage / markerCount);

        using FileStream stream = new(
            swapPath,
            FileMode.Open,
            FileAccess.Write,
            FileShare.Read,
            MarkerSize,
            FileOptions.WriteThrough | FileOptions.RandomAccess);
        for (int index = 0; index < markerCount; index++)
        {
            long offset = Math.Min(
                Math.Max(0, logicalUsage - MarkerSize),
                index * interval);
            stream.Position = offset;
            stream.Write(marker);
        }
        stream.Flush(flushToDisk: true);
    }

    private static void EnsureInitialized()
    {
        if (_state is null)
        {
            Initialize();
        }
    }

    private static long GetConfiguredCapacity()
    {
        PartitionManifest? manifest =
            Partition_Manager.Load(Import.Variables.installPath);
        if (manifest is not null)
        {
            return manifest.SwapBytes;
        }

        string existingSwap = GetSwapFilePath();
        return File.Exists(existingSwap) ? new FileInfo(existingSwap).Length : 0;
    }

    private static void EnsureSwapFile(long capacity)
    {
        string path = GetSwapFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length != capacity)
            {
                using FileStream existing = new(
                    path,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.Read);
                existing.SetLength(capacity);
            }
            return;
        }

        using FileStream created = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read);
        created.SetLength(capacity);
    }

    private static SwapState? LoadState()
    {
        string path = GetStatePath();
        try
        {
            return File.Exists(path)
                ? JsonStorage.Read<SwapState>(path)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveState()
    {
        JsonStorage.Write(GetStatePath(), _state);
    }

    private static string GetSwapFilePath() =>
        Path.Combine(
            Import.Variables.installPath,
            "system",
            "swap",
            "swapfile.sys");

    private static string GetStatePath() =>
        Path.Combine(
            Import.Variables.installPath,
            "system",
            "swap",
            "swap-state.json");
}
