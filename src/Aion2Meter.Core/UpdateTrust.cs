using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Aion2Meter.Core;

/// <summary>
/// What the updater trusts: an update list is accepted only with a valid signature from our key, and the update
/// server is accepted only when its certificate has the pinned public key (it is self-signed on purpose).
/// </summary>
public static class UpdateTrust
{
    /// <summary>ECDSA P-256 / SHA-256 signature (IEEE P1363, 64 bytes) over the exact bytes of the update list.</summary>
    /// <param name="publicKeySpki">The signer's public key, DER SubjectPublicKeyInfo.</param>
    public static bool VerifyFeed(ReadOnlySpan<byte> feed, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKeySpki)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKeySpki, out _);
            return key.VerifyData(feed, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>SHA-256 of the certificate's public key (SubjectPublicKeyInfo), base64: stays the same when the certificate is renewed with the same key.</summary>
    public static string PinOf(X509Certificate2 certificate) =>
        Convert.ToBase64String(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));

    public static bool PinMatches(X509Certificate? certificate, string pin)
    {
        if (certificate is null || pin.Length == 0) return false;
        using var cert = new X509Certificate2(certificate);
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(PinOf(cert)), System.Text.Encoding.ASCII.GetBytes(pin));
    }
}
