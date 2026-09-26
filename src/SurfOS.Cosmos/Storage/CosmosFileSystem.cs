using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.Kernel.HAL.Interfaces.Devices;
using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System.Filesystems.Fat;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Vfs;

namespace SurfOS.Storage
{
    public sealed class CosmosFileSystem : IFileSystemService
    {
        private const string MountPoint = "/mnt";
        private const long BytesPerMegabyte = 1048576;
        private const int MaximumTextFileSize = 65536;
        private bool _initialized;

        public string DriveRoot { get; private set; } = string.Empty;
        public string CurrentDirectory { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;
        public bool HasMountedVolume { get { return DriveRoot.Length > 0; } }

        public bool Initialize()
        {
            if (_initialized) return true;
            try
            {
                if (!VfsManager.RegisterFilesystem("fat", new FatFilesystemType()))
                    throw new InvalidOperationException("Could not register the FAT filesystem driver.");
                for (int index = 0; index < StorageManager.Partitions.Count; index++)
                {
                    if (TryMount(StorageManager.Partitions[index])) break;
                }
                _initialized = true;
                return true;
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                return false;
            }
        }

        private bool TryMount(Partition partition)
        {
            VfsManager.VfsMount mount;
            if (!VfsManager.TryMount("fat", partition, MountFlags.None, MountPoint, out mount)) return false;
            DriveRoot = MountPoint + "/";
            CurrentDirectory = DriveRoot;
            LastError = string.Empty;
            return true;
        }

        public int GetPhysicalDiskCount() { return StorageManager.DeviceCount; }

        public long GetPhysicalDiskSizeMegabytes(int diskIndex)
        {
            IBlockDevice disk = GetDisk(diskIndex);
            return (long)(disk.BlockCount * disk.BlockSize / BytesPerMegabyte);
        }

        private static IBlockDevice GetDisk(int diskIndex)
        {
            if (diskIndex < 0 || diskIndex >= StorageManager.DeviceCount)
                throw new InvalidOperationException("Disk index is out of range.");
            return StorageManager.GetDevice(diskIndex);
        }

        public void PreparePhysicalDisk(int diskIndex)
        {
            IBlockDevice disk = GetDisk(diskIndex);
            if (disk.BlockSize != 512 || disk.BlockCount <= 2048 + 65536)
                throw new InvalidOperationException("SurfOS requires a disk larger than 32 MB with 512-byte sectors.");
            ulong usableSectors = disk.BlockCount - 2048;
            if (usableSectors * disk.BlockSize / BytesPerMegabyte > 131071)
                throw new InvalidOperationException("SurfOS supports installation disks up to 128 GB (MBR/FAT32).");
            for (int i = 0; i < VfsManager.Mounts.Count; i++)
            {
                Partition mounted = VfsManager.Mounts[i].Partition;
                if (mounted != null && mounted.Host == disk)
                    throw new InvalidOperationException("A mounted disk cannot be erased by first-boot setup.");
            }

            // StorageInstaller obtains an explicit ERASE confirmation before this call.
            Mbr.Create(disk);
            if (!PartitionManager.Create(disk, 2048, usableSectors, 0x0C, Gpt.BasicDataPartitionType))
                throw new InvalidOperationException("Could not create the SurfOS partition.");
            StorageManager.RescanPartitions(disk);
            for (int i = 0; i < StorageManager.Partitions.Count; i++)
            {
                Partition partition = StorageManager.Partitions[i];
                if (partition.Host != disk) continue;
                FatFormatOptions options = new FatFormatOptions { Type = FatType.Fat32, VolumeLabel = "SURFOS     " };
                if (!VfsManager.TryFormat("fat", partition, options))
                    throw new InvalidOperationException("Could not format the SurfOS partition as FAT32.");
                if (!TryMount(partition))
                    throw new InvalidOperationException("The formatted partition did not mount.");
                return;
            }
            throw new InvalidOperationException("The new partition was not detected.");
        }

        public void EnsureSystemLayout()
        {
            string[] directories = { "/System", "/Users", "/Apps", "/Packages", "/Config", "/Logs", "/Temp" };
            for (int i = 0; i < directories.Length; i++)
            {
                string path = ResolvePath(directories[i]);
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            }
        }

        public string ResolvePath(string path)
        {
            if (!HasMountedVolume) throw new InvalidOperationException("No SurfOS volume is mounted.");
            if (string.IsNullOrWhiteSpace(path)) return CurrentDirectory;
            string value = path.Trim().Replace('\\', '/');
            string combined = value.StartsWith("/") ? value : ToDisplayPath(CurrentDirectory).TrimEnd('/') + "/" + value;
            string[] parts = combined.Split('/');
            List<string> clean = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0 || part == ".") continue;
                if (part == "..")
                {
                    if (clean.Count > 0) clean.RemoveAt(clean.Count - 1);
                    continue;
                }
                if (part.IndexOf(':') >= 0) throw new InvalidOperationException("Invalid path component: " + part);
                clean.Add(part);
            }
            string result = MountPoint;
            for (int i = 0; i < clean.Count; i++) result += "/" + clean[i];
            return clean.Count == 0 ? DriveRoot : result;
        }

