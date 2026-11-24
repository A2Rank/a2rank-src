using System.Text.Json;

namespace Aion2Meter.Core;

/// <summary>
/// One kill, death or duel inside a window: when, what kind, points, the other player's server and race, and their name (the victim's for a
/// kill, the killer's for a death, the opponent's for a duel; empty for monsters). The name stays on this PC: <see cref="WindowRecord.ToJson"/>
/// leaves it out of what is sent to the server.
/// </summary>
public sealed record WindowKill(long Time, KillKind Kind, long Delta, int Server, int Race, string Name);

/// <summary>Points gained and lost while my party had one size.</summary>
public sealed record WindowParty(int Size, long Gain, long Loss, int Events);

/// <summary>
/// One counted period ("구간 기록") of one character: everything that was aggregated is sent, as long as rank points moved at all in it.
/// <see cref="Start"/> and <see cref="End"/> are the period's own limits (the hour, or shorter when it was cut: measuring stopped, the app
/// closed, the reset time changed); <see cref="First"/> and <see cref="Last"/> are when the first and the last change were seen and
/// <see cref="StartTotal"/>/<see cref="EndTotal"/> my total before the first and after the last change.
/// <para>
/// <see cref="Complete"/> is the foothold for checking the numbers later (a ranking, comparison with the game's own lists): the period is the
/// whole hour between two of the game's ranking resets and was measured from the first second to the last, so its totals are the totals at
/// the two resets. Only those can be compared with the game's ranking; the others are for the player's own eyes.
/// </para>
/// </summary>
public sealed record WindowRecord(
    string CharacterId, string CharacterName, int Server, ulong? RankingId,
    DateTimeOffset Start, DateTimeOffset End, int OffsetSeconds, DateTimeOffset First, DateTimeOffset Last, ulong StartTotal, ulong EndTotal,
    long Gain, long Loss, int Events, bool Aligned, bool Complete,
    IReadOnlyList<WindowParty> Parties, IReadOnlyList<WindowKill> Kills, long Unmatched)
{
    // Korean names stay readable in the JSON (the default would write them as \uXXXX).
    private static readonly JsonSerializerOptions Options = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Reads a window back from <see cref="ToJson"/> (what the queue on this PC keeps).</summary>
    public static WindowRecord FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var w = doc.RootElement;
        var c = w.GetProperty("c");
        string name = c.GetProperty("n").GetString() ?? "";
        int server = c.GetProperty("s").GetInt32();
        ulong? rankingId = c.TryGetProperty("r", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetUInt64() : null;
        DateTimeOffset Time(string key) => Kst.Of(DateTimeOffset.FromUnixTimeSeconds(w.GetProperty(key).GetInt64()));
        var parties = w.GetProperty("p").EnumerateArray().Select(p => new WindowParty(p[0].GetInt32(), p[1].GetInt64(), p[2].GetInt64(), p[3].GetInt32())).ToList();
        var kills = w.GetProperty("k").EnumerateArray().Select(k => new WindowKill(k[0].GetInt64(), (KillKind)k[1].GetInt32(), k[2].GetInt64(), k[3].GetInt32(),
            k[4].GetInt32(), k.GetArrayLength() > 5 ? k[5].GetString() ?? "" : "")).ToList();
        return new WindowRecord($"{server}:{name}", name, server, rankingId, Time("start"), Time("end"), w.GetProperty("off").GetInt32(), Time("f"), Time("l"),
            w.GetProperty("t0").GetUInt64(), w.GetProperty("t1").GetUInt64(), w.GetProperty("gain").GetInt64(), w.GetProperty("loss").GetInt64(),
            w.GetProperty("ev").GetInt32(), w.GetProperty("al").GetInt32() != 0, w.GetProperty("cp").GetInt32() != 0, parties, kills, w.GetProperty("un").GetInt64());
    }

    /// <summary>
    /// The window as JSON (schema 1; short keys, positional arrays: see docs/PROTOCOL.md §5). With <paramref name="withNames"/> (the copy kept on this
    /// PC) the kill rows end with the other player's name; without (what is sent to the server) they do not: other players' names never leave the PC.
    /// </summary>
    public string ToJson(bool withNames = true)
    {
        var o = new
        {
            c = new { n = CharacterName, s = Server, r = RankingId },
            start = Start.ToUnixTimeSeconds(),
            end = End.ToUnixTimeSeconds(),
            off = OffsetSeconds,
            f = First.ToUnixTimeSeconds(),
            l = Last.ToUnixTimeSeconds(),
            t0 = StartTotal,
            t1 = EndTotal,
            gain = Gain,
            loss = Loss,
            ev = Events,
            al = Aligned ? 1 : 0,
            cp = Complete ? 1 : 0,
            p = Parties.Select(p => new long[] { p.Size, p.Gain, p.Loss, p.Events }),
            k = Kills.Select(k => withNames ? new object[] { k.Time, (int)k.Kind, k.Delta, k.Server, k.Race, k.Name } : new object[] { k.Time, (int)k.Kind, k.Delta, k.Server, k.Race }),
            un = Unmatched,
        };
        return JsonSerializer.Serialize(o, Options);
    }
}
