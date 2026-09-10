namespace SurfOS2;

internal static partial class VirtualFileSystem
{
    public static void ValidateWriteDestination(string path)
    {
        lock (SyncRoot)
        {
            string real = ResolveVirtualPath(NormalizeVirtualPath(path));
            if (IsProtectedMutationPath(real)) throw new UnauthorizedAccessException("Protected SurfOS system data cannot be modified from the shell.");
            if (Directory.Exists(real)) throw new IOException("Destination is a directory.");
            string current = real;
            while (!File.Exists(current) && !Directory.Exists(current)) current = Path.GetDirectoryName(current) ?? throw new IOException("Invalid destination.");
            if (File.Exists(current) && !PathsEqual(current, real)) throw new IOException("Destination parent is a file.");
            string root = Path.GetFullPath(GetRoot());
            while (true)
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Links and junctions are not followed.");
                if (PathsEqual(current, root)) break;
                current = Path.GetDirectoryName(current) ?? throw new IOException("Invalid destination.");
            }
        }
    }

    public static byte[] ReadBytes(string path, long maximumBytes)
    {
        lock (SyncRoot)
        {
            VirtualPathInfo info = Inspect(path);
            string real = ResolveVirtualPath(info.Path);
            if (IsSensitiveReadPath(real)) throw new UnauthorizedAccessException("Protected SurfOS data cannot be read from the shell.");
            if (info.IsDirectory) throw new IOException("Expected a file.");
            if (info.Size > maximumBytes) throw new IOException($"File exceeds {maximumBytes} bytes.");
            using FileStream stream = File.OpenRead(real);
            using MemoryStream result = new();
            byte[] buffer = new byte[8192]; int read;
            while ((read = stream.Read(buffer)) > 0)
            {
                if (result.Length + read > maximumBytes) throw new IOException("File size limit exceeded.");
                result.Write(buffer, 0, read);
            }
            return result.ToArray();
        }
    }

    public static void WriteBytes(string path, byte[] content)
    {
        lock (SyncRoot)
        {
            ValidateWriteDestination(path);
            string virtualPath = NormalizeVirtualPath(path);
            string real = ResolveVirtualPath(virtualPath);
            bool existed = File.Exists(real);
            long oldSize = existed ? new FileInfo(real).Length : 0;
            if (!Partition_Manager.CanAllocate(GetRoot(), Math.Max(0, content.LongLength - oldSize), out string error)) throw new IOException(error);
            Directory.CreateDirectory(Path.GetDirectoryName(real)!);
            string temporary = real + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, content); File.Move(temporary, real, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            if (!existed) RemoveMetadataCore(virtualPath);
        }
    }
}