        public string ToDisplayPath(string physicalPath)
        {
            if (string.IsNullOrEmpty(physicalPath)) return "/";
            string value = physicalPath.Replace('\\', '/');
            if (value == MountPoint || value == DriveRoot) return "/";
            if (value.StartsWith(DriveRoot, StringComparison.Ordinal)) return "/" + value.Substring(DriveRoot.Length);
            return value.StartsWith("/") ? value : "/" + value;
        }

        public bool FileExists(string path) { return File.Exists(ResolvePath(path)); }
        public bool DirectoryExists(string path) { return Directory.Exists(ResolvePath(path)); }

        public void SetCurrentDirectory(string path)
        {
            string resolved = ResolvePath(path);
            if (!Directory.Exists(resolved)) throw new InvalidOperationException("Directory not found: " + ToDisplayPath(resolved));
            CurrentDirectory = resolved;
        }

        public List<FileSystemEntry> List(string path)
        {
            string resolved = ResolvePath(path);
            if (!Directory.Exists(resolved)) throw new InvalidOperationException("Directory not found: " + ToDisplayPath(resolved));
            List<FileSystemEntry> result = new List<FileSystemEntry>();
            string[] directories = Directory.GetDirectories(resolved);
            for (int i = 0; i < directories.Length; i++) result.Add(new FileSystemEntry(Path.GetFileName(directories[i]), true, 0));
            string[] files = Directory.GetFiles(resolved);
            for (int i = 0; i < files.Length; i++) result.Add(new FileSystemEntry(Path.GetFileName(files[i]), false, new FileInfo(files[i]).Length));
            return result;
        }

        public void CreateDirectory(string path) { Directory.CreateDirectory(ResolvePath(path)); }
        public void CreateFile(string path)
        {
            string resolved = ResolvePath(path);
            if (!File.Exists(resolved)) using (File.Create(resolved)) { }
        }

        public string ReadAllText(string path)
        {
            using (Stream stream = File.OpenRead(ResolvePath(path)))
            {
                if (stream.Length > MaximumTextFileSize) throw new InvalidOperationException("Text file exceeds 64 KiB.");
                byte[] bytes = new byte[(int)stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int count = stream.Read(bytes, read, bytes.Length - read);
                    if (count <= 0) break;
                    read += count;
                }
                char[] characters = new char[read];
                for (int i = 0; i < read; i++) characters[i] = bytes[i] <= 127 ? (char)bytes[i] : '?';
                return new string(characters);
            }
        }

        public void WriteAllText(string path, string content)
        {
            using (Stream stream = File.Create(ResolvePath(path))) WriteAscii(stream, content);
        }

        public void AppendAllText(string path, string content)
        {
            using (Stream stream = new FileStream(ResolvePath(path), FileMode.Append, FileAccess.Write)) WriteAscii(stream, content);
        }

        public void Copy(string source, string destination, bool overwrite)
        { File.Copy(ResolvePath(source), ResolvePath(destination), overwrite); }

        public void Move(string source, string destination, bool overwrite)
        {
            string from = ResolvePath(source), to = ResolvePath(destination);
            if (File.Exists(from)) { File.Move(from, to, overwrite); return; }
            if (Directory.Exists(from))
            {
                if (Directory.Exists(to)) throw new InvalidOperationException("Destination directory already exists.");
                Directory.Move(from, to);
                return;
            }
            throw new InvalidOperationException("Source was not found.");
        }

        public void Delete(string path, bool recursive)
        {
            string resolved = ResolvePath(path);
            if (File.Exists(resolved)) { File.Delete(resolved); return; }
            if (Directory.Exists(resolved)) { Directory.Delete(resolved, recursive); return; }
            throw new InvalidOperationException("Path was not found.");
        }

        public bool IsProtectedPath(string path)
        {
            string display = ToDisplayPath(ResolvePath(path)).ToLowerInvariant();
            return display == "/system" || display.StartsWith("/system/") ||
                   display == "/config" || display.StartsWith("/config/") ||
                   display == "/logs" || display.StartsWith("/logs/") ||
                   display == "/users/users.db";
        }

        private static void WriteAscii(Stream stream, string content)
        {
            if (content.Length > MaximumTextFileSize) throw new InvalidOperationException("Text file exceeds 64 KiB.");
            byte[] bytes = new byte[content.Length];
            for (int i = 0; i < content.Length; i++) bytes[i] = content[i] <= 127 ? (byte)content[i] : (byte)'?';
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
