using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.System.FileSystem;
using Cosmos.System.FileSystem.Listing;
using Cosmos.System.FileSystem.VFS;

namespace SurfOS.Storage
{
    public sealed class CosmosFileSystem : IFileSystemService
    {
        private const long BytesPerMegabyte = 1048576;
        private const long MbrFirstPartitionOffsetBytes = 63 * 512;
        private const long MaximumFat32SizeMegabytes = 131071;
        private const int MaximumTextFileSize = 65536;
        private bool _initialized;
        private CosmosVFS _vfs;

        public string DriveRoot { get; private set; }
        public string CurrentDirectory { get; private set; }
        public string LastError { get; private set; }
        public bool HasMountedVolume { get { return DriveRoot != null && DriveRoot.Length > 0; } }

        public bool Initialize()
        {
            if (_initialized)
            {
                return true;
            }

            try
            {
                _vfs = new CosmosVFS();
                VFSManager.RegisterVFS(_vfs, false, false);
                List<string> drives = VFSManager.GetLogicalDrives();
                if (drives == null || drives.Count == 0)
                {
                    DriveRoot = string.Empty;
                    CurrentDirectory = string.Empty;
                    LastError = string.Empty;
                    _initialized = true;
                    return true;
                }

                DriveRoot = NormalizeDriveRoot(drives[0]);
                CurrentDirectory = DriveRoot;
                LastError = string.Empty;
                _initialized = true;
                return true;
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                return false;
            }
        }

        public int GetPhysicalDiskCount()
        {
            return _vfs == null ? 0 : _vfs.GetDisks().Count;
        }

        public long GetPhysicalDiskSizeMegabytes(int diskIndex)
        {
            List<Disk> disks = _vfs.GetDisks();
            if (diskIndex < 0 || diskIndex >= disks.Count) { throw new InvalidOperationException("Disk index is out of range."); }
            return disks[diskIndex].Size / BytesPerMegabyte;
        }

        public void PreparePhysicalDisk(int diskIndex)
        {
            List<Disk> disks = _vfs.GetDisks();
            if (diskIndex < 0 || diskIndex >= disks.Count) { throw new InvalidOperationException("Disk index is out of range."); }
            Disk disk = disks[diskIndex];
            long usableSizeMegabytes = (disk.Size - MbrFirstPartitionOffsetBytes) / BytesPerMegabyte;
            if (usableSizeMegabytes < 32)
            {
                throw new InvalidOperationException("The initial Cosmos FAT disk must have at least 32 MB of usable space.");
            }
            if (usableSizeMegabytes > MaximumFat32SizeMegabytes)
            {
                throw new InvalidOperationException("This Cosmos release supports installation disks up to 128 GB (MBR/FAT32).");
            }
            int sizeMegabytes = (int)usableSizeMegabytes;
            for (int index = 0; index < disk.Partitions.Count; index++)
            {
                if (disk.Partitions[index].MountedFS != null)
                {
                    throw new InvalidOperationException("A mounted disk cannot be erased by first-boot setup.");
                }
            }
            while (disk.Partitions.Count > 0) { disk.DeletePartition(0); }
            disk.CreatePartition(sizeMegabytes);
            FormatFat32(disk.Partitions[0]);
            disk.MountPartition(0);
            List<string> drives = VFSManager.GetLogicalDrives();
            if (drives == null || drives.Count == 0)
            {
                throw new InvalidOperationException("The formatted partition did not mount. Reboot and try again.");
            }
            DriveRoot = NormalizeDriveRoot(drives[drives.Count - 1]);
            CurrentDirectory = DriveRoot;
        }

