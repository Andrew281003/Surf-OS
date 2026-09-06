namespace SurfOS2;

internal sealed class VirtualDirectoryEntry
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public long Size { get; init; }
    public DateTime Modified { get; init; }
}

internal static partial class VirtualFileSystem
{
    private static readonly object SyncRoot = new();
    private static string _currentDirectory = "/";
    private static bool _currentDirectoryInitialized;

    public static string CurrentDirectory
    {
        get
        {
            lock (SyncRoot)
            {
                EnsureInitialized();
                return _currentDirectory;
            }
        }
    }

    public static void InitializeForCurrentUser()
    {
        lock (SyncRoot)
        {
            EnsureMounts();
            _currentDirectory = GetHomePath();
            _currentDirectoryInitialized = true;
            Directory.CreateDirectory(ResolveVirtualPath(_currentDirectory));
        }
    }

    public static bool ChangeDirectory(string path, out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string virtualPath = NormalizeVirtualPath(path);
            string realPath = ResolveVirtualPath(virtualPath);

            if (!Directory.Exists(realPath))
            {
                error = $"Directory not found: {virtualPath}";
                return false;
            }

            _currentDirectory = virtualPath;
            return true;
        }
    }

    public static IReadOnlyList<VirtualDirectoryEntry> List(string path, out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string virtualPath = string.IsNullOrWhiteSpace(path)
                ? CurrentDirectory
                : NormalizeVirtualPath(path);
            string realPath = ResolveVirtualPath(virtualPath);

            if (virtualPath == "/")
            {
                return
                [
                    new VirtualDirectoryEntry { Name = "home", Type = "dir", Size = 0 },
                    new VirtualDirectoryEntry { Name = "system", Type = "dir", Size = 0 },
                    new VirtualDirectoryEntry { Name = "apps", Type = "dir", Size = 0 },
                    new VirtualDirectoryEntry { Name = "projects", Type = "dir", Size = 0 },
                    new VirtualDirectoryEntry { Name = "themes", Type = "dir", Size = 0 },
                    new VirtualDirectoryEntry { Name = "music", Type = "dir", Size = 0 },
                    new VirtualDirectoryEntry { Name = "logs", Type = "dir", Size = 0 },
                    new VirtualDirectoryEntry { Name = "temp", Type = "dir", Size = 0 },
                    new VirtualDirectoryEntry { Name = "mail", Type = "dir", Size = 0 }
                ];
            }

            if (File.Exists(realPath))
            {
                FileInfo file = new(realPath);
                return
                [
                    new VirtualDirectoryEntry
                    {
                        Name = Path.GetFileName(realPath),
                        Type = "file",
                        Size = file.Length,
                        Modified = file.LastWriteTime
                    }
                ];
            }

            if (!Directory.Exists(realPath))
            {
                error = $"Path not found: {virtualPath}";
                return [];
            }

            return Directory.GetDirectories(realPath)
                .Select(directory => new VirtualDirectoryEntry
                {
                    Name = Path.GetFileName(directory),
                    Type = "dir",
                    Size = 0,
                    Modified = Directory.GetLastWriteTime(directory)
                })
                .Concat(Directory.GetFiles(realPath)
                    .Select(file =>
                    {
                        FileInfo info = new(file);
                        return new VirtualDirectoryEntry
                        {
                            Name = info.Name,
                            Type = "file",
                            Size = info.Length,
                            Modified = info.LastWriteTime
                        };
                    }))
                .OrderBy(entry => entry.Type == "file")
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public static bool CreateDirectory(string path, out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string virtualPath = NormalizeVirtualPath(path);
            string realPath = ResolveVirtualPath(virtualPath);
            if (IsProtectedMutationPath(realPath))
            {
                error = "Protected SurfOS system data cannot be modified from the shell.";
                return false;
            }
            bool existed = Directory.Exists(realPath);
            Directory.CreateDirectory(realPath);
            if (!existed) RemoveMetadataCore(virtualPath, descendants: true);
            return true;
        }
    }

    public static bool Touch(string path, out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string virtualPath = NormalizeVirtualPath(path);
            string realPath = ResolveVirtualPath(virtualPath);
            if (IsProtectedMutationPath(realPath))
            {
                error = "Protected SurfOS system data cannot be modified from the shell.";
                return false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(realPath) ?? GetRoot());

            if (File.Exists(realPath))
            {
                File.SetLastWriteTime(realPath, DateTime.Now);
            }
            else
            {
                if (!Partition_Manager.CanAllocate(GetRoot(), 0, out error))
                {
                    return false;
                }

                File.WriteAllText(realPath, string.Empty);
                RemoveMetadataCore(virtualPath);
            }

            return true;
        }
    }

    public static bool ReadFile(string path, out string content, out string error)
    {
        lock (SyncRoot)
        {
            content = string.Empty;
            error = string.Empty;
            string virtualPath = NormalizeVirtualPath(path);
            string realPath = ResolveVirtualPath(virtualPath);

            if (IsSensitiveReadPath(realPath))
            {
                error = "Protected SurfOS account and system data cannot be read from the shell.";
                return false;
            }

            if (!File.Exists(realPath))
            {
                error = $"File not found: {virtualPath}";
                return false;
            }

            content = File.ReadAllText(realPath);
            return true;
        }
    }

    /// <summary>
    /// Resolves a file in the Package Builder workspace for its trusted editor.
    /// The shell remains unable to write other files under /apps.
    /// </summary>
    public static bool TryResolvePackageBuilderFile(
        string path,
        out string workspaceRoot,
        out string fullPath)
    {
        lock (SyncRoot)
        {
            workspaceRoot = Path.Combine(GetRoot(), "apps", "PackageBuilder");
            fullPath = string.Empty;
            string virtualPath = NormalizeVirtualPath(path);
            if (!virtualPath.StartsWith("/apps/PackageBuilder/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string candidate = ResolveVirtualPath(virtualPath);
            if (!PathSafety.IsInsideRoot(candidate, workspaceRoot) ||
                candidate.Equals(workspaceRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
    }

    public static bool WriteFile(string path, string content, bool append, out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string virtualPath = NormalizeVirtualPath(path);
            string realPath = ResolveVirtualPath(virtualPath);
            if (IsProtectedMutationPath(realPath))
            {
                error = "Protected SurfOS system data cannot be modified from the shell.";
                return false;
            }
            bool existed = File.Exists(realPath);
            long currentSize = existed ? new FileInfo(realPath).Length : 0;
            long newSize = System.Text.Encoding.UTF8.GetByteCount(content);
            long additionalBytes = append ? newSize : Math.Max(0, newSize - currentSize);

            if (!Partition_Manager.CanAllocate(GetRoot(), additionalBytes, out error))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(realPath) ?? GetRoot());
            if (append)
            {
                File.AppendAllText(realPath, content);
            }
            else
            {
                File.WriteAllText(realPath, content);
            }

            if (!existed) RemoveMetadataCore(virtualPath);

            return true;
        }
    }

    public static bool RemoveFile(string path, out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string virtualPath = NormalizeVirtualPath(path);
            string realPath = ResolveVirtualPath(virtualPath);

            if (IsProtectedMutationPath(realPath))
            {
                error = "Protected SurfOS system data cannot be modified from the shell.";
                return false;
            }

            if (Directory.Exists(realPath))
            {
                error = "rm only removes files in SurfOS. Use file paths, not directories.";
                return false;
            }

            if (!File.Exists(realPath))
            {
                error = $"File not found: {virtualPath}";
                return false;
            }

            File.Delete(realPath);
            RemoveMetadataCore(virtualPath);
            return true;
        }
    }

    public static bool RemoveDirectory(string path, out string error)
    {
        return RemoveDirectory(path, recursive: false, out error);
    }

    public static bool RemoveDirectory(string path, bool recursive, out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string virtualPath = NormalizeVirtualPath(path);

            string[] protectedDirectories =
            [
                "/", "/home", "/system", "/apps", "/projects", "/themes", "/music",
                "/logs", "/temp", "/mail", GetHomePath()
            ];
            if (protectedDirectories.Contains(virtualPath, StringComparer.OrdinalIgnoreCase))
            {
                error = $"Protected SurfOS directory cannot be removed: {virtualPath}";
                return false;
            }

            if (_currentDirectory.Equals(virtualPath, StringComparison.OrdinalIgnoreCase) ||
                _currentDirectory.StartsWith(
                    virtualPath.TrimEnd('/') + "/",
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "Cannot remove the current directory or one of its parent directories.";
                return false;
            }

            string realPath = ResolveVirtualPath(virtualPath);
            if (IsProtectedMutationPath(realPath))
            {
                error = "Protected SurfOS system data cannot be modified from the shell.";
                return false;
            }
            if (File.Exists(realPath))
            {
                error = "rmdir only removes directories. Use rm for files.";
                return false;
            }

            if (!Directory.Exists(realPath))
            {
                error = $"Directory not found: {virtualPath}";
                return false;
            }

            if (!recursive && Directory.EnumerateFileSystemEntries(realPath).Any())
            {
                error = $"Directory is not empty: {virtualPath}";
                return false;
            }

            Directory.Delete(realPath, recursive);
            RemoveMetadataCore(virtualPath, descendants: true);
            return true;
        }
    }

    public static bool Copy(
        string source,
        string destination,
        bool recursive,
        bool overwrite,
        out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string sourceVirtual = NormalizeVirtualPath(source);
            string sourceReal = ResolveVirtualPath(sourceVirtual);
            string destReal = ResolveDestinationPath(destination, sourceReal);

            if (IsSensitiveReadPath(sourceReal) || IsProtectedMutationPath(destReal))
            {
                error = "Protected SurfOS system data cannot be copied from or overwritten by the shell.";
                return false;
            }

            if (PathsEqual(sourceReal, destReal))
            {
                error = "Source and destination refer to the same path.";
                return false;
            }

            if (File.Exists(sourceReal))
            {
                if (File.Exists(destReal) && !overwrite)
                {
                    error = $"Destination already exists: {NormalizeVirtualPath(destination)} (use -f to overwrite)";
                    return false;
                }

                long existingSize = File.Exists(destReal) ? new FileInfo(destReal).Length : 0;
                long additionalBytes = Math.Max(0, new FileInfo(sourceReal).Length - existingSize);
                if (!Partition_Manager.CanAllocate(GetRoot(), additionalBytes, out error))
                {
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destReal) ?? GetRoot());
                File.Copy(sourceReal, destReal, overwrite);
                return true;
            }

            if (Directory.Exists(sourceReal))
            {
                if (!recursive)
                {
                    error = $"cp: omitting directory '{sourceVirtual}'; use -r to copy recursively";
                    return false;
                }

                if (IsSameOrDescendant(destReal, sourceReal))
                {
                    error = "Cannot copy a directory into itself or one of its descendants.";
                    return false;
                }

                if (Directory.Exists(destReal) && !overwrite)
                {
                    error = $"Destination directory already exists: {NormalizeVirtualPath(destination)} (use -f to merge)";
                    return false;
                }

                long additionalBytes = GetDirectorySize(sourceReal);
                if (!Partition_Manager.CanAllocate(GetRoot(), additionalBytes, out error))
                {
                    return false;
                }

                CopyDirectory(sourceReal, destReal, overwrite);
                return true;
            }

            error = $"Source not found: {sourceVirtual}";
            return false;
        }
    }

    public static bool Move(string source, string destination, bool overwrite, out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string sourceVirtual = NormalizeVirtualPath(source);
            string sourceReal = ResolveVirtualPath(sourceVirtual);
            string destReal = ResolveDestinationPath(destination, sourceReal);

            if (IsProtectedMutationPath(sourceReal) || IsProtectedMutationPath(destReal))
            {
                error = "Protected SurfOS system data cannot be moved from or overwritten by the shell.";
                return false;
            }

            if (IsProtectedDirectory(sourceVirtual))
            {
                error = $"Protected SurfOS directory cannot be moved: {sourceVirtual}";
                return false;
            }

            if (PathsEqual(sourceReal, destReal))
            {
                error = "Source and destination refer to the same path.";
                return false;
            }

            if (File.Exists(sourceReal))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destReal) ?? GetRoot());
                if (File.Exists(destReal))
                {
                    if (!overwrite)
                    {
                        error = $"Destination already exists: {NormalizeVirtualPath(destination)} (use -f to overwrite)";
                        return false;
                    }

                    File.Delete(destReal);
                }

                File.Move(sourceReal, destReal);
                return true;
            }

            if (Directory.Exists(sourceReal))
            {
                if (IsSameOrDescendant(destReal, sourceReal))
                {
                    error = "Cannot move a directory into itself or one of its descendants.";
                    return false;
                }

                if (Directory.Exists(destReal))
                {
                    error = "Destination directory already exists.";
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destReal) ?? GetRoot());
                Directory.Move(sourceReal, destReal);
                return true;
            }

            error = $"Source not found: {sourceVirtual}";
            return false;
        }
    }

    private static string ResolveDestinationPath(string destination, string sourceRealPath)
    {
        string destinationVirtual = NormalizeVirtualPath(destination);
        string destinationReal = ResolveVirtualPath(destinationVirtual);

        if (Directory.Exists(destinationReal))
        {
            return Path.Combine(destinationReal, Path.GetFileName(sourceRealPath));
        }

        return destinationReal;
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string destinationDirectory,
        bool overwrite)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (string file in Directory.GetFiles(sourceDirectory))
        {
            File.Copy(
                file,
                Path.Combine(destinationDirectory, Path.GetFileName(file)),
                overwrite);
        }

        foreach (string directory in Directory.GetDirectories(sourceDirectory))
        {
            CopyDirectory(
                directory,
                Path.Combine(destinationDirectory, Path.GetFileName(directory)),
                overwrite);
        }
    }

    private static long GetDirectorySize(string directory)
    {
        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
    }

    private static string NormalizeVirtualPath(string path)
    {
        EnsureInitialized();

        string workingPath = string.IsNullOrWhiteSpace(path)
            ? _currentDirectory
            : path.Trim().Replace('\\', '/');

        if (workingPath == "~")
        {
            workingPath = GetHomePath();
        }
        else if (workingPath.StartsWith("~/", StringComparison.Ordinal))
        {
            workingPath = $"{GetHomePath()}/{workingPath[2..]}";
        }
        else if (!workingPath.StartsWith("/", StringComparison.Ordinal))
        {
            workingPath = $"{_currentDirectory.TrimEnd('/')}/{workingPath}";
        }

        Stack<string> segments = new();
        foreach (string segment in workingPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.Pop();
                }

                continue;
            }

            segments.Push(segment);
        }

        string normalized = "/" + string.Join("/", segments.Reverse());
        return normalized == "/" ? "/" : normalized.TrimEnd('/');
    }

    private static string ResolveVirtualPath(string virtualPath)
    {
        string root = GetRoot();
        string[] segments = virtualPath
            .TrimStart('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        string relative = segments.Length == 0
            ? string.Empty
            : segments[0].ToLowerInvariant() switch
            {
                "apps" => Path.Combine(new[] { "apps" }.Concat(segments.Skip(1)).ToArray()),
                "projects" => Path.Combine(new[] { "Projects" }.Concat(segments.Skip(1)).ToArray()),
                "themes" => Path.Combine(new[] { "Packages" }.Concat(segments.Skip(1)).ToArray()),
                _ => Path.Combine(segments)
            };

        string candidate = Path.GetFullPath(Path.Combine(root, relative));

        if (!IsInsideRoot(candidate, root))
        {
            throw new InvalidOperationException("Access outside the SurfOS install folder is blocked.");
        }

        return candidate;
    }

    private static bool IsInsideRoot(string candidate, string root) =>
        PathSafety.IsInsideRoot(candidate, root);

    private static bool PathsEqual(string first, string second) =>
        Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrDescendant(string candidate, string directory) =>
        PathSafety.IsInsideRoot(candidate, directory);

    private static bool IsProtectedDirectory(string virtualPath)
    {
        string[] protectedDirectories =
        [
            "/", "/home", "/system", "/apps", "/projects", "/themes", "/music",
            "/logs", "/temp", "/mail", GetHomePath()
        ];
        return protectedDirectories.Contains(virtualPath, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSensitiveReadPath(string realPath)
    {
        string root = GetRoot();
        string[] sensitiveFiles =
        [
            Path.Combine(root, "database.json"),
            Path.Combine(root, "options.json"),
            Path.Combine(root, "partition.json")
        ];
        return sensitiveFiles.Any(path => PathsEqual(path, realPath)) ||
               PathsEqual(root, realPath) ||
               PathSafety.IsInsideRoot(realPath, Path.Combine(root, "preVersions"));
    }

    private static bool IsProtectedMutationPath(string realPath)
    {
        string root = GetRoot();
        string[] protectedFiles =
        [
            Path.Combine(root, "database.json"),
            Path.Combine(root, "options.json"),
            Path.Combine(root, "partition.json"),
            Path.Combine(root, "installer_feedback.json")
        ];
        return protectedFiles.Any(path => PathsEqual(path, realPath)) ||
               PathsEqual(root, realPath) ||
               PathSafety.IsInsideRoot(realPath, Path.Combine(root, "apps")) ||
               PathSafety.IsInsideRoot(realPath, Path.Combine(root, "system")) ||
               PathSafety.IsInsideRoot(realPath, Path.Combine(root, "preVersions"));
    }

    private static void EnsureInitialized()
    {
        EnsureMounts();
        if (!_currentDirectoryInitialized)
        {
            _currentDirectory = GetHomePath();
            _currentDirectoryInitialized = true;
        }
    }

    private static void EnsureMounts()
    {
        string root = GetRoot();
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "home", GetSafeUserName()));
        Directory.CreateDirectory(Path.Combine(root, "system"));
        Directory.CreateDirectory(Path.Combine(root, "Projects"));
        Directory.CreateDirectory(Path.Combine(root, "apps"));
        Directory.CreateDirectory(Path.Combine(root, "Packages"));
        Directory.CreateDirectory(Path.Combine(root, "music"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        Directory.CreateDirectory(Path.Combine(root, "temp"));
        Directory.CreateDirectory(Path.Combine(root, "mail"));
    }

    private static string GetRoot()
    {
        return string.IsNullOrWhiteSpace(Import.Variables.installPath)
            ? Environment.CurrentDirectory
            : Import.Variables.installPath;
    }

    private static string GetHomePath()
    {
        return $"/home/{GetSafeUserName()}";
    }

    private static string GetSafeUserName()
    {
        string username = string.IsNullOrWhiteSpace(Import.Variables.userName)
            ? "Guest"
            : Import.Variables.userName;

        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(username
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray());
    }
}
