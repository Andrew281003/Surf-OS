namespace SurfOS2;

internal sealed class ProcessInfo
{
    public int Pid { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Status { get; set; } = "Running";
    public DateTime StartTime { get; init; } = DateTime.Now;
    public int MemoryEstimateMb { get; init; }
    public bool IsProtected { get; init; }
    public bool SupportsKill { get; init; }
    public DateTime? EndTime { get; set; }
}

internal sealed class ProcessHandle : IDisposable
{
    private bool _disposed;

    internal ProcessHandle(ProcessInfo info)
    {
        Info = info;
    }

    public ProcessInfo Info { get; }
    public int Pid => Info.Pid;

    public bool KillRequested => ProcessManager.IsKillRequested(Pid);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ProcessManager.CompleteProcess(Pid);
    }
}

internal static class ProcessManager
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<int, ProcessInfo> Processes = [];
    private static readonly HashSet<int> KillRequests = [];
    private static int _nextPid = 100;

    public static ProcessHandle StartProcess(
        string name,
        int memoryEstimateMb,
        bool isProtected = false,
        bool supportsKill = false)
    {
        lock (SyncRoot)
        {
            ProcessInfo info = new()
            {
                Pid = _nextPid++,
                Name = name,
                MemoryEstimateMb = Math.Max(1, memoryEstimateMb),
                IsProtected = isProtected,
                SupportsKill = supportsKill
            };

            Processes[info.Pid] = info;
            return new ProcessHandle(info);
        }
    }

    public static IReadOnlyList<ProcessInfo> ListProcesses(bool includeCompleted = false)
    {
        lock (SyncRoot)
        {
            CleanupCompleted();
            return Processes.Values
                .Where(process => includeCompleted || process.Status == "Running")
                .OrderBy(process => process.Pid)
                .Select(Clone)
                .ToList();
        }
    }

    public static ProcessInfo? FindProcess(int pid)
    {
        lock (SyncRoot)
        {
            CleanupCompleted();
            return Processes.TryGetValue(pid, out ProcessInfo? process)
                ? Clone(process)
                : null;
        }
    }

    public static bool TryKillProcess(int pid, out string message)
    {
        lock (SyncRoot)
        {
            CleanupCompleted();
            if (!Processes.TryGetValue(pid, out ProcessInfo? process))
            {
                message = $"No simulated process exists with PID {pid}.";
                return false;
            }

            if (process.IsProtected)
            {
                message = $"PID {pid} ({process.Name}) is protected by the SurfOS kernel.";
                return false;
            }

            if (process.Status != "Running")
            {
                message = $"PID {pid} ({process.Name}) is already {process.Status.ToLowerInvariant()}.";
                return false;
            }

            if (!process.SupportsKill)
            {
                message = $"PID {pid} ({process.Name}) does not support safe simulated kill.";
                return false;
            }

            KillRequests.Add(pid);
            process.Status = "Killed";
            process.EndTime = DateTime.Now;
            message = $"Stopped simulated process {pid} ({process.Name}).";
            return true;
        }
    }

    public static bool IsKillRequested(int pid)
    {
        lock (SyncRoot)
        {
            return KillRequests.Contains(pid);
        }
    }

    internal static void CompleteProcess(int pid)
    {
        lock (SyncRoot)
        {
            if (!Processes.TryGetValue(pid, out ProcessInfo? process))
            {
                return;
            }

            if (process.Status == "Running")
            {
                process.Status = "Completed";
                process.EndTime = DateTime.Now;
            }
        }
    }

    public static void StopAllUserProcesses()
    {
        lock (SyncRoot)
        {
            foreach (ProcessInfo process in Processes.Values)
            {
                if (process.IsProtected || process.Status != "Running")
                {
                    continue;
                }

                process.Status = "Stopped";
                process.EndTime = DateTime.Now;
                KillRequests.Add(process.Pid);
            }
        }
    }

    private static ProcessInfo Clone(ProcessInfo process)
    {
        return new ProcessInfo
        {
            Pid = process.Pid,
            Name = process.Name,
            Status = process.Status,
            StartTime = process.StartTime,
            MemoryEstimateMb = process.MemoryEstimateMb,
            IsProtected = process.IsProtected,
            SupportsKill = process.SupportsKill,
            EndTime = process.EndTime
        };
    }

    private static void CleanupCompleted()
    {
        DateTime cutoff = DateTime.Now.AddSeconds(-10);
        foreach (int pid in Processes
                     .Where(pair => pair.Value.EndTime is not null &&
                                    pair.Value.EndTime < cutoff &&
                                    !pair.Value.IsProtected)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            Processes.Remove(pid);
            KillRequests.Remove(pid);
        }
    }
}
