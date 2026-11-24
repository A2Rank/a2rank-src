using Aion2Meter.Core;
using System.Text.RegularExpressions;
using Aion2Meter.Capture;
using Aion2Meter.Protocol;

/// <summary>
/// --skills-table: matches a per-skill table read off a screenshot (skill name, number of hits, total damage - for example an
/// in-game damage list) with the skill keys of a recording. The table covers a time slice, so the recording is cut into bursts
/// (cut at pauses of 5, 15, 30, 60 and 120 seconds) and each row is looked for in every burst: a key whose hit count and total
/// both match is "sure" (two keys that together match are "sure, two keys": one skill with two keys, like 천벌); a key whose total
/// alone matches is "total only" (a name from it stays provisional). Damage to monsters from every source counts, and so does a player's damage to a player (summons have their own
/// source), the extra lines that follow a hit count with its Total.
/// Usage: --skills-table <file.pcap> <table.txt>   (one row per line: name hits total, commas and % allowed)
/// </summary>
static class SkillsTable
{
    private sealed class Sum
    {
        public int Hits;
        public long Total, Main;
        public readonly List<long> Dot = [];
        public HitStats Stats = HitStats.Empty;   // how the hits landed (critical, perfect, ...) as the app counts them   // damage-over-time ticks (05 38 type 0A) of this key from the start of the piece on, in time order
    }

    private sealed record Row(string Name, int Hits, long Total, int[]? Percent = null);
    private sealed record Hit(DateTimeOffset Time, uint Key, long Main, long Total, DamageMessage Message);

