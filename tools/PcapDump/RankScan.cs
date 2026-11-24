using Aion2Meter.Capture;
using Aion2Meter.Protocol;

/// <summary>
/// --rank-scan: what ranking messages (55 8D list, 57 8D personal) the recordings in a folder (or the given files) hold: when they come,
/// for which reset time (deadline), how many entries, and - for ALL-class lists of consecutive resets - the points each player gained
/// between them (the game's own hourly numbers, to check data players send in against).
/// Usage: --rank-scan <folder-or-pcap ...>
/// </summary>
static class RankScan
{
    private sealed record Seen(string File, DateTimeOffset Time, RankingMessage Msg);

    public static void Run(IEnumerable<string> paths, TextWriter output)
    {
        var files = paths.SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p, "*.pcap") : [p]).OrderBy(f => f).ToList();
        var seen = new List<Seen>();
        foreach (var file in files)
        {
            var flows = new Dictionary<FlowKey, TcpFlow>();
            try
            {
                foreach (var frame in PcapReader.Read(file))
                {
                    var segment = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
                    if (segment is null) continue;
                    if (!flows.TryGetValue(segment.Flow, out var flow)) flows[segment.Flow] = flow = new TcpFlow();
                    foreach (var raw in flow.Feed(segment).Messages)
                        if (MessageParser.Parse(raw) is RankingMessage m) seen.Add(new Seen(Path.GetFileName(file), frame.Time, m));
                }
            }
            catch (IOException)
            {
                output.WriteLine($"skipped (in use by the meter): {Path.GetFileName(file)}");
            }
        }
        string Tm(DateTimeOffset t) => t.ToOffset(TimeSpan.FromHours(9)).ToString("MM-dd HH:mm:ss");
        output.WriteLine($"{files.Count} recordings, {seen.Count(s => s.Msg.IsList)} lists, {seen.Count(s => !s.Msg.IsList)} personal messages\n");

        output.WriteLine("lists (55 8D):  received            filter  entries  reset(deadline)    rank1 score       last rank/score");
        foreach (var s in seen.Where(s => s.Msg.IsList))
        {
            var e = s.Msg.Entries;
            if (e.Count == 0) continue;
            output.WriteLine($"  {Tm(s.Time)}  {s.Msg.Filter,6}  {e.Count,7}  {Tm(s.Msg.Deadline)}   {e[0].Score,12:N0}   #{e[^1].Rank} {e[^1].Score:N0}   ({s.File})");
        }

        output.WriteLine("\npersonal (57 8D) per reset time (how many came, own rank/score):");
        foreach (var g in seen.Where(s => !s.Msg.IsList && s.Msg.Filter == 0).GroupBy(s => s.Msg.Deadline).OrderBy(g => g.Key))
        {
            var last = g.Last().Msg.Entries[0];
            output.WriteLine($"  reset {Tm(g.Key)}: {g.Count()} messages, #{last.Rank} {last.Score:N0} {last.Name}");
        }

        // ALL-class lists, the last one seen per reset time; points gained from one reset to the next per player
        var byReset = seen.Where(s => s.Msg.IsList && s.Msg.Filter == 0 && s.Msg.Entries.Count > 0)
            .GroupBy(s => s.Msg.Deadline).OrderBy(g => g.Key)
            .Select(g => (Reset: g.Key, List: g.Last().Msg.Entries)).ToList();
        output.WriteLine("\nhourly gain between two ALL lists of consecutive resets (top 10 of the first list):");
        for (int i = 1; i < byReset.Count; i++)
        {
            var (resetA, a) = byReset[i - 1];
            var (resetB, b) = byReset[i];
            var gapHours = (resetB - resetA).TotalHours;
            output.WriteLine($"  {Tm(resetA)} -> {Tm(resetB)} ({gapHours:0.#} h)");
            var bById = b.ToDictionary(x => x.CharacterId);
            foreach (var x in a.Take(10))
                output.WriteLine(bById.TryGetValue(x.CharacterId, out var y)
                    ? $"     #{x.Rank,-4} {x.Name,-14} {x.Score,12:N0} -> {y.Score,12:N0}  gain {(long)y.Score - (long)x.Score,8:N0}"
                    : $"     #{x.Rank,-4} {x.Name,-14} {x.Score,12:N0} -> (not in the later list)");
        }
    }
}
