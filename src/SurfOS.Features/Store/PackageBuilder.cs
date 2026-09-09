using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SurfOS2;

internal enum PackageTemplate
{
    Theme,
    Game,
    Program,
    Extension
}

internal sealed class BuilderProjectManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PackageTemplate Template { get; set; }
    public string EntryFile { get; set; } = string.Empty;
}

/// <summary>
/// A local workshop for authoring, validating, previewing, and exporting SurfOS packages.
/// Projects live under the apps directory, keeping package authoring together with installed app content.
/// </summary>
internal static class PackageBuilder
{
    private const int MaximumSourceBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static void HandleCommand(string[] args, int commandIndex)
    {
        if (args.Length == commandIndex + 1)
        {
            Open();
            return;
        }

        string action = args[commandIndex + 1].ToLowerInvariant();
        switch (action)
        {
            case "new" when args.Length == commandIndex + 4:
                if (!TryParseTemplate(args[commandIndex + 2], out PackageTemplate template))
                {
                    Console.WriteLine("Unknown template. Choose theme, game, program, or extension.");
                    return;
                }

                string id = NormalizeId(args[commandIndex + 3]);
                bool created = TryCreateProject(
                    template,
                    id,
                    ToDisplayName(id),
                    Import.Variables.userName,
                    out string projectPath,
                    out string createError);
                Console.WriteLine(created ? $"Created {projectPath}" : createError);
                break;

            case "list" when args.Length == commandIndex + 2:
                PrintProjects();
                break;

            case "validate" when args.Length == commandIndex + 3:
                PrintValidation(args[commandIndex + 2]);
                break;

            case "export" when args.Length == commandIndex + 3:
                bool exported = TryExportProject(args[commandIndex + 2], out string exportPath, out string exportError);
                Console.WriteLine(exported ? $"Exported {exportPath}" : exportError);
                break;

            default:
                PrintUsage();
                break;
        }
    }

    public static void Open()
    {
        EnsureWorkspace();
        bool open = true;
        while (open)
        {
            Console.Clear();
            DrawHeader("PACKAGE BUILDER", "Themes, games, programs, and extensions");
            string[] projects = GetProjectDirectories();
            Console.WriteLine($"  Workspace   {GetWorkspaceRoot()}");
            Console.WriteLine($"  Projects    {projects.Length}");
            Console.WriteLine();
            Console.WriteLine("  [1] Create package");
            Console.WriteLine("  [2] Open package");
            Console.WriteLine("  [3] List workspace");
            Console.WriteLine("  [0] Return to terminal");
            Console.Write("\n  Select: ");

            switch (ReadMenuKey())
            {
                case '1': CreateProjectWizard(); break;
                case '2': OpenProjectPicker(); break;
                case '3': Console.Clear(); PrintProjects(); Pause(); break;
                case '0': open = false; break;
                default: Pause("Unknown selection."); break;
            }
        }
    }

    internal static bool TryCreateProject(
        PackageTemplate template,
        string id,
        string name,
        string author,
        out string projectPath,
        out string error)
    {
        projectPath = string.Empty;
        error = string.Empty;
        id = NormalizeId(id);
        if (!IsValidId(id))
        {
            error = "Package IDs may contain only letters, numbers, dots, dashes, and underscores.";
            return false;
        }

        EnsureWorkspace();
        projectPath = Path.Combine(GetWorkspaceRoot(), id);
        if (Directory.Exists(projectPath))
        {
            error = $"Package '{id}' already exists.";
            return false;
        }

        string sourceRoot = Path.Combine(projectPath, "src");
        Directory.CreateDirectory(sourceRoot);
        string entryFile = template == PackageTemplate.Theme ? $"src/{id}.theme.json" : "src/main.surf";
        BuilderProjectManifest manifest = new()
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(name) ? ToDisplayName(id) : name.Trim(),
            Author = string.IsNullOrWhiteSpace(author) ? "SurfOS Creator" : author.Trim(),
            Description = $"A {template.ToString().ToLowerInvariant()} package for SurfOS.",
            Template = template,
            EntryFile = entryFile
        };

