namespace SurfOS2;

/// <summary>
/// Describes the host paths and runtime details used while SurfOS starts.
/// Keeping them together makes startup behavior explicit and testable.
/// </summary>
internal sealed record SystemContext(
    string LocalDataPath,
    string DesktopInstallPath,
    string DocumentsInstallPath)
{
    public static SystemContext CreateForCurrentUser() => new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SurfOS"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "SurfOS"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "SurfOS"));
}
