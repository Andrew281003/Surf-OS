using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace SurfOS2.os_Apps
{
    public class CodeEditor
    {
        [SupportedOSPlatform("windows")]

        private const string ProjectsFolderName = "Projects";
        private static string lastProject = string.Empty;

        public static void Launch(string? requestedProject = null)
        {
            try
            {
                LaunchCore(requestedProject);
            }
            catch (Exception ex)
            {
                KernelPanic.ShowAndHandle(
                    ex,
                    "src/Apps/codeEditor.cs",
                    "Reboot SurfOS. If SurfCode keeps crashing, start Safe Mode and check the project files.");
            }
        }

        private static void LaunchCore(string? requestedProject = null)
        {
            EnsureProjectsRoot();

            if (!string.IsNullOrWhiteSpace(requestedProject))
            {
                string requestedPath = Path.Combine(GetProjectsRoot(), CleanName(requestedProject));
                if (Directory.Exists(requestedPath))
                {
                    OpenProject(requestedPath);
                    return;
                }

                Console.WriteLine($"Project '{requestedProject}' was not found.");
                Pause();
            }

            bool running = true;
            while (running)
            {
                Console.Clear();
                DrawShellHeader("SurfCode IDE");
                Console.WriteLine("[1] New project");
                Console.WriteLine($"[2] Open project{(string.IsNullOrWhiteSpace(lastProject) ? string.Empty : $" (last: {lastProject})")}");
                Console.WriteLine("[3] List projects");
                Console.WriteLine("[0] Exit IDE");
                Console.Write("\nSelect: ");
                char input = ReadMenuKey();

                switch (input)
                {
                    case '1':
                        CreateProject();
                        break;
                    case '2':
                        SelectProject();
                        break;
                    case '3':
                        ListProjects();
                        break;
                    case '0':
                        running = false;
                        break;
                    default:
                        Pause();
                        break;
                }
            }
        }

        public static void PrintScreen()
        {
            DrawShellHeader("SurfCode IDE");
            Console.WriteLine("[1] New Project");
            Console.WriteLine($"[2] Open a project (Last project: {lastProject})");
            Console.WriteLine("[0] Exit");
        }

        private static void CreateProject()
        {
            Console.Clear();
            DrawShellHeader("New Project");
            Console.Write("Project name: ");
            string projectName = CleanName(Console.ReadLine() ?? string.Empty);

            if (!PathSafety.IsSafeFileName(projectName))
            {
                Console.WriteLine("Project name is blank or not valid on Windows.");
                Pause();
                return;
            }

            string projectRoot = Path.Combine(GetProjectsRoot(), projectName);
            if (Directory.Exists(projectRoot))
            {
                Console.WriteLine("A project with that name already exists.");
                Pause();
                return;
            }

            Directory.CreateDirectory(projectRoot);

            string csprojPath = Path.Combine(projectRoot, $"{projectName}.csproj");
            File.WriteAllText(
                csprojPath,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net9.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """);

            File.WriteAllText(
                Path.Combine(projectRoot, "Program.cs"),
                string.Join(
                    Environment.NewLine,
                    $"namespace {MakeNamespace(projectName)};",
                    string.Empty,
                    "internal class Program",
                    "{",
                    "    private static void Main()",
                    "    {",
                    $"        Console.WriteLine(\"Hello from {projectName} on SurfOS.\");",
                    "    }",
                    "}",
                    string.Empty));

            File.WriteAllText(
                Path.Combine(projectRoot, "README.md"),
                $"# {projectName}{Environment.NewLine}{Environment.NewLine}Created in SurfCode IDE.{Environment.NewLine}");

            lastProject = projectName;
            Console.WriteLine($"Project '{projectName}' created.");
            Pause();
            OpenProject(projectRoot);
        }

        private static void SelectProject()
        {
            string[] projects = GetProjectDirectories();
            if (projects.Length == 0)
            {
                Console.WriteLine("No projects exist yet.");
                Pause();
                return;
            }

            Console.Clear();
            DrawShellHeader("Open Project");
            int visibleProjectCount = Math.Min(projects.Length, 9);
            for (int i = 0; i < visibleProjectCount; i++)
            {
                Console.WriteLine($"[{i + 1}] {Path.GetFileName(projects[i])}");
            }

            if (projects.Length > visibleProjectCount)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"Showing first {visibleProjectCount} of {projects.Length} projects.");
                Screen_Print.ResetColors();
            }

            if (!string.IsNullOrWhiteSpace(lastProject))
            {
                Console.WriteLine("[L] Open last project");
            }

            Console.Write("\nSelect: ");
            char input = ReadMenuKey();

            if (input == 'l' && !string.IsNullOrWhiteSpace(lastProject))
            {
                string lastPath = Path.Combine(GetProjectsRoot(), lastProject);
                if (Directory.Exists(lastPath))
                {
                    OpenProject(lastPath);
                    return;
                }
            }

            if (char.IsDigit(input))
            {
                int selected = input - '0';
                if (selected >= 1 && selected <= visibleProjectCount)
                {
                    OpenProject(projects[selected - 1]);
                    return;
                }
            }

            Console.WriteLine("Invalid project selection.");
            Pause();
        }

        private static void OpenProject(string projectRoot)
        {
            lastProject = Path.GetFileName(projectRoot);

            bool inProject = true;
            while (inProject)
            {
                Console.Clear();
                DrawShellHeader($"Project: {lastProject}");
                PrintProjectTree(projectRoot);
                Console.WriteLine();
                Console.WriteLine("[1] Open/edit file");
                Console.WriteLine("[2] New file");
                Console.WriteLine("[3] Build project");
                Console.WriteLine("[4] Run project");
                Console.WriteLine("[5] Project info");
                Console.WriteLine("[6] Format project");
                Console.WriteLine("[7] Debug project");
                Console.WriteLine("[0] Close project");
                Console.Write("\nSelect: ");

                char input = ReadMenuKey();

                switch (input)
                {
                    case '1':
                        PromptEditFile(projectRoot);
                        break;
                    case '2':
                        PromptCreateFile(projectRoot);
                        break;
                    case '3':
                        BuildProject(projectRoot);
                        break;
                    case '4':
                        RunProject(projectRoot);
                        break;
                    case '5':
                        ShowProjectInfo(projectRoot);
                        break;
                    case '6':
                        FormatProject(projectRoot);
                        break;
                    case '7':
                        DebugProject(projectRoot);
                        break;
                    case '0':
                        inProject = false;
                        break;
                    default:
                        Console.WriteLine("Unknown project command.");
                        Pause();
                        break;
                }
            }
        }

        private static void PromptEditFile(string projectRoot)
        {
            Console.Write("File path: ");
            string relativePath = Console.ReadLine() ?? string.Empty;

            if (!TryResolveProjectPath(projectRoot, relativePath, out string fullPath))
            {
                Console.WriteLine("That file path is outside the project.");
                Pause();
                return;
            }

            EditFile(projectRoot, fullPath);
        }

        private static void PromptCreateFile(string projectRoot)
        {
            Console.Write("New file path: ");
            string relativePath = Console.ReadLine() ?? string.Empty;

            if (!TryResolveProjectPath(projectRoot, relativePath, out string fullPath))
            {
                Console.WriteLine("That file path is outside the project.");
                Pause();
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? projectRoot);
            if (!File.Exists(fullPath))
            {
                File.WriteAllText(fullPath, string.Empty);
            }

            EditFile(projectRoot, fullPath);
        }

        private static void EditFile(
            string projectRoot,
            string fullPath,
            BuildDiagnostic? activeDiagnostic = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? projectRoot);
            List<string> lines = File.Exists(fullPath)
                ? File.ReadAllLines(fullPath).ToList()
                : new List<string>();

            bool editing = true;
            int? insertIndex = null;
            while (editing)
            {
                Console.Clear();
                DrawShellHeader($"Editing {Path.GetRelativePath(projectRoot, fullPath)}");
                if (activeDiagnostic is not null)
                {
                    PrintDiagnostic(activeDiagnostic);
                }

                PrintNumberedLines(lines, activeDiagnostic?.LineNumber);
                Console.WriteLine();
                Console.WriteLine("Commands: :w save | :q close | :wq save+close | :fmt format | :doc C# lexicon | :i n insert mode | :set n text | :d n | :clear");
                Console.WriteLine("Type normal text to append lines. In insert mode, use :esc to go back to appending.");
                Console.Write(insertIndex is null ? "> " : $"insert:{insertIndex.Value + 1}> ");

                string input = Console.ReadLine() ?? string.Empty;

                if (insertIndex is not null && !input.StartsWith(':'))
                {
                    lines.Insert(insertIndex.Value, input);
                    insertIndex++;
                    activeDiagnostic = null;
                }
                else if (input == ":esc")
                {
                    insertIndex = null;
                }
                else if (input == ":q")
                {
                    editing = false;
                }
                else if (input == ":w")
                {
                    File.WriteAllLines(fullPath, lines);
                    activeDiagnostic = null;
                    Console.WriteLine("Saved.");
                    Pause();
                }
                else if (input == ":wq")
                {
                    File.WriteAllLines(fullPath, lines);
                    activeDiagnostic = null;
                    editing = false;
                }
                else if (input == ":clear")
                {
                    lines.Clear();
                    insertIndex = null;
                    activeDiagnostic = null;
                }
                else if (input == ":fmt")
                {
                    lines = FormatCodeLines(fullPath, lines);
                    insertIndex = null;
                    activeDiagnostic = null;
                }
                else if (input == ":doc")
                {
                    ShowCSharpLexicon();
                }
                else if (TryReadNumberCommand(input, ":i", out int insertLine))
                {
                    insertIndex = Math.Clamp(insertLine - 1, 0, lines.Count);
                    activeDiagnostic = null;
                }
                else if (TrySplitLineCommand(input, ":set", out int setLine, out string setText))
                {
                    if (setLine >= 1 && setLine <= lines.Count)
                    {
                        lines[setLine - 1] = setText;
                        activeDiagnostic = null;
                    }
                }
                else if (TryReadNumberCommand(input, ":d", out int deleteLine) ||
                         TryReadNumberCommand(input, ":del", out deleteLine))
                {
                    if (deleteLine >= 1 && deleteLine <= lines.Count)
                    {
                        lines.RemoveAt(deleteLine - 1);
                        if (insertIndex is not null)
                        {
                            insertIndex = Math.Clamp(insertIndex.Value, 0, lines.Count);
                        }
                        activeDiagnostic = null;
                    }
                }
                else if (input.StartsWith(':'))
                {
                    Console.WriteLine("Unknown editor command.");
                    Pause();
                }
                else
                {
                    lines.Add(input);
                    activeDiagnostic = null;
                }
            }
        }

        private static void BuildProject(string projectRoot)
        {
            RunDotnet(projectRoot, "build", openDiagnostics: true);
        }

        private static void DebugProject(string projectRoot)
        {
            RunDotnet(projectRoot, "build", openDiagnostics: true);
        }

        private static void RunProject(string projectRoot)
        {
            RunDotnetInteractive(projectRoot, "run --no-build");
        }

        private static void RunDotnet(string projectRoot, string command, bool openDiagnostics)
        {
            if (!Directory.GetFiles(projectRoot, "*.csproj").Any())
            {
                Console.WriteLine("This project does not have a .csproj file.");
                Pause();
                return;
            }

            Console.Clear();
            DrawShellHeader($"dotnet {command}");
            try
            {
                using Process process = new();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = command,
                    WorkingDirectory = projectRoot,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process.Start();
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                string output = outputTask.GetAwaiter().GetResult();
                string errors = errorTask.GetAwaiter().GetResult();

                Console.WriteLine(output);
                if (!string.IsNullOrWhiteSpace(errors))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(errors);
                    Screen_Print.ResetColors();
                }

                Console.WriteLine($"Process exited with code {process.ExitCode}.");
                if (openDiagnostics && process.ExitCode != 0)
                {
                    BuildDiagnostic? diagnostic = FindFirstDiagnostic(projectRoot, output + Environment.NewLine + errors);
                    if (diagnostic is not null)
                    {
                        ShowDiagnostic(projectRoot, diagnostic);
                        EditFile(projectRoot, diagnostic.FilePath, diagnostic);
                        return;
                    }

                    if (TryShowLockedProcessDiagnostic(output + Environment.NewLine + errors))
                    {
                        Pause();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not start dotnet: {ex.Message}");
            }

            Pause();
        }

        private static void RunDotnetInteractive(string projectRoot, string command)
        {
            if (!Directory.GetFiles(projectRoot, "*.csproj").Any())
            {
                Console.WriteLine("This project does not have a .csproj file.");
                Pause();
                return;
            }

            Console.Clear();
            DrawShellHeader($"dotnet {command}");
            try
            {
                using Process process = new();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = command,
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };

                process.Start();
                process.WaitForExit();
                Console.WriteLine();
                Console.WriteLine($"Process exited with code {process.ExitCode}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not start dotnet: {ex.Message}");
            }

            Pause();
        }

        private static void FormatProject(string projectRoot)
        {
            Console.Clear();
            DrawShellHeader("Format Project");

            string[] codeFiles = Directory.GetFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                               !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .ToArray();

            foreach (string file in codeFiles)
            {
                List<string> formatted = FormatCodeLines(file, File.ReadAllLines(file).ToList());
                File.WriteAllLines(file, formatted);
                Console.WriteLine($"Formatted {Path.GetRelativePath(projectRoot, file)}");
            }

            Console.WriteLine($"\nFormatted {codeFiles.Length} C# file(s).");
            Pause();
        }

        private static void ShowCSharpLexicon()
        {
            Console.Clear();
            DrawShellHeader("C# Lexicon");
            Console.WriteLine("""
                Program shape:
                  namespace Name { ... }     Groups code under a name.
                  class Name { ... }         Defines a reference type.
                  struct Name { ... }        Defines a value type.
                  interface Name { ... }     Defines a contract.
                  enum Name { A, B }         Defines named numeric values.
                  Main()                     App entry point.

                Common types:
                  string    Text
                  char      One character
                  bool      true or false
                  int       Whole number
                  long      Large whole number
                  float     Decimal number, f suffix
                  double    Decimal number
                  decimal   Money/precise decimal, m suffix
                  var       Compiler infers the type
                  object    Base type for all C# values
                  void      Returns nothing

                Access and modifiers:
                  public       Visible everywhere
                  private      Visible only inside this type
                  protected    Visible in this type and derived types
                  internal     Visible inside this project
                  static       Belongs to the type, not an instance
                  readonly     Assigned only at declaration or constructor
                  const        Compile-time constant
                  async        Method can use await

                Flow:
                  if / else                  Branch on a condition
                  switch / case / default    Multi-branch selection
                  for                        Counted loop
                  foreach                    Loop through a collection
                  while                      Loop while condition is true
                  do / while                 Run once, then loop
                  break                      Exit loop/switch
                  continue                   Skip to next loop iteration
                  return                     Leave method
                  try / catch / finally      Error handling
                  throw                      Raise an exception

                Operators:
                  =      assign              ==     equals
                  !=     not equals          < > <= >= comparisons
                  + - * / % math             ++ -- increment/decrement
                  &&     and                 ||     or
                  !      not                 ??     fallback if null
                  ?.     safe member access  =>     lambda/expression body

                Semantics:
                  Compile time vs runtime
                    Compile time is when C# checks syntax and types.
                    Runtime is when your built program actually executes.

                  Types and values
                    A variable has a type, and the type controls what values it can hold.
                    int age = 15; means age stores whole numbers.
                    string name = "Ada"; means name stores text.

                  Assignment vs comparison
                    = changes a variable.
                    == asks whether two values are equal.

                  Scope
                    A name only exists inside the braces where it was declared.
                    A variable declared inside Main cannot be used outside Main.

                  Control flow
                    Code normally runs top to bottom.
                    if, loops, return, break, and exceptions change the path.

                  Methods
                    A method is a named block of reusable behavior.
                    Parameters are inputs. return sends an output back.

                  static vs instance
                    static belongs to the class itself.
                    Instance members belong to an object created with new.

                  Value types vs reference types
                    int, bool, char, double, decimal, structs, and enums store values directly.
                    string, arrays, classes, and List<T> are references to objects.

                  Null
                    null means no object/value is there.
                    Console.ReadLine() can return null, so use ?? for a fallback.

                  Exceptions
                    Exceptions stop normal flow when something goes wrong.
                    try/catch lets your program handle the problem instead of crashing.

                Nullability:
                  string? name               string can be null
                  string name = value ?? ""  fallback when value is null
                  value!                     tell compiler value is not null

                Console:
                  Console.WriteLine("Hi");              Print line
                  Console.Write("Name: ");              Print without newline
                  string name = Console.ReadLine() ?? ""; Read text safely
                  Console.Clear();                      Clear screen

                Collections:
                  string[] args              Array
                  List<string> names = new(); Dynamic list
                  names.Add("Kai");          Add item
                  names.Count                Item count

                Useful using directives:
                  using System;
                  using System.Collections.Generic;
                  using System.IO;
                  using System.Linq;
                  using System.Threading.Tasks;

                Tiny template:
                  namespace MyApp;

                  internal class Program
                  {
                      private static void Main()
                      {
                          Console.WriteLine("Hello");
                      }
                  }
                """);

            Pause();
        }

        private static void ShowProjectInfo(string projectRoot)
        {
            Console.Clear();
            DrawShellHeader("Project Info");
            string[] files = Directory.GetFiles(projectRoot, "*", SearchOption.AllDirectories);
            long totalBytes = files.Sum(file => new FileInfo(file).Length);

            Console.WriteLine($"Name       : {Path.GetFileName(projectRoot)}");
            Console.WriteLine($"Location   : {projectRoot}");
            Console.WriteLine($"Files      : {files.Length}");
            Console.WriteLine($"Size       : {totalBytes} bytes");
            Console.WriteLine($"C# project : {(Directory.GetFiles(projectRoot, "*.csproj").Any() ? "yes" : "no")}");
            Pause();
        }

        private static void ListProjects()
        {
            Console.Clear();
            DrawShellHeader("Projects");
            string[] projects = GetProjectDirectories();

            if (projects.Length == 0)
            {
                Console.WriteLine("No projects exist yet.");
            }
            else
            {
                foreach (string project in projects)
                {
                    Console.WriteLine($"- {Path.GetFileName(project)}");
                }
            }

            Pause();
        }

        private static void PrintProjectTree(string projectRoot)
        {
            string[] files = Directory.GetFiles(projectRoot, "*", SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                               !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Files:");
            Screen_Print.ResetColors();

            if (files.Length == 0)
            {
                Console.WriteLine("  (empty)");
                return;
            }

            foreach (string file in files)
            {
                Console.WriteLine($"  {Path.GetRelativePath(projectRoot, file)}");
            }
        }

        private static void PrintNumberedLines(List<string> lines, int? highlightLine = null)
        {
            if (lines.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  (empty file)");
                Screen_Print.ResetColors();
                return;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                bool isHighlighted = highlightLine == i + 1;
                Console.ForegroundColor = isHighlighted ? ConsoleColor.Black : ConsoleColor.DarkGray;
                if (isHighlighted)
                {
                    Console.BackgroundColor = ConsoleColor.Yellow;
                }

                Console.Write($"{i + 1,4} | ");
                if (!isHighlighted)
                {
                    Screen_Print.ResetColors();
                }

                Console.WriteLine(lines[i]);
                Screen_Print.ResetColors();
            }
        }

        private static List<string> FormatCodeLines(string filePath, List<string> lines)
        {
            if (!filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                return TrimTrailingWhitespace(lines);
            }

            List<string> formatted = new();
            int indentLevel = 0;

            foreach (string originalLine in lines)
            {
                string line = originalLine.Trim();
                if (line.Length == 0)
                {
                    formatted.Add(string.Empty);
                    continue;
                }

                if (line.StartsWith('}'))
                {
                    indentLevel = Math.Max(0, indentLevel - 1);
                }

                formatted.Add(new string(' ', indentLevel * 4) + line);

                int openBraces = line.Count(ch => ch == '{');
                int closeBraces = line.Count(ch => ch == '}');
                indentLevel = Math.Max(0, indentLevel + openBraces - closeBraces);
            }

            return TrimTrailingWhitespace(formatted);
        }

        private static List<string> TrimTrailingWhitespace(List<string> lines)
        {
            return lines.Select(line => line.TrimEnd()).ToList();
        }

        private static BuildDiagnostic? FindFirstDiagnostic(string projectRoot, string output)
        {
            foreach (string outputLine in output.Split(Environment.NewLine))
            {
                Match match = Regex.Match(
                    outputLine,
                    @"^(?<file>.*?\.cs)\((?<line>\d+),(?<column>\d+)\): (?<level>error|warning) (?<code>[A-Z]+\d+): (?<message>.*)$",
                    RegexOptions.IgnoreCase);

                if (!match.Success)
                {
                    continue;
                }

                string filePath = match.Groups["file"].Value.Trim();
                if (!Path.IsPathRooted(filePath))
                {
                    filePath = Path.GetFullPath(Path.Combine(projectRoot, filePath));
                }

                if (!TryResolveProjectPath(projectRoot, Path.GetRelativePath(projectRoot, filePath), out string resolvedPath) ||
                    !File.Exists(resolvedPath))
                {
                    continue;
                }

                return new BuildDiagnostic(
                    resolvedPath,
                    int.Parse(match.Groups["line"].Value),
                    int.Parse(match.Groups["column"].Value),
                    match.Groups["level"].Value,
                    match.Groups["code"].Value,
                    match.Groups["message"].Value.Trim());
            }

            return null;
        }

        private static void ShowDiagnostic(string projectRoot, BuildDiagnostic diagnostic)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine("Build failed. Opening problem file...");
            Screen_Print.ResetColors();
            Console.WriteLine($"{Path.GetRelativePath(projectRoot, diagnostic.FilePath)}:{diagnostic.LineNumber}:{diagnostic.ColumnNumber}");
            Console.WriteLine($"{diagnostic.Level.ToUpperInvariant()} {diagnostic.Code}: {diagnostic.Message}");
            Thread.Sleep(1200);
        }

        private static bool TryShowLockedProcessDiagnostic(string output)
        {
            Match match = Regex.Match(
                output,
                @"The file is locked by: ""(?<name>.+?) \((?<pid>\d+)\)""",
                RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                return false;
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine("Build failed because the project executable is still running.");
            Screen_Print.ResetColors();
            Console.WriteLine($"Locked by : {match.Groups["name"].Value}");
            Console.WriteLine($"Process ID: {match.Groups["pid"].Value}");
            Console.WriteLine();
            Console.WriteLine("Close that program window, or stop the process, then build again.");

            Console.WriteLine("Tip: Run uses 'dotnet run --no-build' so it will not rebuild unless you choose Build or Debug.");
            return true;
        }

        private static void PrintDiagnostic(BuildDiagnostic diagnostic)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{diagnostic.Level.ToUpperInvariant()} {diagnostic.Code} at line {diagnostic.LineNumber}, column {diagnostic.ColumnNumber}");
            Screen_Print.ResetColors();
            Console.WriteLine(diagnostic.Message);
            Console.WriteLine();
        }

        private static bool TryResolveProjectPath(string projectRoot, string relativePath, out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            string root = Path.GetFullPath(projectRoot);
            string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
            bool isInsideRoot = candidate.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                                candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

            if (!isInsideRoot)
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }

        private static bool TrySplitLineCommand(string input, string command, out int lineNumber, out string text)
        {
            lineNumber = 0;
            text = string.Empty;

            if (!input.StartsWith(command + " ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string[] parts = input.Split(' ', 3, StringSplitOptions.None);
            if (parts.Length < 3 || !int.TryParse(parts[1], out lineNumber))
            {
                return false;
            }

            text = parts[2];
            return true;
        }

        private static bool TryReadNumberCommand(string input, string command, out int lineNumber)
        {
            lineNumber = 0;
            if (!input.StartsWith(command + " ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return int.TryParse(input[(command.Length + 1)..], out lineNumber);
        }

        private static string[] GetProjectDirectories()
        {
            EnsureProjectsRoot();
            return Directory.GetDirectories(GetProjectsRoot())
                .OrderBy(directory => Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string GetProjectsRoot()
        {
            string installPath = string.IsNullOrWhiteSpace(Import.Variables.installPath)
                ? Environment.CurrentDirectory
                : Import.Variables.installPath;

#pragma warning disable CA1416
            string projectsFolder = ProjectsFolderName;
#pragma warning restore CA1416

            return Path.Combine(installPath, projectsFolder);
        }

        private static void EnsureProjectsRoot()
        {
            Directory.CreateDirectory(GetProjectsRoot());
        }

        private static string CleanName(string value)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string cleaned = new(value.Trim()
                .Select(ch => invalidChars.Contains(ch) ? '-' : ch)
                .ToArray());

            return cleaned.Replace(' ', '-');
        }

        private static string MakeNamespace(string projectName)
        {
            string cleaned = new(projectName
                .Where(char.IsLetterOrDigit)
                .ToArray());

            return string.IsNullOrWhiteSpace(cleaned) ? "SurfProject" : cleaned;
        }

        private static void DrawShellHeader(string title)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"<- {title} ->");
            Console.WriteLine();
            Screen_Print.ResetColors();
        }

        private static char ReadMenuKey()
        {
            return char.ToLowerInvariant(Console.ReadKey(intercept: true).KeyChar);
        }

        private static void Pause()
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\nPress ENTER to continue...");
            Screen_Print.ResetColors();
            Console.ReadLine();
        }

        private sealed record BuildDiagnostic(
            string FilePath,
            int LineNumber,
            int ColumnNumber,
            string Level,
            string Code,
            string Message);
    }
}