        File.WriteAllText(
            Path.Combine(projectPath, "package.json"),
            JsonSerializer.Serialize(manifest, JsonOptions));
        if (template == PackageTemplate.Theme)
        {
            File.WriteAllText(
                Path.Combine(sourceRoot, $"{id}.theme.json"),
                JsonSerializer.Serialize(new Import.SurfTheme
                {
                    ThemeName = id,
                    WindowTitle = $"{manifest.Name} - SurfOS",
                    TargetColor = "Cyan",
                    BackgroundColor = "Black",
                    FontName = "Consolas",
                    UILayout = "Default",
                    WelcomeMessage = $"{manifest.Name} is ready.",
                    PromptStyle = "Linux",
                    AsciiArt = $"~~ {manifest.Name} ~~",
                    AuthorSignature = manifest.Author
                }, JsonOptions));
        }
        else
        {
            File.WriteAllText(
                Path.Combine(sourceRoot, "main.surf"),
                template switch
                {
                    PackageTemplate.Game => $"# {manifest.Name}\necho \"Welcome to {manifest.Name}!\"\necho \"Edit src/main.surf to build your game.\"\n",
                    PackageTemplate.Program => $"# {manifest.Name}\necho \"{manifest.Name} is running.\"\n",
                    _ => $"# {manifest.Name}\necho \"{manifest.Name} extension loaded.\"\n"
                });
        }

