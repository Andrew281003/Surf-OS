namespace SurfOS2;

/// <summary>
/// Shared line-oriented Vim-style editor for SurfOS text surfaces.
/// </summary>
internal static class VimEditor
{
    public static void EditVirtualFile(string fileName)
    {
        if (VirtualFileSystem.TryResolvePackageBuilderFile(fileName, out string workspaceRoot, out string builderFile))
        {
            EditHostFile(workspaceRoot, builderFile);
            return;
        }

        if (VirtualFileSystem.ReadFile(fileName, out string content, out string readError))
        {
            Edit($"Vim: {fileName}", content, updated =>
                VirtualFileSystem.WriteFile(fileName, updated, append: false, out string error)
                    ? (true, string.Empty)
                    : (false, error));
            return;
        }

        if (!readError.StartsWith("File not found:", StringComparison.Ordinal))
        {
            Console.WriteLine(readError);
            return;
        }

        Edit($"Vim: {fileName}", string.Empty, updated =>
            VirtualFileSystem.WriteFile(fileName, updated, append: false, out string error)
                ? (true, string.Empty)
                : (false, error));
    }

    public static void EditHostFile(string workspaceRoot, string fullPath)
    {
        if (!PathSafety.IsInsideRoot(fullPath, workspaceRoot))
        {
            Console.WriteLine("Vim: the selected file is outside this app workspace.");
            return;
        }

        string title = $"Vim: {Path.GetRelativePath(workspaceRoot, fullPath)}";
        string content = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
        Edit(title, content, updated =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? workspaceRoot);
                File.WriteAllText(fullPath, updated);
                return (true, string.Empty);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (false, ex.Message);
            }
        });
    }

    private static void Edit(
        string title,
        string content,
        Func<string, (bool Success, string Error)> save)
    {
        List<string> lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count == 1 && lines[0].Length == 0) lines.Clear();
        Stack<List<string>> undoHistory = new();
        Stack<List<string>> redoHistory = new();
        int? insertIndex = null;
        int? selectedLine = null;
        int firstVisibleLine = 0;

        Console.Clear();
        while (true)
        {
            int commandBarRow = Math.Max(0, Console.WindowHeight - 1);
            const int sourceTopRow = 2;
            int sourceRowCount = Math.Max(0, commandBarRow - sourceTopRow);
            firstVisibleLine = EnsureSelectedLineIsVisible(lines.Count, selectedLine, firstVisibleLine, sourceRowCount);

            Console.ForegroundColor = ConsoleColor.Cyan;
            WriteViewportLine($"--- {title} ---", 0);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            if (commandBarRow > 1)
                WriteViewportLine("Commands: :w save | :q quit | :wq save + quit | ↑/↓ scroll | :help", 1);
            Screen_Print.ResetColors();

            for (int offset = 0; offset < sourceRowCount; offset++)
            {
                int index = firstVisibleLine + offset;
                Console.ForegroundColor = selectedLine == index ? ConsoleColor.Yellow : Import.Variables.activeForegroundColor;
                WriteViewportLine(index < lines.Count ? $"{index + 1,4} {lines[index]}" : string.Empty,
                    sourceTopRow + offset);
            }

            if (selectedLine is not null)
            {
                int lineIndex = selectedLine.Value;
                int row = sourceTopRow + lineIndex - firstVisibleLine;
                if (row < commandBarRow && TryEditAtLine(row, out string replacement))
                {
                    RecordChange(lines, undoHistory, redoHistory);
                    lines[lineIndex] = replacement;
                    selectedLine = null;
                    continue;
                }
            }

            string prompt = insertIndex is not null ? $"insert:{insertIndex.Value + 1}> " : "> ";
            DrawCommandBar(commandBarRow, prompt);
            CommandInputResult result = ReadCommandInput(commandBarRow, prompt, out string input);
            if (result is CommandInputResult.ScrollUp or CommandInputResult.ScrollDown)
            {
                int maxFirstVisibleLine = Math.Max(0, lines.Count - sourceRowCount);
                int scrollAmount = result == CommandInputResult.ScrollUp ? -1 : 1;
                firstVisibleLine = Math.Clamp(firstVisibleLine + scrollAmount, 0, maxFirstVisibleLine);
                continue;
            }

            if (insertIndex is not null && !input.StartsWith(':'))
            {
                RecordChange(lines, undoHistory, redoHistory);
                lines.Insert(insertIndex.Value, input);
                insertIndex++;
                continue;
            }

            if (selectedLine is not null && !input.StartsWith(':'))
            {
                RecordChange(lines, undoHistory, redoHistory);
                lines[selectedLine.Value] = input;
                selectedLine = null;
                continue;
            }

            if (!input.StartsWith(':'))
            {
                RecordChange(lines, undoHistory, redoHistory);
                lines.Add(input);
                continue;
            }

            if (input is ":q" or ":quit") return;
            if (input is ":u" or ":undo")
            {
                RestoreHistory(lines, undoHistory, redoHistory);
                selectedLine = null;
                insertIndex = null;
                continue;
            }
            if (input is ":r" or ":redo")
            {
                RestoreHistory(lines, redoHistory, undoHistory);
                selectedLine = null;
                insertIndex = null;
                continue;
            }
            if (input is ":w" or ":write" or ":wq")
            {
                (bool success, string error) = save(string.Join(Environment.NewLine, lines));
                if (!success)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Save failed: {error}");
                    Screen_Print.ResetColors();
                    Pause();
                    continue;
                }

                if (input == ":wq") return;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Saved.");
                Screen_Print.ResetColors();
                Pause();
                continue;
            }

            if (input is ":esc" or ":normal") { insertIndex = null; selectedLine = null; continue; }
            if (input == ":clear") { RecordChange(lines, undoHistory, redoHistory); lines.Clear(); insertIndex = null; selectedLine = null; continue; }
            if (input == ":help") { Pause("Use :u to undo and :redo to restore an undone change. Use :s <line> to move the live input to that line."); continue; }
            if (TryReadLineNumber(input, ":i", out int insertLine))
            {
                insertIndex = Math.Clamp(insertLine - 1, 0, lines.Count);
                selectedLine = null;
                continue;
            }
            if (TryReadLineNumber(input, ":s", out int lineToSelect) && lineToSelect >= 1 && lineToSelect <= lines.Count)
            {
                selectedLine = lineToSelect - 1;
                insertIndex = null;
                continue;
            }
            if (TrySetLine(input, out int setLine, out string text) && setLine >= 1 && setLine <= lines.Count)
            {
                RecordChange(lines, undoHistory, redoHistory);
                lines[setLine - 1] = text;
                selectedLine = null;
                continue;
            }
            if (TryReadLineNumber(input, ":d", out int deleteLine) && deleteLine >= 1 && deleteLine <= lines.Count)
            {
                RecordChange(lines, undoHistory, redoHistory);
                lines.RemoveAt(deleteLine - 1);
                if (insertIndex is not null) insertIndex = Math.Clamp(insertIndex.Value, 0, lines.Count);
                selectedLine = null;
            }
        }
    }

    private static bool TryReadLineNumber(string input, string command, out int line)
    {
        line = 0;
        return input.StartsWith(command + " ", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(input[(command.Length + 1)..], out line);
    }

    private static void RecordChange(
        IReadOnlyCollection<string> lines,
        Stack<List<string>> undoHistory,
        Stack<List<string>> redoHistory)
    {
        undoHistory.Push(lines.ToList());
        redoHistory.Clear();
    }

    private static void RestoreHistory(
        List<string> lines,
        Stack<List<string>> sourceHistory,
        Stack<List<string>> destinationHistory)
    {
        if (sourceHistory.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Nothing to undo or redo.");
            Screen_Print.ResetColors();
            Pause();
            return;
        }

        destinationHistory.Push(lines.ToList());
        List<string> snapshot = sourceHistory.Pop();
        lines.Clear();
        lines.AddRange(snapshot);
    }

    private static bool TrySetLine(string input, out int line, out string text)
    {
        line = 0;
        text = string.Empty;
        if (!input.StartsWith(":set ", StringComparison.OrdinalIgnoreCase)) return false;
        string[] parts = input[5..].Split(' ', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out line)) return false;
        text = parts[1];
        return true;
    }

    private static int EnsureSelectedLineIsVisible(int lineCount, int? selectedLine, int firstVisibleLine, int sourceRowCount)
    {
        int maxFirstLine = Math.Max(0, lineCount - sourceRowCount);
        firstVisibleLine = Math.Clamp(firstVisibleLine, 0, maxFirstLine);
        if (selectedLine is null || sourceRowCount == 0) return firstVisibleLine;

        if (selectedLine.Value < firstVisibleLine) return selectedLine.Value;
        if (selectedLine.Value >= firstVisibleLine + sourceRowCount)
            return Math.Min(maxFirstLine, selectedLine.Value - sourceRowCount + 1);

        return firstVisibleLine;
    }

    private enum CommandInputResult
    {
        Submitted,
        ScrollUp,
        ScrollDown
    }

    private static CommandInputResult ReadCommandInput(int row, string prompt, out string input)
    {
        input = string.Empty;
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) return CommandInputResult.Submitted;
            if (key.Key == ConsoleKey.UpArrow) return CommandInputResult.ScrollUp;
            if (key.Key == ConsoleKey.DownArrow) return CommandInputResult.ScrollDown;

            if (key.Key == ConsoleKey.Backspace)
            {
                if (input.Length > 0) input = input[..^1];
                DrawCommandBar(row, prompt, input);
                continue;
            }

            int availableInputWidth = Math.Max(0, Console.WindowWidth - 1 - prompt.Length);
            if (!char.IsControl(key.KeyChar) && input.Length < availableInputWidth)
            {
                input += key.KeyChar;
                DrawCommandBar(row, prompt, input);
            }
        }
    }

    private static void DrawCommandBar(int row, string prompt, string input = "")
    {
        int width = Math.Max(1, Console.WindowWidth - 1);
        Console.SetCursorPosition(Console.WindowLeft, Console.WindowTop + row);
        Console.Write(new string(' ', width));
        Console.SetCursorPosition(Console.WindowLeft, Console.WindowTop + row);
        Console.ForegroundColor = Import.Variables.activeForegroundColor;
        string value = prompt + input;
        Console.Write(value.Length > width ? value[..width] : value);
        Screen_Print.ResetColors();
    }

    private static void WriteViewportLine(string value, int row)
    {
        int width = Math.Max(0, Console.WindowWidth - 1);
        Console.SetCursorPosition(Console.WindowLeft, Console.WindowTop + row);
        Console.Write(FormatViewportLine(value, width).PadRight(width));
    }

    internal static string FormatViewportLine(string value, int width)
    {
        // Expand tabs before clipping so they cannot wrap or displace subsequent rows.
        var visible = new System.Text.StringBuilder();
        foreach (char character in value)
        {
            if (visible.Length >= width) break;
            if (character == '\t')
                visible.Append(' ', Math.Min(4 - visible.Length % 4, width - visible.Length));
            else if (!char.IsControl(character))
                visible.Append(character);
        }
        return visible.ToString();
    }

    private static bool TryEditAtLine(int row, out string replacement)
    {
        replacement = string.Empty;
        try
        {
            // Each rendered source line has a five-character number gutter.
            int column = 5;
            int writableWidth = Math.Max(1, Console.WindowWidth - column - 1);
            Console.SetCursorPosition(Console.WindowLeft + column, Console.WindowTop + row);
            Console.Write(new string(' ', writableWidth));
            Console.SetCursorPosition(Console.WindowLeft + column, Console.WindowTop + row);
            replacement = Console.ReadLine() ?? string.Empty;
            return true;
        }
        catch (IOException)
        {
            // A non-interactive console cannot reposition the input cursor.
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static void Pause(string? message = null)
    {
        if (!string.IsNullOrWhiteSpace(message)) Console.WriteLine($"\n{message}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Press ENTER to continue...");
        Screen_Print.ResetColors();
        Console.ReadLine();
    }
}
