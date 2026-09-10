using System.IO.Enumeration;

namespace SurfOS2;

internal partial class CLI_Engine
{
    private static void InspectFilesystem(string command, string[] args)
    {
        try
        {
            bool flag = false;
            List<string> paths = new();
            string pattern = "*";
            bool hasPattern = false;
            bool optionsEnded = false;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (!optionsEnded && arg == "--") { optionsEnded = true; continue; }
                if (!optionsEnded && ((command == "tree" && arg == "-a") ||
                    (command is "du" or "df" && arg == "-h"))) { flag = true; continue; }
                if (!optionsEnded && command == "find" && arg == "-name" && !hasPattern && i + 1 < args.Length)
                { pattern = args[++i]; hasPattern = true; continue; }
                if (!optionsEnded && arg.StartsWith('-')) { PrintCommandUsage(command); return; }
                paths.Add(arg);
            }
            if (paths.Count > 1 || (command is "find" or "stat" && paths.Count != 1) ||
                (command == "df" && paths.Count != 0))
            { PrintCommandUsage(command); return; }

            string path = paths.FirstOrDefault() ?? VirtualFileSystem.CurrentDirectory;
            string Size(long bytes) => flag ? FormatFileSize(bytes) : bytes.ToString();
            if (command == "df")
            {
                var (manifest, used) = VirtualFileSystem.InspectPartition();
                if (manifest is null) { ShellError("df: SurfOS partition capacity is unavailable (no partition manifest).", 69); return; }
                long available = Math.Max(0, manifest.CapacityBytes - manifest.SystemReservedBytes - used);
                Console.WriteLine("Filesystem  Mounted  Capacity  Used  Reserved  Available" + (flag ? "" : " (bytes)"));
                Console.WriteLine($"{manifest.FileSystem}  /  {Size(manifest.CapacityBytes)}  {Size(used)}  {Size(manifest.SystemReservedBytes)}  {Size(available)}");
                Console.WriteLine("All virtual directories share this SurfOS partition; available space excludes system reserves.");
                return;
            }
            VirtualPathInfo info = VirtualFileSystem.Inspect(path);
            if (command == "stat")
            {
                Console.WriteLine($"Path: {info.Path}\nType: {(info.IsDirectory ? "directory" : "file")}\nSize: {info.Size} bytes" +
                    (info.IsDirectory ? " (directory entry; use du for contents)" : ""));
                Console.WriteLine($"Created (UTC): {info.CreatedUtc:O}\nModified (UTC): {info.ModifiedUtc:O}\nAccessed (UTC): {info.AccessedUtc:O}\nHost attributes: {info.Attributes}");
                var metadata = VirtualFileSystem.GetMetadata(info.Path);
                Console.WriteLine($"Owner: {metadata.Owner}\nMode: {Convert.ToString(metadata.Mode, 8).PadLeft(3, '0')} ({VirtualFileSystem.FormatMode(info.IsDirectory, metadata.Mode)})");
                return;
            }
            if (command == "tree" && !info.IsDirectory)
            { ShellError($"tree: Not a directory: {info.Path}"); return; }
            long total = 0;
            foreach (var node in VirtualFileSystem.Walk(path, command != "tree" || flag))
            {
                if (command == "du") { total = checked(total + node.Info.Size); continue; }
                if (command == "find")
                {
                    string name = node.Info.Path == "/" ? "/" : node.Info.Path.Split('/')[^1];
                    if (FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true))
                        Console.WriteLine(node.Info.Path);
                }
                else Console.WriteLine(node.Branch + (node.Branch.Length == 0 ? node.Info.Path : node.Info.Path.Split('/')[^1]));
            }
            if (command == "du") Console.WriteLine($"{Size(total)}\t{info.Path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OverflowException)
        {
            ShellError($"{command}: {ex.Message}");
        }
    }
}
