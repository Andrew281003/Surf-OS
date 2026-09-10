using System.Collections.Generic;

namespace SurfOS.Storage
{
    public interface IFileSystemService
    {
        string DriveRoot { get; }
        string CurrentDirectory { get; }
        string LastError { get; }
        bool HasMountedVolume { get; }
        bool Initialize();
        int GetPhysicalDiskCount();
        long GetPhysicalDiskSizeMegabytes(int diskIndex);
        void PreparePhysicalDisk(int diskIndex);
        void EnsureSystemLayout();
        string ResolvePath(string path);
        string ToDisplayPath(string physicalPath);
        bool FileExists(string path);
        bool DirectoryExists(string path);
        void SetCurrentDirectory(string path);
        List<FileSystemEntry> List(string path);
        void CreateDirectory(string path);
        void CreateFile(string path);
        string ReadAllText(string path);
        void WriteAllText(string path, string content);
        void AppendAllText(string path, string content);
        void Copy(string source, string destination, bool overwrite);
        void Move(string source, string destination, bool overwrite);
        void Delete(string path, bool recursive);
        bool IsProtectedPath(string path);
    }
}