        File.WriteAllText(
            Path.Combine(projectPath, "README.md"),
            $"# {manifest.Name}{Environment.NewLine}{Environment.NewLine}" +
            $"Created with SurfOS Package Builder. Edit `{entryFile}` and run `builder validate {id}` before exporting.{Environment.NewLine}");
        return true;
    }

    internal static bool TryValidateProject(string id, out BuilderProjectManifest? manifest, out List<string> issues)
    {
        issues = [];
        manifest = null;
        if (!TryGetProjectPath(id, out string projectPath))
        {
            issues.Add($"Package '{id}' was not found.");
            return false;
        }

        string manifestPath = Path.Combine(projectPath, "package.json");
        try
        {
            manifest = JsonSerializer.Deserialize<BuilderProjectManifest>(File.ReadAllText(manifestPath), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            issues.Add($"package.json could not be read: {ex.Message}");
            return false;
        }

        if (manifest is null) issues.Add("package.json is empty.");
        if (manifest is null) return false;
        if (!IsValidId(manifest.Id) || !manifest.Id.Equals(Path.GetFileName(projectPath), StringComparison.OrdinalIgnoreCase))
            issues.Add("The manifest ID is invalid or does not match the project folder.");
        if (string.IsNullOrWhiteSpace(manifest.Name)) issues.Add("Name is required.");
        if (string.IsNullOrWhiteSpace(manifest.Author)) issues.Add("Author is required.");
        if (!Version.TryParse(manifest.Version, out _)) issues.Add("Version must use a numeric form such as 1.0.0.");
        if (!PathSafety.TryResolveRelativePath(projectPath, manifest.EntryFile, out string entryPath) || !File.Exists(entryPath))
        {
            issues.Add("EntryFile is missing or points outside the project.");
            return false;
        }

        if (new FileInfo(entryPath).Length > MaximumSourceBytes) issues.Add("The package payload is larger than 4 MB.");
        if (manifest.Template == PackageTemplate.Theme)
        {
            try
            {
                Import.SurfTheme? theme = JsonSerializer.Deserialize<Import.SurfTheme>(File.ReadAllText(entryPath), JsonOptions);
                if (theme is null || string.IsNullOrWhiteSpace(theme.ThemeName)) issues.Add("The theme needs a ThemeName.");
                if (theme is not null && !Enum.TryParse(theme.TargetColor, true, out ConsoleColor _)) issues.Add("TargetColor is not a console color.");
                if (theme is not null && !Enum.TryParse(theme.BackgroundColor, true, out ConsoleColor _)) issues.Add("BackgroundColor is not a console color.");
            }
            catch (JsonException ex)
            {
                issues.Add($"Theme JSON is invalid: {ex.Message}");
            }
        }
        else if (string.IsNullOrWhiteSpace(File.ReadAllText(entryPath)))
        {
            issues.Add("The Surf script is empty.");
        }

        return issues.Count == 0;
    }

    internal static bool TryExportProject(string id, out string exportPath, out string error)
    {
        exportPath = string.Empty;
        if (!TryValidateProject(id, out BuilderProjectManifest? project, out List<string> issues) || project is null)
        {
            error = string.Join(Environment.NewLine, issues.Select(issue => $"- {issue}"));
            return false;
        }

        TryGetProjectPath(id, out string projectPath);
        PathSafety.TryResolveRelativePath(projectPath, project.EntryFile, out string entryPath);
        string exportsRoot = GetExportsRoot();
        Directory.CreateDirectory(exportsRoot);
        exportPath = Path.Combine(exportsRoot, $"{project.Id}-{project.Version}.surfpkg");
        string tempPath = exportPath + ".tmp";
        if (File.Exists(tempPath)) File.Delete(tempPath);

        byte[] payload = File.ReadAllBytes(entryPath);
        string payloadName = project.Template == PackageTemplate.Theme ? $"{project.Id}.json" : $"{project.Id}.surf";
        StorePackageManifest storeManifest = new()
        {
            Id = project.Id,
            Name = project.Name,
            Version = project.Version,
            Author = project.Author,
            Description = project.Description,
            Category = CategoryFor(project.Template),
            InstallPath = project.Template == PackageTemplate.Theme
                ? $"Packages/{payloadName}"
                : $"apps/{project.Id}/{payloadName}",
            Sha256 = Convert.ToHexString(SHA256.HashData(payload)),
            MinimumSurfOSVersion = Import.Variables.version,
            Command = project.Template == PackageTemplate.Theme
                ? $"theme load {project.Id}"
                : $"run /apps/{project.Id}/{project.Id}.surf"
        };

        try
        {
            using (ZipArchive archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry manifestEntry = archive.CreateEntry("manifest.json");
                using (StreamWriter writer = new(manifestEntry.Open()))
                    writer.Write(JsonSerializer.Serialize(storeManifest, JsonOptions));
                ZipArchiveEntry payloadEntry = archive.CreateEntry($"payload/{payloadName}");
                using Stream target = payloadEntry.Open();
                target.Write(payload);
            }

            File.Move(tempPath, exportPath, overwrite: true);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            error = $"Export failed: {ex.Message}";
            return false;
        }
    }

    private static void CreateProjectWizard()
    {
        Console.Clear();
        DrawHeader("NEW PACKAGE", "Choose a starting point");
        Console.WriteLine("  [1] Theme       Colors, typography, prompt, and ASCII art");
        Console.WriteLine("  [2] Game        A playable Surf command script");
        Console.WriteLine("  [3] Program     A general-purpose Surf command script");
        Console.WriteLine("  [4] Extension   A lightweight system add-on");
        Console.WriteLine("  [0] Cancel");
        Console.Write("\n  Template: ");
        PackageTemplate? template = ReadMenuKey() switch
        {
            '1' => PackageTemplate.Theme,
            '2' => PackageTemplate.Game,
            '3' => PackageTemplate.Program,
            '4' => PackageTemplate.Extension,
            _ => null
        };
        if (template is null) return;

        Console.Write("\n  Package name: ");
        string name = (Console.ReadLine() ?? string.Empty).Trim();
        Console.Write($"  Package ID [{NormalizeId(name)}]: ");
        string enteredId = (Console.ReadLine() ?? string.Empty).Trim();
        string id = NormalizeId(string.IsNullOrWhiteSpace(enteredId) ? name : enteredId);
        Console.Write($"  Author [{Import.Variables.userName}]: ");
        string author = (Console.ReadLine() ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(author)) author = Import.Variables.userName;

        if (TryCreateProject(template.Value, id, name, author, out string path, out string error))
        {
            Pause($"Created {path}\nNext: edit the source, validate it, then export.");
        }
        else Pause(error);
    }

    private static void OpenProjectPicker()
    {
        string[] projects = GetProjectDirectories();
        if (projects.Length == 0) { Pause("No packages yet. Create one first."); return; }
        Console.Clear();
        DrawHeader("OPEN PACKAGE", "Select a workspace");
        for (int i = 0; i < Math.Min(9, projects.Length); i++) Console.WriteLine($"  [{i + 1}] {Path.GetFileName(projects[i])}");
        Console.Write("\n  Select: ");
        char key = ReadMenuKey();
        if (!char.IsDigit(key)) return;
        int index = key - '1';
        if (index >= 0 && index < Math.Min(9, projects.Length)) ProjectDashboard(Path.GetFileName(projects[index]));
    }

    private static void ProjectDashboard(string id)
    {
        bool open = true;
        while (open)
        {
            Console.Clear();
            TryValidateProject(id, out BuilderProjectManifest? project, out List<string> issues);
            DrawHeader(project?.Name.ToUpperInvariant() ?? id.ToUpperInvariant(), project?.Template.ToString() ?? "Package");
            Console.WriteLine($"  Status      {(issues.Count == 0 ? "Ready to export" : $"Needs attention ({issues.Count})")}");
            Console.WriteLine($"  Source      {project?.EntryFile ?? "-"}");
            Console.WriteLine($"  Version     {project?.Version ?? "-"}");
            Console.WriteLine();
            Console.WriteLine("  [1] Validate");
            Console.WriteLine("  [2] Export .surfpkg");
            Console.WriteLine("  [3] Show files");
            Console.WriteLine("  [4] Edit source (Vim)");
            Console.WriteLine("  [0] Back");
            Console.Write("\n  Select: ");
            switch (ReadMenuKey())
            {
                case '1': Console.Clear(); PrintValidation(id); Pause(); break;
                case '2':
                    bool ok = TryExportProject(id, out string path, out string error);
                    Pause(ok ? $"Exported {path}" : error);
                    break;
                case '3':
                    Console.Clear();
                    if (TryGetProjectPath(id, out string root))
                        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                            Console.WriteLine($"  {Path.GetRelativePath(root, file)}");
                    Pause();
                    break;
                case '4':
                    if (project is not null && TryGetProjectPath(id, out string workspace) &&
                        PathSafety.TryResolveRelativePath(workspace, project.EntryFile, out string sourcePath))
                    {
                        VimEditor.EditHostFile(workspace, sourcePath);
                    }
                    break;
                case '0': open = false; break;
            }
        }
    }

    private static void PrintValidation(string id)
    {
        bool valid = TryValidateProject(id, out BuilderProjectManifest? manifest, out List<string> issues);
        Console.ForegroundColor = valid ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.WriteLine(valid ? $"VALID  {manifest!.Id} {manifest.Version}" : $"NOT READY  {id}");
        Screen_Print.ResetColors();
        foreach (string issue in issues) Console.WriteLine($"  - {issue}");
    }

    private static void PrintProjects()
    {
        DrawHeader("PACKAGE WORKSPACE", GetWorkspaceRoot());
        string[] projects = GetProjectDirectories();
        if (projects.Length == 0) { Console.WriteLine("  No packages yet. Run: builder new theme my-theme"); return; }
        Console.WriteLine($"  {"ID",-24} {"TYPE",-12} {"VERSION",-10} STATUS");
        foreach (string directory in projects)
        {
            string id = Path.GetFileName(directory);
            bool valid = TryValidateProject(id, out BuilderProjectManifest? project, out List<string> issues);
            Console.WriteLine($"  {id,-24} {project?.Template.ToString() ?? "Unknown",-12} {project?.Version ?? "-",-10} {(valid ? "ready" : $"{issues.Count} issue(s)")}");
        }
    }

    private static void DrawHeader(string title, string subtitle)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ============================================================");
        Console.WriteLine($"  {title}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {subtitle}");
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ============================================================");
        Screen_Print.ResetColors();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: builder [new <theme|game|program|extension> <id>|list|validate <id>|export <id>]");
    }

    private static bool TryGetProjectPath(string id, out string path)
    {
        path = string.Empty;
        id = NormalizeId(id);
        if (!IsValidId(id)) return false;
        string candidate = Path.Combine(GetWorkspaceRoot(), id);
        if (!Directory.Exists(candidate) || !PathSafety.IsInsideRoot(candidate, GetWorkspaceRoot())) return false;
        path = candidate;
        return true;
    }

    private static string[] GetProjectDirectories()
    {
        EnsureWorkspace();
        return Directory.GetDirectories(GetWorkspaceRoot())
            .Where(path => File.Exists(Path.Combine(path, "package.json")))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetWorkspaceRoot() => Path.Combine(
        string.IsNullOrWhiteSpace(Import.Variables.installPath) ? Environment.CurrentDirectory : Import.Variables.installPath,
        "apps",
        "PackageBuilder");

    private static string GetExportsRoot() => Path.Combine(GetWorkspaceRoot(), ".exports");
    private static void EnsureWorkspace() => Directory.CreateDirectory(GetWorkspaceRoot());
    private static string CategoryFor(PackageTemplate template) => template switch
    {
        PackageTemplate.Theme => "Themes",
        PackageTemplate.Game => "Games",
        PackageTemplate.Program => "Programs",
        _ => "Extensions"
    };

    private static string NormalizeId(string value) => string.Join('-', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static bool IsValidId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 80 && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');
    private static string ToDisplayName(string id) => string.Join(' ', id.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    private static bool TryParseTemplate(string value, out PackageTemplate template) => Enum.TryParse(value, true, out template);
    private static char ReadMenuKey() => char.ToLowerInvariant(Console.ReadKey(intercept: true).KeyChar);
    private static void Pause(string? message = null)
    {
        if (!string.IsNullOrWhiteSpace(message)) Console.WriteLine($"\n  {message}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n  Press ENTER to continue...");
        Screen_Print.ResetColors();
        Console.ReadLine();
    }
}
