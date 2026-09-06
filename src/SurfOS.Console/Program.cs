using System.Runtime.Versioning;

namespace SurfOS2;

internal static class Program
{
    public static void Main()
    {
        if (OperatingSystem.IsWindows()) { SurfOsApplication.Run(); }
        else { System.Console.WriteLine("OS not supported"); }
    } 
}