using Aion2Meter.Capture;
using Aion2Meter.Protocol;

/// <summary>
/// --skills-any: for recordings without an enter-world message (data sent in by other players: "me" is unknown). Sums the
/// damage events of every player source per skill key, and with totals given on the command line lists the hits of exactly
/// that size (read off a combat-log screenshot) with their skill key, source and time, so a name can be matched to a key.
/// Usage: --skills-any <file.pcap> [total total ...]
/// </summary>
static class SkillsAny
{
    private sealed class Sum
    {
        public int Hits;
        public long Total, Max;
        public readonly HashSet<uint> Sources = [];
    }

    public static void Run(string path, IEnumerable<string> totals, TextWriter output)
    {
        var list = totals.ToList();
        bool every = list.Remove("all");
        bool raw = list.Remove("raw");     // "raw": also the damage lines the normal filter leaves out (other flags, non-player sources)   // "all": list every hit (to line a recording up with an in-game meter that counts a time slice)
        var wanted = list.Select(t => long.Parse(t.Replace(",", ""))).ToHashSet();
        var flows = new Dictionary<FlowKey, TcpFlow>();
        var sums = new Dictionary<uint, Sum>();
        var found = new List<(DateTimeOffset Time, uint Source, uint Target, uint Key, long Main, long Total, byte Flags)>();
        foreach (var frame in PcapReader.Read(path))
        {
            var segment = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
            if (segment is null) continue;
            if (!flows.TryGetValue(segment.Flow, out var flow)) flows[segment.Flow] = flow = new TcpFlow();
            foreach (var rawMessage in flow.Feed(segment).Messages)
            {
                if (MessageParser.Parse(rawMessage) is not DamageMessage d || (!raw && (d.Flags & 0x02) == 0) || d.Source == d.Target) continue;
                if (!raw && !Entities.IsPlayer(d.Source)) continue;
                if (!sums.TryGetValue(d.SkillKey, out var s)) sums[d.SkillKey] = s = new Sum();
                s.Hits++;
                s.Total += d.Total;
                s.Max = Math.Max(s.Max, d.Total);
                s.Sources.Add(d.Source);
                // The log line names the hit itself (Main); the packet's Total also holds the extra damage lines that follow it.
                if (every || wanted.Contains(d.Main) || wanted.Contains(d.Total)) found.Add((frame.Time, d.Source, d.Target, d.SkillKey, d.Main, d.Total, (byte)d.Flags));
            }
        }
        string Tm(DateTimeOffset t) => t.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss");
        output.WriteLine("key   skill                      hits        total        max  sources");
        foreach (var (key, s) in sums.OrderByDescending(x => x.Value.Total).Take(40))
            output.WriteLine($"{key:x4}  {Skills.Name(key),-22} {s.Hits,7} {s.Total,12:N0} {s.Max,10:N0}  {s.Sources.Count}");
        if (found.Count > 0)
        {
            output.WriteLine("\nhits with a total from the screenshot:");
            foreach (var f in found.OrderBy(x => x.Time))
                output.WriteLine($"{Tm(f.Time)}  main {f.Main,9:N0} total {f.Total,9:N0}  key {f.Key:x4}  {Skills.Name(f.Key),-18} source #{f.Source} -> #{f.Target}  flags {f.Flags:x2}");
        }
    }
}
