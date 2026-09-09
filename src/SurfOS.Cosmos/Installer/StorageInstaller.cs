using System;
using SurfOS.Storage;

namespace SurfOS.Installer
{
    public sealed class StorageInstaller
    {
        private readonly IFileSystemService _fileSystem;

        public StorageInstaller(IFileSystemService fileSystem)
        {
            _fileSystem = fileSystem;
        }

        public void EnsureMountedVolume()
        {
            if (_fileSystem.HasMountedVolume) { return; }
            int count = _fileSystem.GetPhysicalDiskCount();
            if (count == 0)
            {
                throw new InvalidOperationException("No writable disk was detected. Attach an IDE disk of at least 32 MB and reboot.");
            }

            Console.WriteLine();
            Console.WriteLine("No formatted SurfOS volume is mounted.");
            Console.WriteLine("Available physical disks:");
            for (int index = 0; index < count; index++)
            {
                Console.WriteLine("  [" + index + "] " + _fileSystem.GetPhysicalDiskSizeMegabytes(index) + " MB");
            }
            Console.Write("Disk to erase and format as FAT32: ");
            int selected;
            if (!int.TryParse(Console.ReadLine(), out selected) || selected < 0 || selected >= count)
            {
                throw new InvalidOperationException("No valid installation disk was selected.");
            }
            Console.Write("Type ERASE " + selected + " to confirm: ");
            if (Console.ReadLine() != "ERASE " + selected)
            {
                throw new InvalidOperationException("Disk preparation cancelled.");
            }
            Console.WriteLine("Formatting disk. This may take several minutes...");
            _fileSystem.PreparePhysicalDisk(selected);
        }
    }
}
