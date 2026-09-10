using System.Text;

namespace SurfOS2;

internal partial class CLI_Engine
{
    internal static int LastExitCode { get; private set; }
    private static string? ShellInput;
    private static int ExecutionDepth;
    private static readonly List<string> History = [];
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> EnvironmentVariables = new(StringComparer.Ordinal);
    private static string SessionIdentity = "";
    private sealed record ShellToken(string Value, bool Operator = false);
    private sealed class ShellStage
    {
        public List<string> Args { get; } = [];
        public string? Input { get; set; }
        public string? Output { get; set; }
        public bool Append { get; set; }
    }
    private sealed record ShellPipeline(string Condition, List<ShellStage> Stages);
    private sealed class ShellOutputBuffer : TextWriter
    {
        private readonly StringBuilder content = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value)
        {
            if (content.Length >= MaximumDownloadBytes) throw new IOException("Shell output exceeds 16 Mi characters.");
            content.Append(value);
        }
        public override void Write(string? value)
        {
            if (value is null) return;
            if (content.Length + (long)value.Length > MaximumDownloadBytes) throw new IOException("Shell output exceeds 16 Mi characters.");
            content.Append(value);
        }
        public override string ToString() => content.ToString();
    }

    internal static void ResetShellSession()
    {
        History.Clear(); Aliases.Clear(); EnvironmentVariables.Clear();
        ShellInput = null; LastExitCode = 0;
        SessionIdentity = Import.Variables.installPath + "|" + Import.Variables.userName;
    }

    private static void ShellError(string message, int code = 1)
    {
        LastExitCode = code;
        // Keep diagnostics out of redirected output, while retaining normal console/test output.
        (ExecutionDepth > 0 && DiagnosticOutput is not null ? DiagnosticOutput : Console.Out).WriteLine(message);
    }
    private static TextWriter? DiagnosticOutput;

    private static string EnvironmentValue(string name) => name switch
    {
        "?" => LastExitCode.ToString(),
        "PWD" => VirtualFileSystem.CurrentDirectory,
        "USER" => Import.Variables.userName,
        "HOME" => "/home/" + Import.Variables.userName,
        _ => EnvironmentVariables.GetValueOrDefault(name, "")
    };

    private static List<ShellToken> LexShell(string input)
    {
        List<ShellToken> tokens = [];
        StringBuilder word = new();
        char quote = '\0'; bool started = false;
        void Flush() { if (started) tokens.Add(new(word.ToString())); word.Clear(); started = false; }
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '\\' && quote != '\'' && i + 1 < input.Length)
            { word.Append(input[++i]); started = true; continue; }
            if (c == quote && quote != '\0') { quote = '\0'; continue; }
            if (quote == '\0' && c is '\'' or '"') { quote = c; started = true; continue; }
            if (c == '$' && quote != '\'')
            {
                string name;
                if (i + 1 < input.Length && input[i + 1] == '{')
                {
                    int end = input.IndexOf('}', i + 2);
                    if (end < 0) throw new ArgumentException("Unclosed environment variable.");
                    name = input[(i + 2)..end]; i = end;
                }
                else
                {
                    int end = i + 1;
                    if (end < input.Length && input[end] == '?') end++;
                    else while (end < input.Length && (char.IsLetterOrDigit(input[end]) || input[end] == '_')) end++;
                    name = input[(i + 1)..end];
                    if (name.Length == 0) { word.Append(c); started = true; continue; }
                    i = end - 1;
                }
                word.Append('\u0001').Append(name).Append('\u0002'); started = true; continue;
            }
            if (quote == '\0' && char.IsWhiteSpace(c)) { Flush(); continue; }
            if (quote == '\0' && c is '|' or '&' or '<' or '>')
            {
                Flush(); string op = c.ToString();
                if (i + 1 < input.Length && input[i + 1] == c && c is '|' or '&' or '>') { op += c; i++; }
                if (op == "&") throw new ArgumentException("Background '&' execution is not supported.");
                tokens.Add(new(op, true)); continue;
            }
            word.Append(c); started = true;
        }
        if (quote != '\0') throw new ArgumentException("Unclosed quote.");
        Flush(); return tokens;
    }

    private static List<ShellPipeline> ParseShell(List<ShellToken> tokens)
    {
        // Expand command-position aliases before parsing operators; cap cycles and expansion size.
        bool commandPosition = true;
        int expansions = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (commandPosition && !token.Operator && Aliases.TryGetValue(token.Value, out string? alias))
            {
                if (++expansions > 32) throw new ArgumentException("Alias expansion limit exceeded (possible cycle).");
                tokens.RemoveAt(i); tokens.InsertRange(i, LexShell(alias)); i--; continue;
            }
            commandPosition = token.Operator && token.Value is "|" or "&&" or "||";
        }
        List<ShellPipeline> result = [];
        List<ShellStage> stages = []; ShellStage stage = new(); string condition = "";
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!token.Operator) { stage.Args.Add(token.Value); continue; }
            if (token.Value is "<" or ">" or ">>")
            {
                if (++i >= tokens.Count || tokens[i].Operator) throw new ArgumentException("Redirection requires a path.");
                if (token.Value == "<")
                { if (stage.Input != null) throw new ArgumentException("Duplicate input redirection."); stage.Input = tokens[i].Value; }
                else
                { if (stage.Output != null) throw new ArgumentException("Duplicate output redirection."); stage.Output = tokens[i].Value; stage.Append = token.Value == ">>"; }
                continue;
            }
            if (stage.Args.Count == 0) throw new ArgumentException("Missing command before operator.");
            stages.Add(stage); stage = new();
            if (token.Value != "|") { result.Add(new(condition, stages)); condition = token.Value; stages = []; }
        }
        if (stage.Args.Count == 0) throw new ArgumentException("Missing command.");
        stages.Add(stage); result.Add(new(condition, stages)); return result;
    }

    public static void ExecuteCommand(string rawInput, ref bool isRunning, bool isScriptExecution)
    {
        if (string.IsNullOrWhiteSpace(rawInput)) return;
        if (SessionIdentity != Import.Variables.installPath + "|" + Import.Variables.userName) ResetShellSession();
        if (ExecutionDepth >= 32) { ShellError("shell: script recursion limit exceeded."); return; }
        if (ExecutionDepth == 0 && !isScriptExecution)
        { History.Add(rawInput); if (History.Count > 1000) History.RemoveAt(0); }
        TextWriter output = Console.Out;
        TextWriter? previousDiagnostics = DiagnosticOutput;
        string? previousInput = ShellInput;
        DiagnosticOutput ??= output;
        ExecutionDepth++;
        try
        {
            var pipelines = ParseShell(LexShell(rawInput));
            foreach (var pipeline in pipelines)
            {
                if (!isRunning) break;
                if (pipeline.Condition == "&&" && LastExitCode != 0 || pipeline.Condition == "||" && LastExitCode == 0) continue;
                string? piped = null;
                for (int i = 0; i < pipeline.Stages.Count && isRunning; i++)
                {
                    var stage = pipeline.Stages[i];
                    string Expand(string value) => System.Text.RegularExpressions.Regex.Replace(value, "\u0001([^\u0002]*)\u0002", match => EnvironmentValue(match.Groups[1].Value));
                    string[] arguments = stage.Args.Select(Expand).ToArray();
                    string? inputPath = stage.Input is null ? null : Expand(stage.Input);
                    string? outputPath = stage.Output is null ? null : Expand(stage.Output);
                    bool capture = stage.Output != null || i + 1 < pipeline.Stages.Count;
                    LastExitCode = 0;
                    ShellInput = piped;
                    if (stage.Input != null)
                    {
                        try { ShellInput = Encoding.UTF8.GetString(VirtualFileSystem.ReadBytes(inputPath!, MaximumDownloadBytes)); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                        { ShellError(ex.Message); break; }
                    }
                    if (outputPath != null)
                    {
                        try { VirtualFileSystem.ValidateWriteDestination(outputPath); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                        { ShellError(ex.Message); break; }
                    }
                    if ((capture || ShellInput != null) && !SupportsTextIO(arguments[0]))
                    { ShellError($"{stage.Args[0]}: interactive command cannot use pipes or redirection."); break; }
                    using ShellOutputBuffer buffer = new();
                    if (capture) Console.SetOut(buffer);
                    try { ExecuteSimpleCommand(arguments, ref isRunning, isScriptExecution); }
                    finally { Console.SetOut(output); }
                    string content = buffer.ToString();
                    if (stage.Output != null && LastExitCode == 0)
                    {
                        if (!VirtualFileSystem.WriteFile(outputPath!, content, stage.Append, out string error)) ShellError(error);
                    }
                    piped = stage.Output != null ? "" : content;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        { ShellError($"shell: {ex.Message}"); }
        finally
        {
            Console.SetOut(output); ShellInput = previousInput;
            ExecutionDepth--; DiagnosticOutput = previousDiagnostics;
        }
    }

    private static bool SupportsTextIO(string command) => !new[]
    { "top", "vim", "edit", "code", "ide", "store", "shop", "surfai", "ai", "music", "game", "calc", "calculator", "clear", "cleart", "uninstall", "shutdown", "exit", "reboot", "logout", "run", "builder", "package-builder", "surf", "surfos" }
        .Contains(command, StringComparer.OrdinalIgnoreCase);

    private static bool ReadCommandFile(string path, out string content, out string error)
    {
        if (path == "-" && ShellInput != null) { content = ShellInput; error = ""; return true; }
        return VirtualFileSystem.ReadFile(path, out content, out error);
    }
}
