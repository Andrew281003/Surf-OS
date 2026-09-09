namespace SurfOS2;

internal sealed record SystemFootprintLayout(
    string Name,
    long ComponentStoreBytes,
    long OfflineCacheBytes);

internal static class SystemFootprint_Manager
{
    private const long Mb = 1024L * 1024L;
    private static readonly SystemFootprintLayout[] Layouts =
    [
        new("Compact", 256 * Mb, 128 * Mb),
        new("Standard", 1024 * Mb, 512 * Mb),
        new("Complete", 2048 * Mb, 1024 * Mb)
    ];

    public static string[] GetSupportedProfiles(int partitionSizeGb, int swapSizeGb)
    {
        long usableBytes = partitionSizeGb * Partition_Manager.BytesPerGb -
                           Partition_Manager.SystemReservedMb * Mb -
                           256 * Mb;
        string[] supported = Layouts
            .Where(layout => EstimateInitialUsage(layout.Name, swapSizeGb) <= usableBytes)
            .Select(layout => layout.Name)
            .ToArray();
        return supported.Length > 0 ? supported : ["Compact"];
    }

    public static string FormatEstimatedSize(string profile, int swapSizeGb)
    {
        double gb = EstimateInitialUsage(profile, swapSizeGb) /
                    (double)Partition_Manager.BytesPerGb;
        return $"about {gb:0.0} GB installed";
    }

    public static long EstimateInitialUsage(string profile, int swapSizeGb)
    {
        SystemFootprintLayout layout = GetLayout(profile);
        long swapBytes = swapSizeGb * Partition_Manager.BytesPerGb;
        long recoverySnapshotBytes = layout.ComponentStoreBytes + layout.OfflineCacheBytes;
        return swapBytes + layout.ComponentStoreBytes + layout.OfflineCacheBytes +
               recoverySnapshotBytes + 64 * Mb;
    }

    public static void CreateSystemPayload(string installPath)
    {
        SystemFootprintLayout layout = GetLayout(Import.Variables.systemFootprint);
        long estimatedBytes = EstimateInitialUsage(layout.Name, Import.Variables.swapSizeGb);
        if (!Partition_Manager.CanAllocate(installPath, estimatedBytes, out string capacityError))
        {
            throw new IOException(capacityError);
        }

        string systemDirectory = Path.Combine(installPath, "system");
        string componentDirectory = Path.Combine(systemDirectory, "component-store");
        string cacheDirectory = Path.Combine(systemDirectory, "cache", "packages");
        string swapDirectory = Path.Combine(systemDirectory, "swap");
        string languageDirectory = Path.Combine(systemDirectory, "languages");
        string fontDirectory = Path.Combine(systemDirectory, "fonts");
        Directory.CreateDirectory(componentDirectory);
        Directory.CreateDirectory(cacheDirectory);
        Directory.CreateDirectory(swapDirectory);
        Directory.CreateDirectory(languageDirectory);
        Directory.CreateDirectory(fontDirectory);

        RetroConsole.TypeLine(
            $"Provisioning {layout.Name} SurfOS component footprint...", 2);
        CreateAllocatedFile(
            Path.Combine(componentDirectory, "component-store.sfs"),
            layout.ComponentStoreBytes,
            "SurfOS component store");
        CreateAllocatedFile(
            Path.Combine(cacheDirectory, "offline-packages.cache"),
            layout.OfflineCacheBytes,
            "SurfOS offline package cache");

        if (Import.Variables.swapSizeGb > 0)
        {
            CreateAllocatedFile(
                Path.Combine(swapDirectory, "swapfile.sys"),
                Import.Variables.swapSizeGb * Partition_Manager.BytesPerGb,
                "SurfOS virtual memory swap file");
        }

        string[] languages =
            ["English (US)", "English (UK)", "German", "Hungarian", "Spanish"];
        foreach (string language in languages)
        {
            string safeName = language
                .Replace(" ", "-", StringComparison.Ordinal)
                .Replace("(", string.Empty, StringComparison.Ordinal)
                .Replace(")", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
            JsonStorage.Write(
                Path.Combine(languageDirectory, $"{safeName}.language.json"),
                new
                {
                    Language = language,
                    Installed = language.Equals(
                        Import.Variables.language,
                        StringComparison.OrdinalIgnoreCase),
                    Version = "1.0"
                });
        }

        JsonStorage.Write(
            Path.Combine(fontDirectory, "font-catalog.json"),
            new
            {
                Default = "Consolas",
                Fonts = new[] { "Consolas", "Cascadia Mono", "Lucida Console" },
                Profile = layout.Name
            });
        JsonStorage.Write(
            Path.Combine(systemDirectory, "component-manifest.json"),
            new
            {
                Profile = layout.Name,
                ComponentStoreBytes = layout.ComponentStoreBytes,
                OfflineCacheBytes = layout.OfflineCacheBytes,
                SwapBytes = Import.Variables.swapSizeGb * Partition_Manager.BytesPerGb,
                CreatedUtc = DateTime.UtcNow
            });

        KernelLog.Success(
            "install",
            $"{layout.Name} system footprint provisioned ({FormatEstimatedSize(layout.Name, Import.Variables.swapSizeGb)})");
    }

    private static SystemFootprintLayout GetLayout(string profile)
    {
        return Layouts.FirstOrDefault(layout =>
                   layout.Name.Equals(profile, StringComparison.OrdinalIgnoreCase))
               ?? Layouts[1];
    }

    private static void CreateAllocatedFile(string path, long size, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read);
        stream.SetLength(size);
        stream.Position = 0;
        using StreamWriter writer = new(stream, leaveOpen: true);
        writer.WriteLine(description);
        writer.WriteLine($"Allocated: {size} bytes");
        writer.WriteLine($"Created: {DateTime.UtcNow:O}");
        writer.Flush();
    }
}
