namespace SurfOS2;

/// <summary>
/// Runs startup stages in a predictable order and records each completed stage.
/// Feature startup can move into this pipeline incrementally as modules are split.
/// </summary>
internal sealed class StartupPipeline
{
    private readonly List<(string Name, Action Action)> stages = [];

    public StartupPipeline Add(string name, Action action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(action);
        stages.Add((name, action));
        return this;
    }

    public void Run()
    {
        foreach ((string name, Action action) in stages)
        {
            KernelLog.Info("startup", $"starting {name}");
            action();
            KernelLog.Success("startup", $"completed {name}");
        }
    }
}
