namespace SurfOS2;

internal static class First_Boot_Preparation
{
    public static void Run()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Cyan;
        RetroConsole.TypeLine("Preparing your SurfOS installation...", 4);
        Console.ResetColor();
        Console.WriteLine();

        Complete("Creating user profile");
        Complete("Configuring virtual filesystem");
        Complete(Import.Variables.developerToolsEnabled
            ? "Installing selected components and developer tools"
            : "Installing selected components");
        Complete($"Provisioning {Import.Variables.systemFootprint} system footprint");
        Complete("Creating factory recovery snapshot");
        Complete($"Applying {Import.Variables.defaultTheme} theme");
        Complete(Import.Variables.cloudServicesEnabled
            ? "Initializing SurfCloud"
            : "Configuring offline services");
        Complete("Configuring security");
        Complete("Finalizing installation");

        Console.WriteLine();
        RetroConsole.ProgressBar("FIRST-BOOT PREPARATION", 24, 18);
        Console.ForegroundColor = ConsoleColor.Green;
        RetroConsole.TypeLine("\nSystem ready.", 6);
        Console.ResetColor();
        Thread.Sleep(350);
    }

    private static void Complete(string task)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        RetroConsole.TypeLine($"✓ {task}", 3);
        Console.ResetColor();
        Thread.Sleep(80);
    }
}
