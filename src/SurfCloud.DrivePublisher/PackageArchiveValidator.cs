using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

internal static class PackageArchiveValidator
{
    private const long MaxPackageBytes = 64L * 1024 * 1024;
    private const long MaxSourceBytes = 32L * 1024 * 1024;
    private const long MaxExpandedPackageBytes = 64L * 1024 * 1024;
    private const long MaxExpandedSourceBytes = 64L * 1024 * 1024;
    private const int MaxPackageEntries = 2;
    private const int MaxSourceEntries = 256;
    private const long MaxEntryBytes = 32L * 1024 * 1024;
    private const int MaxCompressionRatio = 100;

    public static async Task<string> ValidatePackageAsync(IFormFile file, PackageManifest submitted, CancellationToken cancellationToken)
    {
        if (file.Length is < 22 or > MaxPackageBytes) throw new InvalidDataException("Package size is invalid.");
        await RequireZipHeaderAsync(file, cancellationToken);
        string digest;
        await using (Stream input = file.OpenReadStream())
            digest = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
        await using Stream archiveStream = file.OpenReadStream();
        using ZipArchive archive = new(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count != MaxPackageEntries) throw new InvalidDataException("Package requires exactly manifest and payload entries.");
        HashSet<string> names = ValidateEntries(archive, MaxPackageEntries, MaxExpandedPackageBytes);
        if (!names.Contains("manifest.json")) throw new InvalidDataException("Package manifest is missing.");
        ZipArchiveEntry embeddedEntry = archive.GetEntry("manifest.json")!;
        if (embeddedEntry.Length is <= 0 or > 64 * 1024) throw new InvalidDataException("Manifest size is invalid.");
        PackageManifest? embedded;
        await using (Stream stream = embeddedEntry.Open())
            embedded = await JsonSerializer.DeserializeAsync<PackageManifest>(stream, cancellationToken: cancellationToken);
        if (embedded is null || !PackageId.IsValid(embedded.Id) || embedded.Id is "." or ".." ||
            string.IsNullOrWhiteSpace(embedded.Name) || embedded.Name.Length > 160 ||
            !Version.TryParse(embedded.Version, out _) ||
            !IsSha256(embedded.Sha256) ||
            string.IsNullOrWhiteSpace(embedded.Author) || embedded.Author.Length > 160 ||
            !IsSafeInstallPath(embedded.InstallPath))
            throw new InvalidDataException("Embedded manifest is invalid.");
        if (!string.Equals(embedded.Id, submitted.Id, StringComparison.Ordinal) ||
            !string.Equals(embedded.Name, submitted.Name, StringComparison.Ordinal) ||
            !string.Equals(embedded.Version, submitted.Version, StringComparison.Ordinal) ||
            !string.Equals(embedded.Author, submitted.Author, StringComparison.Ordinal) ||
            !string.Equals(embedded.Sha256, submitted.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(embedded.InstallPath, submitted.InstallPath, StringComparison.Ordinal) ||
            !string.Equals(embedded.Command, submitted.Command, StringComparison.Ordinal))
            throw new InvalidDataException("Submitted and embedded manifests differ.");
        ZipArchiveEntry payload = archive.Entries.Single(entry => entry.FullName != "manifest.json");
        if (payload.FullName != "payload/" + embedded.InstallPath.Split('/')[^1] ||
            payload.Length is <= 0 or > MaxEntryBytes ||
            !embedded.InstallPath.StartsWith("apps/" + embedded.Id + "/", StringComparison.Ordinal) &&
            !embedded.InstallPath.StartsWith("Packages/", StringComparison.Ordinal))
            throw new InvalidDataException("Package payload is invalid.");
        await using Stream payloadStream = payload.Open();
        using IncrementalHash payloadHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] payloadBuffer = new byte[81920];
        long actualLength = 0;
        int payloadCount;
        while ((payloadCount = await payloadStream.ReadAsync(payloadBuffer, cancellationToken)) > 0)
        {
            actualLength = checked(actualLength + payloadCount);
            if (actualLength > MaxEntryBytes) throw new InvalidDataException("Payload expanded size exceeds limit.");
            payloadHash.AppendData(payloadBuffer, 0, payloadCount);
        }
        string actual = Convert.ToHexString(payloadHash.GetHashAndReset());
        if (!actual.Equals(embedded.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Payload hash mismatch.");
        return digest;
    }

    public static async Task ValidateSourceAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length is < 22 or > MaxSourceBytes) throw new InvalidDataException("Source snapshot size is invalid.");
        await RequireZipHeaderAsync(file, cancellationToken);
        await using Stream input = file.OpenReadStream();
        using ZipArchive archive = new(input, ZipArchiveMode.Read);
        ValidateEntries(archive, MaxSourceEntries, MaxExpandedSourceBytes);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            if (Path.GetFileName(entry.FullName).Equals("secrets.json", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(entry.FullName).ToLowerInvariant() is ".pem" or ".p12" or ".pfx")
                throw new InvalidDataException("Source snapshot contains credential-like file.");
            await using Stream stream = entry.Open();
            using MemoryStream bounded = new();
            byte[] buffer = new byte[81920];
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (bounded.Length + count > MaxEntryBytes)
                    throw new InvalidDataException("Source entry expanded size exceeds limit.");
                bounded.Write(buffer, 0, count);
            }
            if (LooksLikeCredential(bounded.GetBuffer().AsSpan(0, (int)bounded.Length)))
                throw new InvalidDataException("Source snapshot contains unsafe content.");
        }
    }

    private static HashSet<string> ValidateEntries(ZipArchive archive, int maxEntries, long maxExpanded)
    {
        if (archive.Entries.Count is 0 || archive.Entries.Count > maxEntries)
            throw new InvalidDataException("Archive entry count is invalid.");
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName;
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/') || name.Contains('\\') || name.Contains(':') ||
                name.Split('/').Any(part => part is "" or "." or "..") && !name.EndsWith('/') ||
                name.EndsWith('/') && name[..^1].Split('/').Any(part => part is "" or "." or "..") ||
                !names.Add(name.TrimEnd('/')))
                throw new InvalidDataException("Archive path is unsafe or duplicated.");
            int unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixMode is not (0 or 0x8000 or 0x4000) ||
                (entry.FullName.EndsWith('/') ? unixMode == 0x8000 : unixMode == 0x4000))
                throw new InvalidDataException("Archive entry type is unsupported.");
            if (entry.Length > MaxEntryBytes || (entry.Length > 0 && entry.CompressedLength == 0) ||
                (entry.CompressedLength > 0 && entry.Length > entry.CompressedLength * MaxCompressionRatio))
                throw new InvalidDataException("Archive entry exceeds size or compression limits.");
            total = checked(total + entry.Length);
            if (total > maxExpanded) throw new InvalidDataException("Archive expanded size exceeds limit.");
        }
        return names;
    }

    private static bool IsSha256(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);

    private static async Task RequireZipHeaderAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using Stream stream = file.OpenReadStream();
        byte[] header = new byte[4];
        if (await stream.ReadAsync(header, cancellationToken) != 4 ||
            header[0] != (byte)'P' || header[1] != (byte)'K' || header[2] != 3 || header[3] != 4)
            throw new InvalidDataException("Unsupported archive format.");
    }

    private static bool LooksLikeCredential(ReadOnlySpan<byte> bytes) =>
        bytes.IndexOf(Encoding.UTF8.GetBytes("-----BEGIN " + "PRIVATE KEY-----")) >= 0 ||
        bytes.IndexOf(Encoding.UTF8.GetBytes("-----BEGIN RSA " + "PRIVATE KEY-----")) >= 0 ||
        bytes.IndexOf("\"private_key\""u8) >= 0 && bytes.IndexOf("service_account"u8) >= 0;

    private static bool IsSafeInstallPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\\') || value.Contains(':') || value.StartsWith('/')) return false;
        string[] parts = value.Split('/');
        return parts.Length >= 2 && parts.All(part => part is not ("" or "." or "..")) &&
               parts[0] is "apps" or "Packages";
    }
}
