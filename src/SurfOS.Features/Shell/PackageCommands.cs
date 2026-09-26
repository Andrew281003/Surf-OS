using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SurfOS2;

internal sealed record PackageSignature(string Algorithm, string PublicKey, string Signature);

internal partial class CLI_Engine
{
    private static bool ValidateBuild(string build)
    {
        if (build.EndsWith(".sos", StringComparison.OrdinalIgnoreCase))
        { BackendUnavailable("Surf Kernel .sos validation"); return false; }
        if (!build.EndsWith(".surfpkg", StringComparison.OrdinalIgnoreCase))
        {
            bool valid = PackageBuilder.TryValidateProject(build, out var manifest, out var issues);
            if (!valid) { ShellError(string.Join(Environment.NewLine, issues)); return false; }
            Console.WriteLine($"Valid builder project: {manifest!.Id} {manifest.Version}"); return true;
        }
        ValidateArchive(VirtualFileSystem.ReadBytes(build, MaximumDownloadBytes));
        Console.WriteLine("Package structure and payload checksum passed. This does not establish publisher trust or runtime safety.");
        return true;
    }

    internal static void ValidateArchive(byte[] data)
    {
        using MemoryStream input = new(data);
        using ZipArchive archive = new(input, ZipArchiveMode.Read);
        Require(archive.Entries.Count == 2, "package must contain one manifest.json and one payload file.");
        ZipArchiveEntry manifestEntry = archive.GetEntry("manifest.json") ?? throw new IOException("Missing manifest.json.");
        Require(manifestEntry.Length <= 1024 * 1024, "manifest must not exceed 1 MiB.");
        using var manifestStream = manifestEntry.Open();
        StorePackageManifest manifest = JsonSerializer.Deserialize<StorePackageManifest>(manifestStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new IOException("Empty manifest.");
        Require(PathSafety.IsSafeFileName(manifest.Id) && !string.IsNullOrWhiteSpace(manifest.Name) && !string.IsNullOrWhiteSpace(manifest.Author) && Version.TryParse(manifest.Version, out _), "valid package ID, name, author, and version are required.");
        Require(Version.TryParse(manifest.MinimumSurfOSVersion, out var minimum) && Version.Parse(Import.Variables.version) >= minimum, "package requires a compatible SurfOS version.");
        string install = manifest.InstallPath.Replace('\\', '/');
        Require(install.Split('/').All(PathSafety.IsSafeFileName) && (install.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) || install.StartsWith("apps/" + manifest.Id + "/", StringComparison.OrdinalIgnoreCase)), "package install path must stay inside Packages or its own apps folder.");
        ZipArchiveEntry payload = archive.Entries.Single(e => e != manifestEntry);
        Require(payload.FullName == "payload/" + install.Split('/')[^1] && payload.Length <= MaximumDownloadBytes, "expected one bounded payload file matching InstallPath.");
        using var payloadStream = payload.Open();
        string hash = Convert.ToHexString(SHA256.HashData(payloadStream));
        Require(hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase), "payload SHA-256 must match the manifest.");
    }

    private static void ExecutePublish(string[] args)
    {
        Require(args.Length == 1 || args.Length == 2 && args[1] is "--public" or "--private",
            AdditionalUsage["publish"].Usage);

        PackageVisibility visibility = args.Length == 2 && args[1] == "--public"
            ? PackageVisibility.Public
            : PackageVisibility.Private;
        string build = args[0];
        byte[] packageBytes;
        byte[]? sourceSnapshot = null;
        string packageName;

        if (build.EndsWith(".sos", StringComparison.OrdinalIgnoreCase))
        {
            BackendUnavailable("Surf Kernel .sos publishing");
            return;
        }

        if (build.EndsWith(".surfpkg", StringComparison.OrdinalIgnoreCase))
        {
            packageBytes = VirtualFileSystem.ReadBytes(build, MaximumDownloadBytes);
            ValidateArchive(packageBytes);
            packageName = Path.GetFileName(build);
        }
        else
        {
            if (!PackageBuilder.TryExportProject(build, out string exportPath, out string exportError))
            {
                ShellError(exportError);
                return;
            }
            packageBytes = File.ReadAllBytes(exportPath);
            ValidateArchive(packageBytes);
            packageName = Path.GetFileName(exportPath);
            if (!PackageBuilder.TryCreateSourceSnapshot(build, out byte[] snapshot, out string snapshotError))
            {
                ShellError(snapshotError);
                return;
            }
            sourceSnapshot = snapshot;
        }

        StorePackageManifest manifest = ReadPackageManifest(packageBytes);
        Console.WriteLine($"Publishing {manifest.Name} {manifest.Version} as {visibility.ToString().ToLowerInvariant()}...");
        PublishResult result = SurfCloudPublisher.PublishAsync(
                packageBytes,
                packageName,
                manifest,
                visibility,
                sourceSnapshot)
            .GetAwaiter()
            .GetResult();

        if (!result.Success)
        {
            ShellError(result.Message);
            return;
        }

        Console.WriteLine(result.Message);
        if (!string.IsNullOrWhiteSpace(result.PublicationUrl))
        {
            Console.WriteLine($"URL: {result.PublicationUrl}");
        }
    }

    internal static StorePackageManifest ReadPackageManifest(byte[] data)
    {
        using MemoryStream input = new(data);
        using ZipArchive archive = new(input, ZipArchiveMode.Read);
        ZipArchiveEntry manifestEntry = archive.GetEntry("manifest.json") ??
            throw new IOException("Missing manifest.json.");
        using Stream manifestStream = manifestEntry.Open();
        return JsonSerializer.Deserialize<StorePackageManifest>(manifestStream,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
               throw new IOException("Empty manifest.");
    }

    internal static PackageSignature SignBytes(byte[] data, RSA rsa) => new("RSA-SHA256-PSS", Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), Convert.ToBase64String(rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)));
    internal static bool VerifyBytes(byte[] data, PackageSignature signature)
    {
        if (signature.Algorithm != "RSA-SHA256-PSS") return false;
        using RSA rsa = RSA.Create(); rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(signature.PublicKey), out _);
        return rsa.KeySize >= 2048 && rsa.VerifyData(data, Convert.FromBase64String(signature.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }

    private static void ExecuteSign(string[] args)
    {
        bool verify = args.Length == 2 && args[0] == "verify";
        Require(verify || args.Length == 1, AdditionalUsage["sign"].Usage);
        string path = args[^1]; byte[] data = VirtualFileSystem.ReadBytes(path, MaximumDownloadBytes);
        string signaturePath = path + ".sig.json";
        if (verify)
        {
            var signature = JsonSerializer.Deserialize<PackageSignature>(VirtualFileSystem.ReadBytes(signaturePath, 65536)) ?? throw new IOException("Invalid signature document.");
            if (!VerifyBytes(data, signature)) { ShellError("sign: signature verification failed."); return; }
            Console.WriteLine($"Signature valid. Signer SHA-256: {Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(signature.PublicKey)))}");
            Console.WriteLine("Integrity verified against the included public key; publisher identity is not trusted automatically.");
            return;
        }
        string keyName = "SurfOS.PackageSigning." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(Import.Variables.installPath) + "|" + Import.Variables.userName)))[..24];
        using RSA signer = OpenSigningKey(keyName);
        VirtualFileSystem.WriteBytes(signaturePath, JsonSerializer.SerializeToUtf8Bytes(SignBytes(data, signer)));
        Console.WriteLine($"Signed {path}; detached signature: {signaturePath}");
    }

    private static RSA OpenSigningKey(string keyName)
    {
        if (OperatingSystem.IsWindows())
        {
            CngKey key = CngKey.Exists(keyName) ? CngKey.Open(keyName) : CngKey.Create(
                CngAlgorithm.Rsa,
                keyName,
                new CngKeyCreationParameters
                {
                    ExportPolicy = CngExportPolicies.None,
                    KeyUsage = CngKeyUsages.Signing,
                    Parameters =
                    {
                        new CngProperty(
                            "Length",
                            BitConverter.GetBytes(3072),
                            CngPropertyOptions.None)
                    }
                });
            return new RSACng(key);
        }

        string keyDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SurfOS",
            "keys");
        string keyPath = Path.Combine(keyDirectory, keyName + ".pem");
        Directory.CreateDirectory(keyDirectory);

        RSA rsa = RSA.Create();
        if (File.Exists(keyPath))
        {
            rsa.ImportFromPem(File.ReadAllText(keyPath));
            return rsa;
        }

        rsa.KeySize = 3072;
        File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem());
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                keyPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return rsa;
    }
}
