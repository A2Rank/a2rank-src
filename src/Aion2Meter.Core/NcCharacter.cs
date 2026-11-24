using System.Text.Json;
using System.Text.RegularExpressions;
using Aion2Meter.Protocol;

namespace Aion2Meter.Core;

/// <summary>
/// The official character search of the game (aion2.plaync.com, "캐릭터 정보실"): the addresses the page itself uses, and picking one character out of its answer.
/// Looked up only when the user clicks a name; the name and the server go to that site then (docs/PRIVACY.md).
/// </summary>
public static partial class NcCharacter
{
    public const string Site = "https://aion2.plaync.com";

    /// <summary>The search the site's own page makes (the server code is the same number the game uses: 1019 = 이슈타르).</summary>
    public static string SearchApiUrl(string name, int server) =>
        $"{Site}/api/search/character?keyword={Uri.EscapeDataString(name)}&race={Servers.RaceOf(server)}&serverId={server}&page=1&size=10";

    /// <summary>The page of a character found by <see cref="FindCharacterId"/> (the id is already URL-encoded, as the search answers it).</summary>
    public static string DetailUrl(int server, string characterId) => $"{Site}/ko-kr/characters/{server}/{characterId}";

    /// <summary>The site's search page with the name filled in (when the character was not found directly).</summary>
    public static string SearchPageUrl(string name, int server) =>
        $"{Site}/ko-kr/characters/index?race={Servers.RaceOf(server)}&serverId={server}&keyword={Uri.EscapeDataString(name)}";

    /// <summary>The id of the character with exactly this name on this server in the answer of the search, or null (not there, or the answer is not what was expected).</summary>
    public static string? FindCharacterId(string json, string name, int server)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array) return null;
            foreach (var item in list.EnumerateArray())
            {
                string found = Tags().Replace(item.GetProperty("name").GetString() ?? "", "");   // the matching part of a name comes in <strong>
                if (found == name && item.GetProperty("serverId").GetInt32() == server && item.GetProperty("characterId").GetString() is { Length: > 0 } id) return id;
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { }
        return null;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();
}
