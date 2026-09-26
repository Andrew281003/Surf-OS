using Microsoft.AspNetCore.Http;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using SurfOS2;

if (args.Contains("--benchmark"))
{
    await Benchmarks.RunAsync();
    return;
}

byte[] payload = Encoding.UTF8.GetBytes("echo safe\n");
PackageManifest manifest = new("test-app", "Test App", "1.0.0", "Tester", Convert.ToHexString(SHA256.HashData(payload)), "apps/test-app/test-app.surf", "run /apps/test-app/test-app.surf");
byte[] valid = Archive(("manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest), false), ("payload/test-app.surf", payload, false));
await Accept(valid, manifest);
await Reject(Archive(("../manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest), false), ("payload/test-app.surf", payload, false)), manifest, "traversal");
await Reject(Archive(("manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest), false), ("MANIFEST.JSON", JsonSerializer.SerializeToUtf8Bytes(manifest), false)), manifest, "duplicate");
await Reject(Archive(("manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest), false), ("payload/test-app.surf", payload, true)), manifest, "symlink");
await Reject(valid, manifest with { Id = "another-app" }, "mismatched ID");
await Reject(Archive(("manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest with { Sha256 = new string('0', 64) }), false), ("payload/test-app.surf", payload, false)), manifest with { Sha256 = new string('0', 64) }, "bad hash");
await Reject(Archive(("manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest), false), ("payload/test-app.surf", new byte[1024 * 1024], false)), manifest, "compression bomb");
await RejectSource(Archive(("../escape.txt", payload, false)), "source traversal");
await RejectSource(Archive(("secrets.json", payload, false)), "source credential filename");
await RejectSource(Archive(("notes.txt", Encoding.UTF8.GetBytes("-----BEGIN " + "PRIVATE KEY-----"), false)), "source credential content");
await Reject(Encoding.UTF8.GetBytes("not a zip"), manifest, "unsupported format");
if (PackageId.IsValid("CON") || PackageId.IsValid("..")) throw new Exception("Reserved package ID accepted.");
List<string> deleted = [];
List<string> errors = [];
await DriveCleanup.CleanupAsync(["first", "second"], (id, _) =>
{
    deleted.Add(id);
    if (id == "first") throw new IOException("injected cleanup failure");
    return Task.CompletedTask;
}, (id, _) => errors.Add(id), TimeSpan.FromSeconds(1));
if (!deleted.SequenceEqual(["first", "second"]) || !errors.SequenceEqual(["first"]))
    throw new Exception("Cleanup skipped an object after a failure.");
byte[] seed = RandomNumberGenerator.GetBytes(32);
string publicKey = Convert.ToBase64String(new Ed25519PrivateKeyParameters(seed).GeneratePublicKey().GetEncoded());
byte[] catalog = JsonSerializer.SerializeToUtf8Bytes(new StoreManifest { Packages = [new StorePackageManifest
{
    Id = "test-app", Format = "surfpkg-v1", PackageSha256 = new string('A', 64), PublisherSubject = "subject-a"
}] });
SignedCatalog signed = CatalogSigning.Sign(catalog, "local-test", 7, seed);
string signedJson = JsonSerializer.Serialize(signed);
if (!CatalogTrust.TryVerify(signedJson, 7, id => id == "local-test" ? publicKey : null, out _, out long acceptedSequence) || acceptedSequence != 7)
    throw new Exception("Valid Ed25519 catalog rejected.");
if (CatalogTrust.TryVerify(signedJson, 8, _ => publicKey, out _, out _) ||
    CatalogTrust.TryVerify(JsonSerializer.Serialize(signed with { Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"Packages\":[{\"Id\":\"evil-app\"}]}")) }), 7, _ => publicKey, out _, out _) ||
    CatalogTrust.TryVerify(signedJson, 7, _ => null, out _, out _))
    throw new Exception("Untrusted catalog accepted.");
if (CatalogTrust.TryVerify(signedJson, 0, out _, out _))
    throw new Exception("Development key was trusted by the production pin set.");
byte[] nullEntryCatalog = Encoding.UTF8.GetBytes("{\"Packages\":[null]}");
SignedCatalog signedNullEntry = CatalogSigning.Sign(nullEntryCatalog, "local-test", 8, seed);
if (CatalogTrust.TryVerify(JsonSerializer.Serialize(signedNullEntry), 8, _ => publicKey, out _, out _))
    throw new Exception("Null catalog entry was accepted.");
int requests = 0;
List<TimeSpan> waits = [];
using HttpClient retryClient = new(new FaultHandler(() =>
{
    requests++;
    if (requests == 1)
    {
        HttpResponseMessage transient = new(System.Net.HttpStatusCode.TooManyRequests);
        transient.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
        return transient;
    }
    return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
}));
using HttpResponseMessage retried = await HttpRetry.SendAsync(retryClient,
    () => new HttpRequestMessage(HttpMethod.Get, "https://example.invalid"), CancellationToken.None,
    (wait, _) => { waits.Add(wait); return Task.CompletedTask; }, 0);
if (retried.StatusCode != System.Net.HttpStatusCode.OK || requests != 2 || !waits.SequenceEqual([TimeSpan.FromSeconds(2)]))
    throw new Exception("Retry-After was not honored.");
Console.WriteLine("PASS: valid package, malformed archives, cleanup, Ed25519 catalog trust, retry policy");

static byte[] Archive(params (string Name, byte[] Bytes, bool Symlink)[] entries)
{
    using MemoryStream output = new();
    using (ZipArchive zip = new(output, ZipArchiveMode.Create, true))
    {
        foreach (var item in entries)
        {
            ZipArchiveEntry entry = zip.CreateEntry(item.Name);
            if (item.Symlink) entry.ExternalAttributes = 0xA000 << 16;
            using Stream target = entry.Open();
            target.Write(item.Bytes);
        }
    }
    return output.ToArray();
}

static FormFile File(byte[] bytes) => new(new MemoryStream(bytes), 0, bytes.Length, "package", "test.surfpkg");

static async Task Accept(byte[] bytes, PackageManifest manifest)
{
    string digest = await PackageArchiveValidator.ValidatePackageAsync(File(bytes), manifest, CancellationToken.None);
    if (digest != Convert.ToHexString(SHA256.HashData(bytes))) throw new Exception("Digest mismatch.");
}

static async Task Reject(byte[] bytes, PackageManifest manifest, string label)
{
    try { await PackageArchiveValidator.ValidatePackageAsync(File(bytes), manifest, CancellationToken.None); }
    catch (InvalidDataException) { return; }
    throw new Exception($"Accepted {label}.");
}

static async Task RejectSource(byte[] bytes, string label)
{
    try { await PackageArchiveValidator.ValidateSourceAsync(File(bytes), CancellationToken.None); }
    catch (InvalidDataException) { return; }
    throw new Exception($"Accepted {label}.");
}

internal sealed record PackageManifest(string Id, string Name, string Version, string Author, string Sha256, string InstallPath, string Command);
internal static class PackageId
{
    public static bool IsValid(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 80 && id is not "." and not ".." &&
        !id.EndsWith('.') && System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9._-]+$") &&
        !System.Text.RegularExpressions.Regex.IsMatch(id, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\..*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}

namespace SurfOS2
{
    internal sealed class StoreManifest
    {
        public List<StorePackageManifest> Packages { get; set; } = [];
    }
    internal sealed class StorePackageManifest
    {
        public string Id { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string PackageSha256 { get; set; } = string.Empty;
        public string PublisherSubject { get; set; } = string.Empty;
    }
}

internal sealed class FaultHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(response());
}