    public static void Run(string pcap, string tablePath, TextWriter output)
    {
        var rows = new List<Row>();
        foreach (var line in File.ReadAllLines(tablePath))
        {
            // name hits total [CRI PERF DOUB FRNT BACK MULT [BLOC IMMU]]: the percentages are the columns of the in-game table
            var m = Regex.Match(line.Trim(), @"^(?<name>.+?)\s+(?<hits>\d+)\s+(?<total>[\d,]{4,})(?<pct>(\s+\d+){6,8})?(\s*\(\d+%\))?$");
            if (m.Success) rows.Add(new Row(m.Groups["name"].Value, int.Parse(m.Groups["hits"].Value), long.Parse(m.Groups["total"].Value.Replace(",", "")),
                m.Groups["pct"].Success ? [.. m.Groups["pct"].Value.Split(" ", StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)] : null));
            else if (line.Trim().Length > 0) output.WriteLine($"(skipped line: {line.Trim()})");
        }

        var hits = new List<Hit>();
        var hitTargets = new List<uint>();   // the target of each hit, in the order of `hits` before it is sorted
        var ticks = new List<(DateTimeOffset Time, uint Key, long Amount)>();
        var flows = new Dictionary<FlowKey, TcpFlow>();
        var templates = new Dictionary<uint, uint>();
        var owners = new Dictionary<uint, uint>();
        var lastCast = new Dictionary<(uint Source, uint Key), DateTimeOffset>();
        uint me = 0;
        foreach (var frame in PcapReader.Read(pcap))
        {
            var segment = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
            if (segment is null) continue;
            if (!flows.TryGetValue(segment.Flow, out var flow)) flows[segment.Flow] = flow = new TcpFlow();
            foreach (var raw in flow.Feed(segment).Messages)
            {
                var message = MessageParser.Parse(raw);
                // Only what I and my summons do (the table is mine): "me" is the entity of the last enter-world message; a summon names its owner's entity.
                bool Mine(uint source) => me == 0 || source == me || owners.GetValueOrDefault(source) == me;
                if (raw.Length >= 14 && raw[0] == 0x05 && raw[1] == 0x38)
                {
                    var pr = new ByteReader(raw, 2);
                    pr.Varint();
                    if (pr.Byte() == 0x0A)
                    {
                        uint tickSource = pr.Varint();
                        pr.Varint();
                        uint stamp = pr.UInt32();
                        if (Mine(tickSource)) ticks.Add((frame.Time, stamp >> 16, pr.Varint()));
                    }
                }
                if (message is EnterWorldMessage entered) me = entered.Entity;
                // Flag 0C: a fixed extra amount of a skill (390 with 흡인, 강습 낙인, 강습 공포): in the table total, not a hit; counted like a tick.
                if (message is DamageMessage { Flags: 0x0C } extra && Mine(extra.Source)) ticks.Add((frame.Time, extra.SkillKey, extra.Total));
                if (message is SpawnMessage { Template: not 0 } spawned) { templates[spawned.Entity] = spawned.Template; owners[spawned.Entity] = spawned.OwnerEntity; }
                // The elemental spirits share skill numbers: their kind (from the spawn message) tells them apart.
                else if (message is DamageMessage d && d.Flags != 0x0C && ((d.Flags & 0x02) != 0 || ((d.Flags & 0x04) != 0 && !Entities.IsPlayer(d.Target))) && d.Source != d.Target && (Entities.IsPlayer(d.Source) || !Entities.IsPlayer(d.Target)) && Mine(d.Source))
                {
                    uint key = Skills.SpiritKey(templates.GetValueOrDefault(d.Source), d.SkillKey);
                    // one cast, two skills under one key (루미엘의 공간 / 나락): the second hit shortly after the first is the other skill
                    if (Skills.FollowUps.TryGetValue(key, out var followUp))
                    {
                        if (lastCast.TryGetValue((d.Source, key), out var first) && frame.Time - first >= TimeSpan.Zero && frame.Time - first <= Skills.FollowUpWindow) key = followUp;
                        else lastCast[(d.Source, key)] = frame.Time;
                    }
                    hits.Add(new Hit(frame.Time, key, d.Main, d.Total, d));
                    hitTargets.Add(d.Target);
                }
            }
        }
        // The table is about one target (the training dummy): when I hurt several monsters, only the one that took the most counts (a skill that
        // splashes onto a second monster would otherwise have more hits and damage than the table shows).
        var npcTargets = hits.Select((h, i) => (h, T: hitTargets[i])).Where(x => !Entities.IsPlayer(x.T)).GroupBy(x => x.T).OrderByDescending(g => g.Sum(x => x.h.Total)).ToList();
        if (npcTargets.Count > 1)
        {
            uint main = npcTargets[0].Key;
            output.WriteLine($"(hits on {npcTargets.Count} monsters: only those on the one that took the most, #{main}, are counted)");
            hits = hits.Where((h, i) => hitTargets[i] == main || Entities.IsPlayer(hitTargets[i])).ToList();
        }
        hits.Sort((a, b) => a.Time.CompareTo(b.Time));

        // A fight has pauses of different length (a cooldown wait of 20 seconds is normal), so the recording is cut at several
        // pause lengths; every distinct piece is a candidate for what the table shows.
        var bursts = new List<List<Hit>>();
        foreach (int gap in new[] { 5, 15, 30, 60, 120 })
        {
            var cut = new List<List<Hit>>();
            foreach (var h in hits)
            {
                if (cut.Count == 0 || (h.Time - cut[^1][^1].Time).TotalSeconds > gap) cut.Add([]);
                cut[^1].Add(h);
            }
            foreach (var c in cut)
                if (!bursts.Any(b => b.Count == c.Count && b[0].Time == c[0].Time)) bursts.Add(c);
        }
        string Tm(DateTimeOffset t) => t.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss");
        output.WriteLine($"{rows.Count} rows, {hits.Count} damage hits in {bursts.Count} candidate piece(s)");

        // The burst that explains the most rows is the one the screenshot shows.
        // The table was taken at some moment: the last hits of a fight may come after it. So every piece is also tried cut after each hit
        // time, and the cut that explains the most rows wins (the longest one when equal).
        IEnumerable<List<Hit>> Candidates() =>
            bursts.SelectMany(b => Enumerable.Range(0, b.Count).Where(i => i == b.Count - 1 || b[i].Time != b[i + 1].Time).Select(i => b.Take(i + 1).ToList()));
        var best = Candidates().Select(b => (Burst: b, Sums: Sums(b, ticks)))
            .OrderByDescending(x => rows.Count(r => Find(r, x.Sums).Any(f => f.Sure))).ThenByDescending(x => x.Burst.Count).FirstOrDefault();
        if (best.Burst is null) { output.WriteLine("no damage in the recording"); return; }
        output.WriteLine($"best piece: {Tm(best.Burst[0].Time)}-{Tm(best.Burst[^1].Time)} (the table was taken right after the last hit) explains {rows.Count(r => Find(r, best.Sums).Any(f => f.Sure))} of {rows.Count} rows\n");

        var claimed = rows.SelectMany(r => Find(r, best.Sums)).Where(f => f.Sure).SelectMany(f => f.Keys).ToHashSet();
        foreach (var row in rows)
        {
            var found = Find(row, best.Sums);
            if (found.Count > 0 && !found[0].Sure) found = found.Where(f => !f.Keys.Any(claimed.Contains)).ToList();   // a key that another row matches for sure is not this one
            output.Write($"{row.Name,-14} {row.Hits,3} {row.Total,11:N0}  ");
            if (found.Count == 0) { output.WriteLine("-> no key (the table has damage this recording does not show)"); continue; }
            output.WriteLine();
            foreach (var (keys, sure, how) in found)
            {
                string names = string.Join(" + ", keys.Select(k => $"{k:x4} [{(Skills.IsKnown(k) ? Skills.Name(k) : "no name yet")}]"));
                bool same = keys.All(k => Skills.IsKnown(k) && Skills.Name(k).StartsWith(row.Name));   // "신성한 기운 (소환수)" is the summon of "신성한 기운"
                string verdict = keys.All(k => !Skills.IsKnown(k)) ? (Skills.KeysNamed(row.Name).ToList() is { Count: > 0 } sameName ? $"NAME EXISTS under {string.Join(", ", sameName.Select(k => k.ToString("x4")))}: a variant, make it an alias" : "NEW NAME") : same ? "name already right" : "CONFLICT with the current name";
                output.WriteLine($"    {(sure ? "sure" : "total only")} ({how}): {names}  -> {verdict}");
                if (sure && row.Percent is { Length: >= 6 } pct && keys.All(best.Sums.ContainsKey))
                {
                    var st = keys.Select(k => best.Sums[k].Stats).Aggregate((HitStats?)null, HitStats.Merge)!;
                    int P(int n) => (int)Math.Round(n * 100.0 / st.Hits, MidpointRounding.AwayFromZero);
                    string[] columns = ["CRI", "PERF", "DOUB", "FRNT", "BACK", "MULT"];
                    int[] app = [P(st.Critical), P(st.Perfect), P(st.Smash), P(st.Front), P(st.Back), P(st.Multi)];
                    var bad = Enumerable.Range(0, 6).Where(i => app[i] != pct[i]).Select(i => $"{columns[i]} table {pct[i]} app {app[i]}").ToList();
                    output.WriteLine($"        hit kinds (CRI/PERF/DOUB[=강타]/FRNT/BACK/MULT): table {string.Join("/", pct.Take(6))}  app {string.Join("/", app)}  " + (bad.Count == 0 ? "-> all equal" : "-> DIFFERENT: " + string.Join(", ", bad)));
                }
            }
        }

        // Keys of the piece that no row claimed for sure: the rows still without a key are among them (a name can then be read
        // from hit counts and totals that lie a little below the table's because of damage over time).
        output.WriteLine("\nkeys no row matched for sure (hits, total):");
        foreach (var (key, s) in best.Sums.Where(x => !claimed.Contains(x.Key)).OrderByDescending(x => x.Value.Total).Take(40))
            output.WriteLine($"    {key:x4} [{(Skills.IsKnown(key) ? Skills.Name(key) : "no name yet")}]  {s.Hits} hits  {s.Total:N0}");
    }

