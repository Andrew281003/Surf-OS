using System.Reflection;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace SurfOS2;

internal sealed record SignedCatalog(string Algorithm, string KeyId, long Sequence, string Payload, string Signature);

internal static class CatalogTrust
{
    public static bool TryVerify(string json, long minimumSequence, out StoreManifest? manifest, out long sequence)
        => TryVerify(json, minimumSequence, GetPinnedKey, out manifest, out sequence);

    internal static bool TryVerify(string json, long minimumSequence, Func<string, string?> pinnedKey,
        out StoreManifest? manifest, out long sequence)
    {
        manifest = null;
        sequence = 0;
        try
        {
            SignedCatalog? envelope = JsonSerializer.Deserialize<SignedCatalog>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (envelope is null || envelope.Algorithm != "Ed25519" ||
                envelope.Sequence < minimumSequence || envelope.Sequence < 1 ||
                !System.Text.RegularExpressions.Regex.IsMatch(envelope.KeyId, "^[a-z0-9_-]{1,32}$")) return false;
            string? publicKeyText = pinnedKey(envelope.KeyId);
            if (publicKeyText is null) return false;
            byte[] publicKeyBytes = Convert.FromBase64String(publicKeyText.Trim());
            if (publicKeyBytes.Length != Ed25519.PublicKeySize) return false;
            Ed25519PublicKeyParameters publicKey = new(publicKeyBytes);
            byte[] payload = Convert.FromBase64String(envelope.Payload);
            if (payload.Length > 1024 * 1024) return false;
            byte[] signature = Convert.FromBase64String(envelope.Signature);
            byte[] signedBytes = Encoding.UTF8.GetBytes(envelope.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + envelope.Payload);
            if (signature.Length != Ed25519.SignatureSize ||
                !publicKey.Verify(Ed25519.Algorithm.Ed25519, null!, signedBytes, 0, signedBytes.Length, signature, 0)) return false;
            manifest = JsonSerializer.Deserialize<StoreManifest>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest?.Packages is null || manifest.Packages.Count == 0 ||
                manifest.Packages.Any(package => package is null || package.Format != "surfpkg-v1" ||
                    package.PackageSha256 is null || package.PackageSha256.Length != 64 || !package.PackageSha256.All(Uri.IsHexDigit) ||
                    string.IsNullOrWhiteSpace(package.PublisherSubject))) return false;
            sequence = envelope.Sequence;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or IOException)
        {
            return false;
        }
    }

    private static string? GetPinnedKey(string keyId)
    {
        string resourceName = "SurfOS.Trust.catalog-public-prod-" + keyId + ".pub";
        using Stream? resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (resource is null) return null;
        using StreamReader reader = new(resource);
        return reader.ReadToEnd();
    }
}
