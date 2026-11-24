using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Aion2Meter.Core;

namespace Aion2Meter.App;

/// <summary>
/// Sends a data package (packet recordings + combat-log screenshots) to our collection server: one zip, over HTTPS to the
/// server whose self-signed certificate has the pinned public key. The server enforces its own limits; the checks here
/// only spare the user a pointless upload.
/// </summary>
internal static class DataUploader
{
    private const string ServerBase = "https://161.33.151.67:8443";
    private const string UploadUrl = ServerBase + "/upload";

    /// <summary>The server refuses bigger packages.</summary>
    public const long MaxPackage = 60L * 1024 * 1024;

    // The pin file holds the SHA-256 of the server certificate's public key as 64 hex digits; UpdateTrust compares base64.
    private static readonly string Pin = HexToBase64(Embedded("upload-server-pin"));
    private static readonly string Key = Embedded("upload-key");

    private static string HexToBase64(string hex)
    {
        try { return hex.Length == 64 ? Convert.ToBase64String(Convert.FromHexString(hex)) : ""; }
        catch (FormatException) { return ""; }
    }

    /// <summary>False until the receiving server's pin and key are in the app (they are written at the server's setup).</summary>
    public static bool Configured => Pin.Length > 0 && Key.Length > 0;

    private static string Embedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        return stream is null ? "" : new StreamReader(stream).ReadToEnd().Trim();
    }

    /// <summary>An HTTPS client that trusts only the collection server's pinned key; adds the app key header with <see cref="Authorize"/>.</summary>
    internal static HttpClient NewClient(TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) => UpdateTrust.PinMatches(certificate, Pin),
        };
        return new HttpClient(handler) { Timeout = timeout };
    }

    internal static void Authorize(HttpRequestMessage request)
    {
        request.Headers.Add("X-Aion2-Key", Key);
        request.Headers.Add("X-Aion2-App", typeof(DataUploader).Assembly.GetName().Version?.ToString(3) ?? "0");   // lets the server refuse versions it no longer supports
    }

    internal static string Url(string path) => ServerBase + path;

    public const string UpdateRequiredText = "이 버전으로는 더 이상 보낼 수 없습니다. 최신 버전으로 업데이트한 뒤 다시 시도해 주세요.";
    private static bool _updateNoticeShown;

    /// <summary>The server refused this version (426): one notice window per run, the callers show <see cref="UpdateRequiredText"/> besides.</summary>
    internal static void NoteUpdateRequired()
    {
        if (_updateNoticeShown) return;
        _updateNoticeShown = true;
        Log.Write("server: this version is no longer accepted (426)");
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => System.Windows.MessageBox.Show(
            "서버로 보내는 기능은 최신 업데이트에서만 사용할 수 있습니다.\n설정의 [업데이트]에서 최신 버전으로 업데이트한 뒤 다시 시도해 주세요.",
            "A2Rank 업데이트 필요", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information));
    }

    /// <summary>Recordings in the folder, newest first (the one being written now included).</summary>
    public static List<FileInfo> Recordings(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? new DirectoryInfo(folder).GetFiles("*.pcap").OrderByDescending(f => f.LastWriteTimeUtc).ToList()
                : [];
        }
        catch (IOException) { return []; }
    }

    /// <summary>The zip's size once built, estimated without building it: pictures do not compress, recordings about 3:1.</summary>
    public static long Estimate(IEnumerable<FileInfo> recordings, IEnumerable<FileInfo> images) =>
        recordings.Sum(f => f.Length / 3 + 1) + images.Sum(f => f.Length) + 2048;

    /// <summary>Builds the package in a temp file: meta.json, recordings/*.pcap, screens/*.</summary>
    public static string Build(IReadOnlyList<FileInfo> recordings, IReadOnlyList<FileInfo> images, string note)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aion2-data-{Guid.NewGuid():N}.zip");
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var meta = new
                {
                    app = typeof(DataUploader).Assembly.GetName().Version?.ToString(3),
                    sentUtc = DateTime.UtcNow,
                    recordings = recordings.Select(r => new { r.Name, r.Length, r.LastWriteTimeUtc }).ToArray(),
                    screens = images.Select(i => new { i.Name, i.Length }).ToArray(),
                    note,
                };
                var entry = zip.CreateEntry("meta.json", CompressionLevel.Optimal);
                using (var w = entry.Open()) JsonSerializer.Serialize(w, meta, new JsonSerializerOptions { WriteIndented = true });

                foreach (var r in recordings) AddFile(zip, "recordings/" + r.Name, r.FullName, CompressionLevel.Optimal);
                int n = 0;
                foreach (var i in images)
                    AddFile(zip, $"screens/{++n:00}-{Path.GetFileName(i.Name)}", i.FullName, CompressionLevel.NoCompression);
            }
            return path;
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    // A recording that is still being written is read as far as it goes (shared read/write).
    private static void AddFile(ZipArchive zip, string name, string file, CompressionLevel level)
    {
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = zip.CreateEntry(name, level).Open();
        input.CopyTo(output);
    }

    /// <returns>null when the server accepted the package, otherwise the reason in words.</returns>
    public static async Task<string?> SendAsync(string zipPath, CancellationToken cancel)
    {
        if (!Configured) return "받는 서버 정보가 아직 이 버전에 들어 있지 않습니다.";
        var size = new FileInfo(zipPath).Length;
        if (size > MaxPackage) return $"보낼 데이터가 너무 큽니다 ({size / 1048576} MB, 최대 {MaxPackage / 1048576} MB). 녹화나 이미지를 줄여 주세요.";
        try
        {
            using var http = NewClient(TimeSpan.FromMinutes(10));
            await using var file = File.OpenRead(zipPath);
            using var content = new StreamContent(file);
            content.Headers.ContentType = new("application/zip");
            using var request = new HttpRequestMessage(HttpMethod.Post, UploadUrl) { Content = content };
            Authorize(request);
            using var response = await http.SendAsync(request, cancel);
            if (response.IsSuccessStatusCode) return null;
            return (int)response.StatusCode switch
            {
                426 => NoteAndText(),
                413 => "서버가 받을 수 있는 크기를 넘었습니다.",
                429 => "보낼 수 있는 횟수를 넘었습니다 (한 시간에 3번, 하루 10번까지). 잠시 뒤 다시 보내 주세요.",
                507 => "서버가 이 크기를 받을 수 없습니다.",
                _ => $"서버가 받지 않았습니다 ({(int)response.StatusCode}).",
            };
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return "취소했습니다."; }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
        {
            Log.Write("upload: " + e.Message);
            return "서버에 연결하지 못했습니다. 인터넷 연결을 확인하고 다시 시도해 주세요.";
        }
    }

    internal static string NoteAndText()
    {
        NoteUpdateRequired();
        return UpdateRequiredText;
    }

    public static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}
