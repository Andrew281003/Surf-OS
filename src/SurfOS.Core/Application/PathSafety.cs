namespace SurfOS2;

internal static class PathSafety
{
    public static bool TryResolveRelativePath(string root, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!IsInsideRoot(candidate, normalizedRoot))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    public static bool IsInsideRoot(string candidate, string root)
    {
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasLinkInPath(string candidate, string root)
    {
        if (!IsInsideRoot(candidate, root)) return true;
        string current = Path.GetFullPath(root);
        if (IsLink(current)) return true;
        string relative = Path.GetRelativePath(current, Path.GetFullPath(candidate));
        foreach (string part in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            current = Path.Combine(current, part);
            if (IsLink(current)) return true;
        }
        return false;
    }

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return true; }
    }

    public static bool IsSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." ||
            value.EndsWith(' ') || value.EndsWith('.') ||
            !value.Equals(Path.GetFileName(value), StringComparison.Ordinal) ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        string baseName = value.Split('.')[0];
        string[] reserved = ["CON", "PRN", "AUX", "NUL"];
        return !reserved.Contains(baseName, StringComparer.OrdinalIgnoreCase) &&
               !(baseName.Length == 4 &&
                 (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                  baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                 baseName[3] is >= '1' and <= '9');
    }
}
