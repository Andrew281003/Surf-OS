namespace SurfOS2;

internal sealed class VirtualDirectoryEntry
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public long Size { get; init; }
}

internal static class VirtualFileSystem
{
    private static readonly object SyncRoot = new();
    private static string _currentDirectory = "/";

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
                    new VirtualDirectoryEntry { Name = "themes", Type = "dir", Size = 0 },
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
                        Size = file.Length
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
                    Size = 0
                })
                .Concat(Directory.GetFiles(realPath)
                    .Select(file =>
                    {
                        FileInfo info = new(file);
                        return new VirtualDirectoryEntry
                        {
                            Name = info.Name,
                            Type = "file",
                            Size = info.Length
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
            string realPath = ResolveVirtualPath(NormalizeVirtualPath(path));
            Directory.CreateDirectory(realPath);
            return true;
        }
    }

    public static bool Touch(string path, out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string realPath = ResolveVirtualPath(NormalizeVirtualPath(path));
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

            if (!File.Exists(realPath))
            {
                error = $"File not found: {virtualPath}";
                return false;
            }

            content = File.ReadAllText(realPath);
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
            return true;
        }
    }

    public static bool RemoveDirectory(string path, out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string virtualPath = NormalizeVirtualPath(path);

            string[] protectedDirectories =
            [
                "/", "/home", "/system", "/apps", "/themes",
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

            if (Directory.EnumerateFileSystemEntries(realPath).Any())
            {
                error = $"Directory is not empty: {virtualPath}";
                return false;
            }

            Directory.Delete(realPath, recursive: false);
            return true;
        }
    }

    public static bool Copy(string source, string destination, out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string sourceVirtual = NormalizeVirtualPath(source);
            string sourceReal = ResolveVirtualPath(sourceVirtual);
            string destReal = ResolveDestinationPath(destination, sourceReal);

            if (File.Exists(sourceReal))
            {
                long existingSize = File.Exists(destReal) ? new FileInfo(destReal).Length : 0;
                long additionalBytes = Math.Max(0, new FileInfo(sourceReal).Length - existingSize);
                if (!Partition_Manager.CanAllocate(GetRoot(), additionalBytes, out error))
                {
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destReal) ?? GetRoot());
                File.Copy(sourceReal, destReal, overwrite: true);
                return true;
            }

            if (Directory.Exists(sourceReal))
            {
                long additionalBytes = GetDirectorySize(sourceReal);
                if (!Partition_Manager.CanAllocate(GetRoot(), additionalBytes, out error))
                {
                    return false;
                }

                CopyDirectory(sourceReal, destReal);
                return true;
            }

            error = $"Source not found: {sourceVirtual}";
            return false;
        }
    }

    public static bool Move(string source, string destination, out string error)
    {
        lock (SyncRoot)
        {
            error = string.Empty;
            string sourceVirtual = NormalizeVirtualPath(source);
            string sourceReal = ResolveVirtualPath(sourceVirtual);
            string destReal = ResolveDestinationPath(destination, sourceReal);

            if (File.Exists(sourceReal))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destReal) ?? GetRoot());
                if (File.Exists(destReal))
                {
                    File.Delete(destReal);
                }

                File.Move(sourceReal, destReal);
                return true;
            }

            if (Directory.Exists(sourceReal))
            {
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

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (string file in Directory.GetFiles(sourceDirectory))
        {
            File.Copy(
                file,
                Path.Combine(destinationDirectory, Path.GetFileName(file)),
                overwrite: true);
        }

        foreach (string directory in Directory.GetDirectories(sourceDirectory))
        {
            CopyDirectory(
                directory,
                Path.Combine(destinationDirectory, Path.GetFileName(directory)));
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
                "apps" => Path.Combine(new[] { "Projects" }.Concat(segments.Skip(1)).ToArray()),
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

    private static bool IsInsideRoot(string candidate, string root)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);

        return normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureInitialized()
    {
        EnsureMounts();
        if (_currentDirectory == "/")
        {
            _currentDirectory = GetHomePath();
        }
    }

    private static void EnsureMounts()
    {
        string root = GetRoot();
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "home", GetSafeUserName()));
        Directory.CreateDirectory(Path.Combine(root, "system"));
        Directory.CreateDirectory(Path.Combine(root, "Projects"));
        Directory.CreateDirectory(Path.Combine(root, "Packages"));
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