        private static void FormatFat32(ManagedPartition partition)
        {
            var device = partition.Host;
            if (device.BlockSize != 512)
            {
                throw new InvalidOperationException("SurfOS FAT32 formatting currently requires 512-byte disk sectors.");
            }
            if (device.BlockCount > uint.MaxValue)
            {
                throw new InvalidOperationException("The selected partition is too large for MBR/FAT32.");
            }

            const uint bytesPerSector = 512;
            const uint sectorsPerCluster = 1;
            const uint reservedSectors = 32;
            const uint fatCount = 2;
            uint totalSectors = (uint)device.BlockCount;
            ulong numerator = totalSectors - reservedSectors + (2 * sectorsPerCluster);
            ulong denominator = (sectorsPerCluster * bytesPerSector / 4) + fatCount;
            uint fatSectors = (uint)(numerator / denominator + 1);

            byte[] boot = new byte[bytesPerSector];
            boot[0] = 0xEB;
            boot[1] = 0x58;
            boot[2] = 0x90;
            WriteAscii(boot, 3, "SURFOS  ");
            WriteUInt16(boot, 11, (ushort)bytesPerSector);
            boot[13] = (byte)sectorsPerCluster;
            WriteUInt16(boot, 14, (ushort)reservedSectors);
            boot[16] = (byte)fatCount;
            boot[21] = 0xF8;
            WriteUInt32(boot, 32, totalSectors);
            WriteUInt32(boot, 36, fatSectors);
            WriteUInt32(boot, 44, 2);
            WriteUInt16(boot, 48, 1);
            WriteUInt16(boot, 50, 6);
            boot[64] = 0x80;
            boot[66] = 0x29;
            WriteUInt32(boot, 67, 0x53465231);
            WriteAscii(boot, 71, "SURFOS     ");
            WriteAscii(boot, 82, "FAT32   ");
            boot[510] = 0x55;
            boot[511] = 0xAA;

            byte[] info = new byte[bytesPerSector];
            WriteUInt32(info, 0, 0x41615252);
            WriteUInt32(info, 484, 0x61417272);
            WriteUInt32(info, 488, uint.MaxValue);
            WriteUInt32(info, 492, uint.MaxValue);
            WriteUInt32(info, 508, 0xAA550000);

            byte[] firstFatSector = new byte[bytesPerSector];
            WriteUInt32(firstFatSector, 0, 0x0FFFFFF8);
            WriteUInt32(firstFatSector, 4, 0x0FFFFFFF);
            WriteUInt32(firstFatSector, 8, 0x0FFFFFFF);

            ClearSectors(device, reservedSectors, fatSectors * fatCount + sectorsPerCluster);
            device.WriteBlock(0, 1, ref boot);
            device.WriteBlock(6, 1, ref boot);
            device.WriteBlock(1, 1, ref info);
            device.WriteBlock(7, 1, ref info);
            for (uint fat = 0; fat < fatCount; fat++)
            {
                ulong start = reservedSectors + (fat * fatSectors);
                device.WriteBlock(start, 1, ref firstFatSector);
            }
        }

        private static void ClearSectors(Cosmos.HAL.BlockDevice.BlockDevice device, uint startSector, uint sectorCount)
        {
            const uint sectorsPerWrite = 128;
            byte[] zeros = new byte[sectorsPerWrite * 512];
            uint cleared = 0;
            while (cleared < sectorCount)
            {
                uint count = sectorCount - cleared;
                if (count > sectorsPerWrite) { count = sectorsPerWrite; }
                if (count == sectorsPerWrite)
                {
                    device.WriteBlock(startSector + cleared, count, ref zeros);
                }
                else
                {
                    byte[] remainder = new byte[count * 512];
                    device.WriteBlock(startSector + cleared, count, ref remainder);
                }
                cleared += count;
            }
        }

        private static void WriteUInt16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteAscii(byte[] data, int offset, string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                data[offset + index] = (byte)value[index];
            }
        }

        public void EnsureSystemLayout()
        {
            string[] directories =
            {
                "/System", "/Users", "/Apps", "/Packages", "/Config", "/Logs", "/Temp"
            };
            for (int index = 0; index < directories.Length; index++)
            {
                string path = ResolvePath(directories[index]);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }
        }

