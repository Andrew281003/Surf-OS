using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
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
    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr consoleHandle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr consoleHandle, uint mode);

    public static bool AnimationsEnabled { get; set; } =
        !Console.IsOutputRedirected && !Console.IsInputRedirected;

    public static void Initialize()
    {
        Console.SetOut(AnimatedOutput);
    }

    public static void ApplyForegroundColor(ConsoleColor color)
    {
        Console.ForegroundColor = color;
        if (OperatingSystem.IsMacOS() && !Console.IsOutputRedirected)
        {
            (byte red, byte green, byte blue) = MacOsColor(color);
            StandardOutput.Write($"\u001b[38;2;{red};{green};{blue}m");
            StandardOutput.Flush();
        }
    }

    public static void ApplyColors(ConsoleColor foreground, ConsoleColor background)
    {
        Console.ForegroundColor = foreground;
        Console.BackgroundColor = background;
        if (OperatingSystem.IsMacOS() && !Console.IsOutputRedirected)
        {
            (byte foregroundRed, byte foregroundGreen, byte foregroundBlue) =
                MacOsColor(foreground);
            (byte backgroundRed, byte backgroundGreen, byte backgroundBlue) =
                MacOsColor(background);
            StandardOutput.Write(
                $"\u001b[38;2;{foregroundRed};{foregroundGreen};{foregroundBlue};" +
                $"48;2;{backgroundRed};{backgroundGreen};{backgroundBlue}m");
            StandardOutput.Flush();
        }
    }

    public static IDisposable BeginCommandOutput()
    {
        if (Interlocked.Increment(ref _commandOutputDepth) == 1)
        {
            _skipCommandOutputAnimation = false;
        }

        return new CommandOutputScope();
    }

    public static IDisposable EnterAlternateScreen()
    {
        // The embedded macOS/Linux terminal hosts SurfOS runs in do not reliably
        // restore the primary buffer after an ANSI alternate-screen session. Keep
        // their scrollback intact instead of risking a cleared shell display.
        if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected)
        {
            return NoOpScope.Instance;
        }

        IntPtr outputHandle = GetStdHandle(StdOutputHandle);
        if (outputHandle == IntPtr.Zero || outputHandle == new IntPtr(-1) ||
            !GetConsoleMode(outputHandle, out uint originalMode) ||
            !SetConsoleMode(outputHandle, originalMode | EnableVirtualTerminalProcessing))
        {
            return NoOpScope.Instance;
        }

        StandardOutput.Write("\u001b[?1049h\u001b[2J\u001b[H");
        StandardOutput.Flush();
        return new AlternateScreenScope(outputHandle, originalMode);
    }

    public static string ReadShellInput(IReadOnlyList<string>? commandHistory = null)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        StringBuilder input = new();
        int cursorIndex = 0;
        int renderedLength = 0;
        int historyIndex = commandHistory?.Count ?? 0;
        string draftInput = string.Empty;
        int inputLeft = Console.CursorLeft;
        int inputTop = Console.CursorTop;

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    if (commandHistory is { Count: > 0 } && historyIndex > 0)
                    {
                        if (historyIndex == commandHistory.Count)
                        {
                            draftInput = input.ToString();
                        }

                        historyIndex--;
                        input.Clear();
                        input.Append(commandHistory[historyIndex]);
                        cursorIndex = input.Length;
                        RedrawShellInput(input, inputLeft, inputTop, cursorIndex, ref renderedLength);
                    }
                    continue;

                case ConsoleKey.DownArrow:
                    if (commandHistory is not null && historyIndex < commandHistory.Count)
                    {
                        historyIndex++;
                        input.Clear();
                        input.Append(historyIndex == commandHistory.Count
                            ? draftInput
                            : commandHistory[historyIndex]);
                        cursorIndex = input.Length;
                        RedrawShellInput(input, inputLeft, inputTop, cursorIndex, ref renderedLength);
                    }
                    continue;

                case ConsoleKey.PageUp:
                    ScrollShellViewport(-1, InputBottom(inputLeft, inputTop, input.Length));
                    continue;

                case ConsoleKey.PageDown:
                    ScrollShellViewport(1, InputBottom(inputLeft, inputTop, input.Length));
                    continue;

                case ConsoleKey.LeftArrow:
                    if (cursorIndex > 0)
                    {
                        cursorIndex--;
                        PositionInputCursor(inputLeft, inputTop, cursorIndex);
                    }
                    continue;

                case ConsoleKey.RightArrow:
                    if (cursorIndex < input.Length)
                    {
                        cursorIndex++;
                        PositionInputCursor(inputLeft, inputTop, cursorIndex);
                    }
                    continue;

                case ConsoleKey.Home:
                    cursorIndex = 0;
                    PositionInputCursor(inputLeft, inputTop, cursorIndex);
                    continue;

                case ConsoleKey.End:
                    cursorIndex = input.Length;
                    PositionInputCursor(inputLeft, inputTop, cursorIndex);
                    continue;

                case ConsoleKey.Backspace:
                    if (cursorIndex > 0)
                    {
                        input.Remove(--cursorIndex, 1);
                        RedrawShellInput(input, inputLeft, inputTop, cursorIndex, ref renderedLength);
                    }
                    continue;

                case ConsoleKey.Delete:
                    if (cursorIndex < input.Length)
                    {
                        input.Remove(cursorIndex, 1);
                        RedrawShellInput(input, inputLeft, inputTop, cursorIndex, ref renderedLength);
                    }
                    continue;

                case ConsoleKey.Enter:
                    EnsureInputVisible(InputBottom(inputLeft, inputTop, input.Length));
                    PositionInputCursor(inputLeft, inputTop, input.Length);
                    Console.WriteLine();
                    return input.ToString();
            }

            if (!char.IsControl(key.KeyChar))
            {
                input.Insert(cursorIndex++, key.KeyChar);
                RedrawShellInput(input, inputLeft, inputTop, cursorIndex, ref renderedLength);
            }
        }
    }

    private static void RedrawShellInput(
        StringBuilder input,
        int inputLeft,
        int inputTop,
        int cursorIndex,
        ref int renderedLength)
    {
        EnsureInputVisible(InputBottom(inputLeft, inputTop, input.Length));
        PositionInputCursor(inputLeft, inputTop, 0);
        Console.Write(input.ToString());
        int trailingSpaces = Math.Max(1, renderedLength - input.Length);
        Console.Write(new string(' ', trailingSpaces));
        renderedLength = input.Length;
        PositionInputCursor(inputLeft, inputTop, cursorIndex);
    }

    private static void PositionInputCursor(int inputLeft, int inputTop, int characterOffset)
    {
        try
        {
            int width = Math.Max(1, Console.BufferWidth);
            int absolutePosition = checked((inputTop * width) + inputLeft + characterOffset);
            Console.SetCursorPosition(absolutePosition % width, absolutePosition / width);
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or OverflowException)
        {
            // Resizing or closing the console can briefly invalidate cursor coordinates.
        }
    }

    private static int InputBottom(int inputLeft, int inputTop, int inputLength)
    {
        try
        {
            int width = Math.Max(1, Console.BufferWidth);
            return checked(((inputTop * width) + inputLeft + inputLength) / width);
        }
        catch (Exception ex) when (ex is IOException or OverflowException)
        {
            return inputTop;
        }
    }

    private static void ScrollShellViewport(int lineDelta, int contentBottom)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            int latestTop = Math.Max(0, contentBottom - Console.WindowHeight + 1);
            int targetTop = Math.Clamp(Console.WindowTop + lineDelta, 0, latestTop);
            if (targetTop != Console.WindowTop)
            {
                Console.SetWindowPosition(Console.WindowLeft, targetTop);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or PlatformNotSupportedException)
        {
            // Some redirected or virtual terminal hosts do not expose a movable viewport.
        }
    }

    private static void EnsureInputVisible(int contentBottom)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            int latestTop = Math.Max(0, contentBottom - Console.WindowHeight + 1);
            if (Console.WindowTop != latestTop)
            {
                Console.SetWindowPosition(Console.WindowLeft, latestTop);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or PlatformNotSupportedException)
        {
            // Keep accepting input even when the host owns viewport scrolling.
        }
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
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

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
        TypeLine($"CPU: {Environment.ProcessorCount}............{cpuStatus}", 2);
        TypeLine($"MEMORY.......................................{memoryStatus}", 2);
        TypeLine($"KEYBOARD...................................{keyboardStatus}", 2);
        TypeLine($"MOUNTING SURFOS SYSTEM VOLUME ...............{volumeStatus}", 2);
        TypeLine($"INITIALIZING NETWORK ADAPTER .................{networkStatus}", 2);
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
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Red;
        Spinner("Saving...", 1000);
        Thread.Sleep(500);
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

    private static (byte Red, byte Green, byte Blue) MacOsColor(ConsoleColor color) =>
        color switch
        {
            ConsoleColor.Black => (31, 41, 49),
            ConsoleColor.DarkBlue => (92, 124, 250),
            ConsoleColor.DarkGreen => (105, 219, 124),
            ConsoleColor.DarkCyan => (102, 217, 232),
            ConsoleColor.DarkRed => (255, 107, 107),
            ConsoleColor.DarkMagenta => (218, 119, 242),
            ConsoleColor.DarkYellow => (255, 209, 102),
            ConsoleColor.Gray => (222, 226, 230),
            ConsoleColor.DarkGray => (173, 181, 189),
            ConsoleColor.Blue => (116, 192, 252),
            ConsoleColor.Green => (140, 233, 154),
            ConsoleColor.Cyan => (153, 233, 242),
            ConsoleColor.Red => (255, 135, 135),
            ConsoleColor.Magenta => (229, 153, 247),
            ConsoleColor.Yellow => (255, 224, 102),
            _ => (248, 249, 250)
        };

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

    private sealed class AlternateScreenScope : IDisposable
    {
        private readonly IntPtr _outputHandle;
        private readonly uint _originalMode;
        private bool _disposed;

        public AlternateScreenScope(IntPtr outputHandle, uint originalMode)
        {
            _outputHandle = outputHandle;
            _originalMode = originalMode;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StandardOutput.Write("\u001b[0m\u001b[?25h\u001b[?1049l");
            StandardOutput.Flush();
            SetConsoleMode(_outputHandle, _originalMode);
            try
            {
                Console.CursorVisible = true;
            }
            catch
            {
                // The console may be closing during shutdown/uninstall.
            }
        }
    }

    private sealed class NoOpScope : IDisposable
    {
        public static readonly NoOpScope Instance = new();

        public void Dispose()
        {
        }
    }
}
