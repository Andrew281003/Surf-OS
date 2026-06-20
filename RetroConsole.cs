using System.Text;

namespace SurfOS2;

internal static class RetroConsole
{
    private static readonly string[] SpinnerFrames = ["|", "/", "-", "\\"];
    private static readonly TextWriter StandardOutput = Console.Out;
    private static readonly AnimatedTextWriter AnimatedOutput = new(StandardOutput);
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

            Thread.Sleep(character is '.' or ':' ? delayMilliseconds * 2 : delayMilliseconds);
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

    public static void BootSequence()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Green;
        TypeLine("SURF BIOS v2.6  (C) 1984-2026 SURF SYSTEMS", 3);
        TypeLine($"CPU: {Environment.ProcessorCount} LOGICAL PROCESSORS ........ OK", 2);
        TypeLine("MEMORY TEST ................................ OK", 2);
        TypeLine("KEYBOARD CONTROLLER ........................ OK", 2);
        TypeLine("MOUNTING SURFOS SYSTEM VOLUME .............. OK", 2);
        TypeLine("INITIALIZING NETWORK ADAPTER ................ BACKGROUND", 2);
        Console.WriteLine();
        ProgressBar("LOADING KERNEL", 18, 12);
        Console.ResetColor();
        Thread.Sleep(100);
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

    private sealed class AnimatedTextWriter(TextWriter innerWriter) : TextWriter
    {
        public override Encoding Encoding => innerWriter.Encoding;

        public override void Write(char value)
        {
            innerWriter.Write(value);
            DelayFor(value);
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
                DelayFor(character);
            }
        }

        public override void WriteLine()
        {
            innerWriter.WriteLine();
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            innerWriter.WriteLine();
        }

        public override void Flush()
        {
            innerWriter.Flush();
        }

        private static void DelayFor(char character)
        {
            if (!ShouldAnimateCommandOutput || character is '\r' or '\n')
            {
                return;
            }

            if (TrySkipAnimation())
            {
                _skipCommandOutputAnimation = true;
                return;
            }

            Thread.Sleep(character is '.' or ':' ? 3 : 1);
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
