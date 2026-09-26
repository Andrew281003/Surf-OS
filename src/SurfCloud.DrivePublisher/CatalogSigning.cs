using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC.Rfc8032;

internal sealed record SignedCatalog(string Algorithm, string KeyId, long Sequence, string Payload, string Signature);

internal static class CatalogSigning
{
    public static SignedCatalog Sign(byte[] manifest, string keyId, long sequence, byte[] signingSeed)
    {
        if (manifest.Length is 0 or > 1024 * 1024 || sequence < 1 ||
            !System.Text.RegularExpressions.Regex.IsMatch(keyId, "^[a-z0-9_-]{1,32}$"))
            throw new InvalidDataException("Catalog signing input is invalid.");
        string payload = Convert.ToBase64String(manifest);
        byte[] signedBytes = Encoding.UTF8.GetBytes(sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + payload);
        if (signingSeed.Length != Ed25519.SecretKeySize)
            throw new InvalidDataException("Catalog signing key is invalid.");
        Ed25519PrivateKeyParameters privateKey = new(signingSeed);
        byte[] signature = new byte[Ed25519.SignatureSize];
        privateKey.Sign(Ed25519.Algorithm.Ed25519, null!, signedBytes, 0, signedBytes.Length, signature, 0);
        return new SignedCatalog("Ed25519", keyId, sequence, payload, Convert.ToBase64String(signature));
    }
}
