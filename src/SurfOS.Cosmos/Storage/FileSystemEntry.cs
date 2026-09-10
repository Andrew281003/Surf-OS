namespace SurfOS.Storage
{
    public sealed class FileSystemEntry
    {
        public FileSystemEntry(string name, bool isDirectory, long size)
        {
            Name = name;
            IsDirectory = isDirectory;
            Size = size;
        }

        public string Name { get; private set; }
        public bool IsDirectory { get; private set; }
        public long Size { get; private set; }
    }
}
