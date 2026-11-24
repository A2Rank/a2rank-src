using System.Text.Json;
using Aion2Meter.Core;
using Aion2Meter.Protocol;

namespace Aion2Meter.Tests;

/// <summary>
/// Replays the recorded battle of 2026-10-10 (testdata/, not in the repository: it contains other players' names)
/// and requires the same kill / death / unmatched results as the reviewed snapshot, record by record.
/// After an intended rule change, set AION2_UPDATE_SNAPSHOT=1, run once and review the diff of replay_snapshot.txt.
/// </summary>
public class ReplayTests
{
    private static string? Fixture(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var testdata = Path.Combine(dir.FullName, "testdata");
            if (Directory.Exists(testdata)) return Path.Combine(testdata, name);
        }
        return null;
    }

    private static DateTimeOffset At(double seconds) => DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(seconds * 1000));

    [Fact]
    public void Recorded_battle_matches_snapshot()
    {
        string? events = Fixture("replay_events.jsonl"), snapshot = Fixture("replay_snapshot.txt");
        if (events is null || snapshot is null || !File.Exists(events)) return; // fixture is local only

        var matcher = new KillMatcher();
        matcher.SetSelf("아라", 1);
        var actual = new List<string>();
        string Line(string kind, double at, KillRecord r) =>
            $"{kind} at={at:F3} {r.Name} {r.Server} {r.Delta:+#;-#;0} time={r.Time.ToUnixTimeMilliseconds() / 1000.0:F3}";

        foreach (var line in File.ReadLines(events))
        {
            var e = JsonDocument.Parse(line).RootElement;
            double t = e.GetProperty("t").GetDouble();
            switch (e.GetProperty("kind").GetString())
            {
                case "identity":
                    var id = new IdentityMessage(e.GetProperty("entity").GetUInt32(), e.GetProperty("name").GetString()!, 1,
                        e.GetProperty("race").GetInt32(), e.GetProperty("server").GetUInt16());
                    foreach (var r in matcher.OnIdentity(id)) actual.Add(Line("late", t, r));
                    break;
                case "death":
                    matcher.OnDeath(new DeathMessage(e.GetProperty("victim").GetUInt32(), e.GetProperty("killer").GetUInt32(),
                        e.GetProperty("server").GetUInt16(), e.GetProperty("name").GetString()!, e.GetProperty("race").GetInt32(),
                        e.GetProperty("pvp").GetBoolean()), At(t));
                    break;
                default:
                    if (matcher.OnPoint(e.GetProperty("delta").GetInt64(), At(t)) is { } k)
                        actual.Add(Line(k.Kind.ToString().ToLowerInvariant(), t, k));
                    matcher.Expire(At(t));
                    foreach (var u in matcher.TakeUnmatched())
                        actual.Add($"unmatched at={t:F3} {u.Delta:+#;-#;0} time={u.Time.ToUnixTimeMilliseconds() / 1000.0:F3}");
                    break;
            }
        }

        if (Environment.GetEnvironmentVariable("AION2_UPDATE_SNAPSHOT") == "1" || !File.Exists(snapshot))
            File.WriteAllLines(snapshot, actual);
        var expected = File.ReadAllLines(snapshot);
        var firstDiff = Enumerable.Range(0, Math.Max(actual.Count, expected.Length))
            .FirstOrDefault(i => i >= actual.Count || i >= expected.Length || actual[i] != expected[i], -1);
        if (firstDiff >= 0)
            Assert.Fail($"first difference at #{firstDiff}: now=[{(firstDiff < actual.Count ? actual[firstDiff] : "-")}] " +
                        $"snapshot=[{(firstDiff < expected.Length ? expected[firstDiff] : "-")}] (now {actual.Count}, snapshot {expected.Length})");
    }
}