    private static Dictionary<uint, Sum> Sums(List<Hit> burst, List<(DateTimeOffset Time, uint Key, long Amount)> ticks)
    {
        var sums = new Dictionary<uint, Sum>();
        foreach (var h in burst)
        {
            if (!sums.TryGetValue(h.Key, out var s)) sums[h.Key] = s = new Sum();
            s.Hits++;
            s.Total += h.Total;
            s.Main += h.Main;
            s.Stats = s.Stats.Add(h.Message);
        }
        foreach (var t in ticks.Where(t => t.Time >= burst[0].Time).OrderBy(t => t.Time))
            {
                // The ticks of a debuff usually carry the key of the hit; 협공: 저주 and 부식 have the key one lower (6033/6034, 6042/6043).
                if (sums.TryGetValue(t.Key, out var owner)) owner.Dot.Add(t.Amount);
                else if (sums.TryGetValue(t.Key + 1, out var next)) next.Dot.Add(t.Amount);
            }
        return sums;
    }

    /// <summary>How many of the first ticks add up to exactly <paramref name="gap"/> (0 when none do): what a table taken while the damage over time was still running adds to the hits.</summary>
    private static int DotPrefix(Sum s, long gap)
    {
        long sum = 0;
        for (int i = 0; i < s.Dot.Count; i++)
        {
            sum += s.Dot[i];
            if (sum == gap) return i + 1;
        }
        return 0;
    }

    private static List<(uint[] Keys, bool Sure, string How)> Find(Row row, Dictionary<uint, Sum> sums)
    {
        var found = new List<(uint[], bool, string)>();
        var dot = new List<(uint[], bool, string)>();
        foreach (var (key, s) in sums)
        {
            if (s.Hits == row.Hits && (s.Total == row.Total || s.Main == row.Total)) found.Add(([key], true, "hits and total"));
            else if (s.Total == row.Total || s.Main == row.Total) found.Add(([key], false, $"total, but {s.Hits} hits"));
            else if (s.Hits == row.Hits && s.Total < row.Total && DotPrefix(s, row.Total - s.Total) is int n && n > 0) found.Add(([key], true, $"hits match, and the {n} first damage-over-time ticks (05 38) make up the rest: {row.Total - s.Total:N0}"));
            else if (s.Hits == row.Hits && s.Total < row.Total) dot.Add(([key], false, $"hits match, total {s.Total:N0} is lower: damage over time (a debuff) comes on top, a name from it stays provisional"));
        }
        if (found.Any(f => f.Item2)) return found.Where(f => f.Item2).ToList();
        if (found.Count == 0 && dot.Count > 0) return dot;
        var keys = sums.Keys.OrderBy(k => k).ToArray();
        for (int i = 0; i < keys.Length; i++)
            for (int j = i + 1; j < keys.Length; j++)
            {
                var a = sums[keys[i]];
                var b = sums[keys[j]];
                if (a.Hits + b.Hits == row.Hits && a.Total + b.Total == row.Total) found.Add(([keys[i], keys[j]], true, "two keys together"));
            }
        return found;
    }
}
