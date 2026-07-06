using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace SurfOS2;

internal static class RetroConsole
{
    private static readonly string[] SpinnerFrames = ["|", "/", "-", "\\"];
    private static readonly TextWriter StandardOutput = Console.Out;
    private static readonly AnimatedTextWriter AnimatedOutput = new(StandardOutput);
    private const int TextAnimationSpeedMultiplier = 2;
    private const int CommandOutputLineDelayMilliseconds = 12;
    private static int _commandOutputDepth;
    private static bool _skipCommandOutputAnimation;

    public static bool AnimationsEnabled { get; set; } =
        !Console.IsOutputRedirected && !Console.IsInputRedirected;

    public static void Initialize()
    {
        Console.SetOut(AnimatedOutput);
    }

    public static IDisposable BeginCommandOutput()
    {
        if (Interlocked.Increment(ref _commandOutputDepth) == 1)
        {
            _skipCommandOutputAnimation = false;
        }

        return new CommandOutputScope();
    }

    public static void Type(string text, int delayMilliseconds = 8)
    {
        if (!AnimationsEnabled || delayMilliseconds <= 0)
        {
            Console.Write(text);
            return;
        }

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            Console.Write(character);

            if (TrySkipAnimation())
            {
                int remainingIndex = index + 1;
                if (remainingIndex < text.Length)
                {
                    Console.Write(text[remainingIndex..]);
                }

                return;
            }

            Thread.Sleep(GetTextDelay(character, delayMilliseconds));
        }
    }

    public static void TypeLine(string text = "", int delayMilliseconds = 8)
    {
        Type(text, delayMilliseconds);
        Console.WriteLine();
    }

    public static void TypeBlock(string text, int delayMilliseconds = 3)
    {
        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
        {
            TypeLine(line, delayMilliseconds);
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_LSHIFT = 0xA0; // HEX Decimal key values
    private const int VK_RSHIFT = 0xA1; // same for this guy
    // This is needed so the assembly can see exactly which key was pressed, in this case its [SHIFT]

    private static bool IsShiftHeld()
    {
        return (GetAsyncKeyState(VK_LSHIFT) & 0x8000) != 0 ||
               (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0;
    }

    public static void BootSequence()
    {
        string cpuStatus = CheckCpu();
        string memoryStatus = CheckMemory(); ;
        string keyboardStatus = CheckKeyboard();
        string volumeStatus = CheckInstallVolume();
        string networkStatus = CheckNetworkAdapter();

        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Green;
        TypeLine("SURF BIOS v2.6 \n\n\n\n", 3);
        TypeLine($"CPU: {Environment.ProcessorCount} LOGICAL PROCESSORS ........ {cpuStatus}", 2);
        TypeLine($"MEMORY TEST ................................ {memoryStatus}", 2);
        TypeLine($"KEYBOARD CONTROLLER ........................ {keyboardStatus}", 2);
        TypeLine($"MOUNTING SURFOS SYSTEM VOLUME .............. {volumeStatus}", 2);
        TypeLine($"INITIALIZING NETWORK ADAPTER ................ {networkStatus}", 2);
        Console.WriteLine();
        ProgressBar("LOADING KERNEL", 18, 12);
        Console.ResetColor();
        if (IsShiftHeld())
        {
            string error = string.Empty;
            int amount = 0;
            if (cpuStatus != "OK") { error += "\ncpu"; amount++; }
            if (memoryStatus != "OK") { error += "\nmemory"; amount++; }
            if (keyboardStatus != "OK") { error += "\nkeyboard"; amount++; }
            if (volumeStatus != "OK") { error += "\nvolume"; amount++; }

            Console.ReadKey();
            Console.WriteLine("BIOS Log: Launching...");
            BIOS.Boot(error, amount);
        }
        Thread.Sleep(100);
    }
    private static string CheckCpu()
    {
        return Environment.ProcessorCount > 0 ? "OK" : "FAIL";
    }

    private static string CheckMemory()
    {
        try
        {
            GCMemoryInfo memoryInfo = GC.GetGCMemoryInfo();
            long availableMemory = memoryInfo.TotalAvailableMemoryBytes;

            return availableMemory <= 0 || availableMemory >= 64L * 1024L * 1024L
                ? "OK"
                : "LOW";
        }
        catch
        {
            return "WARN";
        }
    }

    private static string CheckKeyboard()
    {
        try
        {
            _ = Console.KeyAvailable;
            return Console.IsInputRedirected ? "REDIRECTED" : "OK";
        }
        catch
        {
            return "WARN";
        }
    }

    private static string CheckInstallVolume()
    {
        string installPath = Import.Variables.installPath;
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
        {
            return "MISSING";
        }

        string[] requiredPaths =
        [
            Path.Combine(installPath, "options.json"),
            Path.Combine(installPath, "database.json"),
            Path.Combine(installPath, "installer_feedback.json"),
            Path.Combine(installPath, "Packages")
        ];

        if (requiredPaths.Any(path => !File.Exists(path) && !Directory.Exists(path)))
        {
            return "REPAIR";
        }

        try
        {
            string probePath = Path.Combine(installPath, $".surfos_probe_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
        }
        catch
        {
            return "READONLY";
        }

        return "OK";
    }

    private static string CheckNetworkAdapter()
    {
        try
        {
            bool hasActiveAdapter = NetworkInterface.GetAllNetworkInterfaces()
                .Any(networkInterface =>
                    networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    networkInterface.OperationalStatus == OperationalStatus.Up);

            return hasActiveAdapter ? "OK" : "OFFLINE";
        }
        catch
        {
            return "WARN";
        }
    }

    public static void ProgressBar(
        string label,
        int width = 20,
        int frameDelayMilliseconds = 5)
    {
        if (!AnimationsEnabled)
        {
            Console.WriteLine($"{label} [{new string('#', width)}] 100%");
            return;
        }

        Console.Write($"{label} [");
        for (int index = 0; index < width; index++)
        {
            Console.Write('#');
            Thread.Sleep(frameDelayMilliseconds);
        }

        Console.WriteLine("] 100%");
    }

    public static void Spinner(
        string label,
        int durationMilliseconds = 350,
        ConsoleColor? color = null)
    {
        if (!AnimationsEnabled)
        {
            Console.WriteLine($"{label} ... OK");
            return;
        }

        ConsoleColor originalColor = Console.ForegroundColor;
        if (color.HasValue)
        {
            Console.ForegroundColor = color.Value;
        }

        Console.Write($"{label} ");
        int left = Console.CursorLeft;
        int top = Console.CursorTop;
        int frame = 0;
        long stopAt = Environment.TickCount64 + durationMilliseconds;

        while (Environment.TickCount64 < stopAt)
        {
            Console.SetCursorPosition(left, top);
            Console.Write(SpinnerFrames[frame++ % SpinnerFrames.Length]);
            Thread.Sleep(55);
        }

        Console.SetCursorPosition(left, top);
        Console.WriteLine("OK");
        Console.ForegroundColor = originalColor;
    }

    public static void RevealLines(string text, int lineDelayMilliseconds = 6)
    {
        if (!AnimationsEnabled)
        {
            Console.WriteLine(text);
            return;
        }

        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
        {
            Console.WriteLine(line);
            Thread.Sleep(lineDelayMilliseconds);
        }
    }

    public static void ShutdownSequence()
    {
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Spinner("FLUSHING DISK BUFFERS", 250);
        Spinner("STOPPING BACKGROUND SERVICES", 250);
        Spinner("PARKING SYSTEM DRIVE", 200);
        TypeLine("IT IS NOW SAFE TO TURN OFF YOUR COMPUTER.", 5);
        Console.ResetColor();
    }

    private static bool TrySkipAnimation()
    {
        try
        {
            if (!Console.KeyAvailable)
            {
                return false;
            }

            Console.ReadKey(intercept: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ShouldAnimateCommandOutput =>
        AnimationsEnabled &&
        !_skipCommandOutputAnimation &&
        Volatile.Read(ref _commandOutputDepth) > 0;

    private static int GetTextDelay(char character, int delayMilliseconds)
    {
        int adjustedDelay = Math.Max(1, delayMilliseconds / TextAnimationSpeedMultiplier);
        return character is '.' or ':' ? adjustedDelay + 1 : adjustedDelay;
    }

    private sealed class AnimatedTextWriter(TextWriter innerWriter) : TextWriter
    {
        public override Encoding Encoding => innerWriter.Encoding;

        public override void Write(char value)
        {
            innerWriter.Write(value);
            DelayAfterLine(value);
        }

        public override void Write(string? value)
        {
            if (value is null)
            {
                return;
            }

            if (!ShouldAnimateCommandOutput)
            {
                innerWriter.Write(value);
                return;
            }

            foreach (char character in value)
            {
                innerWriter.Write(character);
                DelayAfterLine(character);
            }
        }

        public override void WriteLine()
        {
            innerWriter.WriteLine();
            DelayAfterLine('\n');
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            innerWriter.WriteLine();
            DelayAfterLine('\n');
        }

        public override void Flush()
        {
            innerWriter.Flush();
        }

        private static void DelayAfterLine(char character)
        {
            if (!ShouldAnimateCommandOutput || character is not '\n')
            {
                return;
            }

            if (TrySkipAnimation())
            {
                _skipCommandOutputAnimation = true;
                return;
            }

            Thread.Sleep(CommandOutputLineDelayMilliseconds);
        }
    }

    private sealed class CommandOutputScope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Interlocked.Decrement(ref _commandOutputDepth);
        }
    }
}
