using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Aion2Meter.Core;

namespace Aion2Meter.Tests;

public class UpdateTrustTests
{
    private static readonly byte[] Feed = Encoding.UTF8.GetBytes("{\"Assets\":[{\"Version\":\"0.1.8\",\"SHA256\":\"AA\"}]}");

    private static (ECDsa Key, byte[] Spki) NewKey()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key, key.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void A_correctly_signed_feed_is_accepted()
    {
        var (key, spki) = NewKey();
        Assert.True(UpdateTrust.VerifyFeed(Feed, key.SignData(Feed, HashAlgorithmName.SHA256), spki));
    }

    [Fact]
    public void A_changed_feed_is_rejected()
    {
        var (key, spki) = NewKey();
        var signature = key.SignData(Feed, HashAlgorithmName.SHA256);
        var tampered = (byte[])Feed.Clone();
        tampered[20] ^= 1;
        Assert.False(UpdateTrust.VerifyFeed(tampered, signature, spki));
    }

    [Fact]
    public void A_signature_from_another_key_is_rejected()
    {
        var (_, spki) = NewKey();
        var (other, _) = NewKey();
        Assert.False(UpdateTrust.VerifyFeed(Feed, other.SignData(Feed, HashAlgorithmName.SHA256), spki));
    }

    [Fact]
    public void Missing_truncated_or_garbage_input_is_rejected_not_thrown()
    {
        var (key, spki) = NewKey();
        var signature = key.SignData(Feed, HashAlgorithmName.SHA256);
        Assert.False(UpdateTrust.VerifyFeed(Feed, [], spki));                                  // no signature file content
        Assert.False(UpdateTrust.VerifyFeed(Feed, signature.AsSpan(0, 40), spki));             // truncated
        Assert.False(UpdateTrust.VerifyFeed(Feed, signature, [1, 2, 3]));                      // broken public key
        Assert.False(UpdateTrust.VerifyFeed(Feed, Encoding.ASCII.GetBytes("<html>404</html>"), spki)); // an error page instead of a signature
    }

    private static X509Certificate2 SelfSigned()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=test", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    [Fact]
    public void Pin_matches_only_the_certificate_with_that_public_key()
    {
        using var server = SelfSigned();
        using var impostor = SelfSigned();
        var pin = UpdateTrust.PinOf(server);
        Assert.True(UpdateTrust.PinMatches(server, pin));
        Assert.False(UpdateTrust.PinMatches(impostor, pin));
        Assert.False(UpdateTrust.PinMatches(null, pin));
        Assert.False(UpdateTrust.PinMatches(server, ""));
    }
}
