namespace SurfOS2;

internal static class Program
{
    public static void Main()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            SurfOsApplication.Run();
            return;
        }

        System.Console.Error.WriteLine(
            "SurfOS Console currently supports Windows and macOS.");
        Environment.ExitCode = 1;
    } 
}