        public string ResolvePath(string path)
        {
            if (path == null || path.Trim().Length == 0)
            {
                return CurrentDirectory;
            }

            string value = path.Trim().Replace('/', '\\');
            string combined;
            int inputColon = value.IndexOf(':');
            if (inputColon > 0 && inputColon + 1 < value.Length && value[inputColon + 1] == '\\')
            {
                combined = value;
            }
            else if (value[0] == '\\')
            {
                combined = DriveRoot + value.Substring(1);
            }
            else
            {
                combined = CurrentDirectory + (CurrentDirectory.EndsWith("\\") ? string.Empty : "\\") + value;
            }

            string root = NormalizeDriveRoot(DriveRoot);
            int combinedColon = combined.IndexOf(':');
            if (combinedColon < 1 || combinedColon + 1 >= combined.Length || combined[combinedColon + 1] != '\\')
            {
                throw new InvalidOperationException("Path does not contain a valid Cosmos drive root.");
            }
            string requestedRoot = NormalizeDriveRoot(combined.Substring(0, combinedColon + 1));
            if (requestedRoot != root)
            {
                throw new InvalidOperationException("Path root " + requestedRoot + " is outside mounted root " + root + ".");
            }
            string remainder = combined.Substring(combinedColon + 2);
            string[] parts = remainder.Split('\\');
            List<string> clean = new List<string>();
            for (int index = 0; index < parts.Length; index++)
            {
                string part = parts[index];
                if (part.Length == 0 || part == ".")
                {
                    continue;
                }
                if (part == "..")
                {
                    if (clean.Count > 0)
                    {
                        clean.RemoveAt(clean.Count - 1);
                    }
                    continue;
                }
                if (part.IndexOf(':') >= 0)
                {
                    throw new InvalidOperationException("Invalid path component: " + part);
                }
                clean.Add(part);
            }

            string result = root;
            for (int index = 0; index < clean.Count; index++)
            {
                result += clean[index];
                if (index < clean.Count - 1)
                {
                    result += "\\";
                }
            }
            return result;
        }

        public string ToDisplayPath(string physicalPath)
        {
            string resolved = ResolvePath(physicalPath);
            if (resolved.Length <= DriveRoot.Length)
            {
                return "/";
            }
            return "/" + resolved.Substring(DriveRoot.Length).Replace('\\', '/');
        }

        public bool FileExists(string path) { return File.Exists(ResolvePath(path)); }
        public bool DirectoryExists(string path) { return Directory.Exists(ResolvePath(path)); }

        public void SetCurrentDirectory(string path)
        {
            string resolved = ResolvePath(path);
            if (!Directory.Exists(resolved))
            {
                throw new InvalidOperationException("Directory not found: " + ToDisplayPath(resolved));
            }
            CurrentDirectory = resolved;
        }

        public List<FileSystemEntry> List(string path)
        {
            string resolved = ResolvePath(path);
            if (!Directory.Exists(resolved))
            {
                throw new InvalidOperationException("Directory not found: " + ToDisplayPath(resolved));
            }

            List<FileSystemEntry> result = new List<FileSystemEntry>();
            string[] directories = Directory.GetDirectories(resolved);
            for (int index = 0; index < directories.Length; index++)
            {
                result.Add(new FileSystemEntry(GetName(directories[index]), true, 0));
            }
            string[] files = Directory.GetFiles(resolved);
            for (int index = 0; index < files.Length; index++)
            {
                long size = 0;
                try
                {
                    using (FileStream stream = File.OpenRead(files[index])) { size = stream.Length; }
                }
                catch { }
                result.Add(new FileSystemEntry(GetName(files[index]), false, size));
            }
            return result;
        }

        public void CreateDirectory(string path)
        {
            string resolved = ResolvePath(path);
            if (Directory.Exists(resolved)) { return; }
            Directory.CreateDirectory(resolved);
        }

        public void CreateFile(string path)
        {
            string resolved = ResolvePath(path);
            if (!File.Exists(resolved))
            {
                using (Stream stream = CreateNewFileStream(resolved)) { }
            }
        }

        public string ReadAllText(string path)
        {
            using (Stream stream = VFSManager.GetFileStream(ResolvePath(path)))
            {
                if (stream.Length > MaximumTextFileSize) { throw new InvalidOperationException("Text file exceeds 64 KiB."); }
                byte[] bytes = new byte[(int)stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int count = stream.Read(bytes, read, bytes.Length - read);
                    if (count <= 0) { break; }
                    read += count;
                }
                char[] characters = new char[read];
                for (int index = 0; index < read; index++) { characters[index] = bytes[index] <= 127 ? (char)bytes[index] : '?'; }
                return new string(characters);
            }
        }

        public void WriteAllText(string path, string content)
        {
            string resolved = ResolvePath(path);
            using (Stream stream = File.Exists(resolved) ? VFSManager.GetFileStream(resolved) : CreateNewFileStream(resolved))
            {
                stream.SetLength(0);
                WriteAscii(stream, content);
            }
        }

        public void AppendAllText(string path, string content)
        {
            string resolved = ResolvePath(path);
            using (Stream stream = File.Exists(resolved) ? VFSManager.GetFileStream(resolved) : CreateNewFileStream(resolved))
            {
                stream.Position = stream.Length;
                WriteAscii(stream, content);
            }
        }

