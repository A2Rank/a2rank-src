using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Aion2Meter.Core;
using Velopack.Sources;

namespace Aion2Meter.App;

/// <summary>
/// Downloads update files only over HTTPS (normal certificate check: the files are on GitHub releases), and accepts the update list
/// (releases.win.json) only when its .sig file verifies with our public key. Packages are then checked by Velopack against the hashes in that list.
/// The signature is what protects the updates: whoever gets into the account that holds the release files still cannot make an app install
/// something we did not sign.
/// </summary>
internal sealed class SecureDownloader : HttpClientFileDownloader
{
    private const string FeedFile = "releases.win.json";

    private static readonly byte[] PublicKey = Convert.FromBase64String(Embedded("update-public-key"));

    private static string Embedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"{name} is not embedded");
        return new StreamReader(stream).ReadToEnd().Trim();
    }

    public override Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers = null,
                                      double timeout = 30, CancellationToken cancelToken = default)
    {
        RequireHttps(url);
        return base.DownloadFile(url, targetFile, progress, headers, timeout, cancelToken);
    }

    public override async Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
    {
        RequireHttps(url);
        return await base.DownloadBytes(url, headers, timeout);
    }

    public override async Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
    {
        var bytes = await DownloadBytes(url, headers, timeout);
        var path = new Uri(url).AbsolutePath;
        if (path.EndsWith("/" + FeedFile, StringComparison.OrdinalIgnoreCase))
        {
            var signatureUrl = new UriBuilder(url) { Query = "" }.Uri.AbsoluteUri + ".sig";
            byte[] signature;
            try { signature = await DownloadBytes(signatureUrl, headers, timeout); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                throw new InvalidDataException("the update list has no signature file: " + e.Message, e);
            }
            if (!UpdateTrust.VerifyFeed(bytes, signature, PublicKey))
                throw new InvalidDataException("the update list's signature is not valid - not updating");
        }
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8);   // also drops a BOM
        return reader.ReadToEnd();
    }

    private static void RequireHttps(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("updates are only downloaded over HTTPS");
    }
}
