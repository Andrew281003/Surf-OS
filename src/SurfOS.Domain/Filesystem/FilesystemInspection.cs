namespace SurfOS2;

internal sealed record VirtualPathInfo(
    string Path, bool IsDirectory, long Size, DateTime CreatedUtc,
    DateTime ModifiedUtc, DateTime AccessedUtc, FileAttributes Attributes);

internal static partial class VirtualFileSystem
{
    public static VirtualPathInfo Inspect(string path)
    {
        lock (SyncRoot)
        {
            string virtualPath = NormalizeVirtualPath(path);
            string realPath = ResolveVirtualPath(virtualPath);
            // Check each component, including explicitly supplied paths through a junction.
            string root = System.IO.Path.GetFullPath(GetRoot());
            string current = realPath;
            while (true)
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Links and junctions are not followed: {virtualPath}");
                if (PathsEqual(current, root)) break;
                current = System.IO.Path.GetDirectoryName(current)
                    ?? throw new IOException("Invalid virtual path.");
            }

            FileAttributes attributes = File.GetAttributes(realPath);
            bool directory = attributes.HasFlag(FileAttributes.Directory);
            return new(virtualPath, directory, directory ? 0 : new FileInfo(realPath).Length,
                File.GetCreationTimeUtc(realPath), File.GetLastWriteTimeUtc(realPath),
                File.GetLastAccessTimeUtc(realPath), attributes);
        }
    }

    public static IEnumerable<(VirtualPathInfo Info, string Branch)> Walk(string path, bool includeHidden)
    {
        VirtualPathInfo start = Inspect(path);
        Stack<(VirtualPathInfo Info, string Prefix, bool Last, bool Root)> pending = new();
        pending.Push((start, "", true, true));
        while (pending.TryPop(out var node))
        {
            yield return (node.Info, node.Root ? "" : node.Prefix + (node.Last ? "`-- " : "|-- "));
            if (!node.Info.IsDirectory) continue;
            var children = List(node.Info.Path, out string error);
            if (error.Length > 0) throw new IOException(error);
            List<VirtualPathInfo> visible = new();
            foreach (var child in children)
            {
                if (!includeHidden && child.Name.StartsWith('.')) continue;
                string childPath = node.Info.Path.TrimEnd('/') + "/" + child.Name;
                // A link is reported as an error, so totals never silently appear complete.
                VirtualPathInfo info = Inspect(childPath);
                if (!includeHidden && info.Attributes.HasFlag(FileAttributes.Hidden)) continue;
                visible.Add(info);
            }
            string prefix = node.Root ? "" : node.Prefix + (node.Last ? "    " : "|   ");
            for (int i = visible.Count - 1; i >= 0; i--)
                pending.Push((visible[i], prefix, i == visible.Count - 1, false));
        }
    }

    public static (PartitionManifest? Manifest, long Used) InspectPartition()
    {
        string root = GetRoot();
        PartitionManifest? manifest = Partition_Manager.Load(root);
        return (manifest, manifest is null ? 0 : Partition_Manager.GetUsedBytes(root));
    }
}