        public void Copy(string source, string destination, bool overwrite)
        {
            File.Copy(ResolvePath(source), ResolvePath(destination), overwrite);
        }

        public void Move(string source, string destination, bool overwrite)
        {
            string from = ResolvePath(source);
            string to = ResolvePath(destination);
            if (File.Exists(from))
            {
                if (File.Exists(to))
                {
                    if (!overwrite) { throw new InvalidOperationException("Destination already exists."); }
                    File.Delete(to);
                }
                File.Copy(from, to, false);
                File.Delete(from);
                return;
            }
            if (Directory.Exists(from))
            {
                if (Directory.Exists(to))
                {
                    throw new InvalidOperationException("Destination directory already exists.");
                }
                CopyDirectory(from, to);
                Directory.Delete(from, true);
                return;
            }
            throw new InvalidOperationException("Source was not found.");
        }

        public void Delete(string path, bool recursive)
        {
            string resolved = ResolvePath(path);
            if (File.Exists(resolved))
            {
                File.Delete(resolved);
                return;
            }
            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive);
                return;
            }
            throw new InvalidOperationException("Path was not found.");
        }

        public bool IsProtectedPath(string path)
        {
            string display = ToDisplayPath(path).ToLower();
            return display == "/system" || display.StartsWith("/system/") ||
                   display == "/config" || display.StartsWith("/config/") ||
                   display == "/logs" || display.StartsWith("/logs/") ||
                   display == "/users/users.db";
        }

        private static string NormalizeDriveRoot(string drive)
        {
            string value = drive.Replace('/', '\\');
            int colon = value.IndexOf(':');
            if (colon < 1) { return string.Empty; }
            return value.Substring(0, colon + 1) + "\\";
        }

        private static string GetName(string path)
        {
            string value = path.TrimEnd('\\');
            int separator = value.LastIndexOf('\\');
            return separator < 0 ? value : value.Substring(separator + 1);
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            string[] files = Directory.GetFiles(source);
            for (int index = 0; index < files.Length; index++)
            {
                File.Copy(files[index], destination + "\\" + GetName(files[index]), false);
            }
            string[] directories = Directory.GetDirectories(source);
            for (int index = 0; index < directories.Length; index++)
            {
                CopyDirectory(directories[index], destination + "\\" + GetName(directories[index]));
            }
        }

        private Stream CreateNewFileStream(string path)
        {
            string name;
            string parentPath;
            SplitParent(path, out parentPath, out name);
            DirectoryEntry parent = _vfs.GetDirectory(parentPath);
            Cosmos.System.FileSystem.FileSystem mounted = GetMountedFileSystem(path);
            if (parent == null || mounted == null) { throw new InvalidOperationException("Parent directory is not mounted."); }
            return mounted.CreateFile(parent, name).GetFileStream();
        }

        private Cosmos.System.FileSystem.FileSystem GetMountedFileSystem(string path)
        {
            string root = NormalizeDriveRoot(path.Substring(0, path.IndexOf(':') + 1));
            List<Disk> disks = _vfs.GetDisks();
            for (int diskIndex = 0; diskIndex < disks.Count; diskIndex++)
            {
                List<ManagedPartition> partitions = disks[diskIndex].Partitions;
                for (int partitionIndex = 0; partitionIndex < partitions.Count; partitionIndex++)
                {
                    ManagedPartition partition = partitions[partitionIndex];
                    if (partition.MountedFS != null && NormalizeDriveRoot(partition.RootPath) == root)
                    {
                        return partition.MountedFS;
                    }
                }
            }
            return null;
        }

        private static void SplitParent(string path, out string parentPath, out string name)
        {
            int separator = path.LastIndexOf('\\');
            name = path.Substring(separator + 1);
            parentPath = separator == path.IndexOf(':') + 1 ? path.Substring(0, separator + 1) : path.Substring(0, separator);
        }

        private static void WriteAscii(Stream stream, string content)
        {
            if (content.Length > MaximumTextFileSize) { throw new InvalidOperationException("Text file exceeds 64 KiB."); }
            byte[] bytes = new byte[content.Length];
            for (int index = 0; index < content.Length; index++)
            {
                bytes[index] = content[index] <= 127 ? (byte)content[index] : (byte)'?';
            }
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
