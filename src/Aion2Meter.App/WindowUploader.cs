using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Aion2Meter.Core;

namespace Aion2Meter.App;

/// <summary>
/// Sends the counted periods ("구간 기록", see <see cref="WindowRecord"/>) to the collection server, reads a character's periods back, and asks the
/// server to forget what this installation sent. A character is told by name and server; the other players' names are never sent.
/// </summary>
internal static class WindowUploader
{
    /// <summary>Most windows in one request (the server takes up to 48).</summary>
    public const int BatchSize = 24;

    /// <param name="windowsJson">Each element is one <see cref="WindowRecord.ToJson"/> as kept on this PC (with the names): they are taken out here.</param>
    /// <returns>null when the server took them (windows it flagged as odd are kept there too), otherwise why not.</returns>
    public static async Task<string?> SendAsync(string installId, IReadOnlyList<string> windowsJson, CancellationToken cancel)
    {
        if (!DataUploader.Configured) return "받는 서버 정보가 이 버전에 들어 있지 않습니다.";
        string version = typeof(WindowUploader).Assembly.GetName().Version?.ToString(3) ?? "";
        var plain = new List<string>();
        foreach (var json in windowsJson)
        {
            try { plain.Add(WindowRecord.FromJson(json).ToJson(withNames: false)); }
            catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                Log.Write("windows upload: a kept record could not be read and is skipped: " + e.Message);
            }
        }
        if (plain.Count == 0) return null;   // nothing readable: not worth asking again
        string body = $"{{\"v\":1,\"install\":\"{installId}\",\"app\":\"{version}\",\"windows\":[{string.Join(',', plain)}]}}";
        return (await PostAsync("/windows", body, cancel)).Problem;
    }

    /// <summary>Sends a message from the contact window (the reply address is optional): null when the server took it, else the reason.</summary>
    public const int MaxContactImages = 3;
    public const int MaxContactImage = 4 * 1024 * 1024;

    public static async Task<string?> ContactAsync(string message, string reply, IReadOnlyList<(string Name, byte[] Data)> images, CancellationToken cancel) =>
        (await PostAsync("/contact", JsonSerializer.Serialize(new { m = message, r = reply, i = images.Select(x => new { n = x.Name, d = Convert.ToBase64String(x.Data) }).ToArray() }), cancel)).Problem;

    /// <summary>Asks the server to delete what this installation sent.</summary>
    public static async Task<string?> ForgetAsync(string installId, CancellationToken cancel) =>
        (await PostAsync("/windows/forget", $"{{\"install\":\"{installId}\"}}", cancel)).Problem;

    /// <summary>The periods the server holds for a character of this installation (name and server, with the installation's secret id: the server gives them
    /// only to an installation that sent periods of that character): {"windows":[...]}, numbers only (docs/PROTOCOL.md §5).</summary>
    public static Task<(string? Body, string? Problem)> FetchCharacterAsync(string name, int server, string installId, CancellationToken cancel) =>
        PostAsync("/windows/of", JsonSerializer.Serialize(new { n = name, s = server, install = installId }), cancel);

    private static async Task<(string? Body, string? Problem)> PostAsync(string path, string json, CancellationToken cancel)
    {
        if (!DataUploader.Configured) return (null, "받는 서버 정보가 이 버전에 들어 있지 않습니다.");
        try
        {
            using var http = DataUploader.NewClient(TimeSpan.FromSeconds(path == "/contact" ? 180 : 60));
            using var request = new HttpRequestMessage(HttpMethod.Post, DataUploader.Url(path))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            DataUploader.Authorize(request);
            using var response = await http.SendAsync(request, cancel);
            if (response.IsSuccessStatusCode) return (await response.Content.ReadAsStringAsync(cancel), null);
            return (null, (int)response.StatusCode switch
            {
                426 => DataUploader.NoteAndText(),
                429 => "보낼 수 있는 횟수를 넘었습니다. 잠시 뒤 다시 시도합니다.",
                _ => $"서버가 받지 않았습니다 ({(int)response.StatusCode}).",
            });
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return (null, "취소했습니다."); }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
        {
            Log.Write("windows upload: " + e.Message);
            return (null, "서버에 연결하지 못했습니다. 다음에 다시 시도합니다.");
        }
    }
}
