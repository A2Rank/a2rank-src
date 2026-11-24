using Aion2Meter.Capture;
using Aion2Meter.Protocol;

/// <summary>
/// --skills: my damage (and the damage I took) per skill key in a recording (docs/PROTOCOL.md §3.6). "Me" = every entity from an
/// enter-world message. Unnamed skills show their key: add them to Skills.cs once a combat-log line tells the name.
/// </summary>
static class SkillSummary
{
    private sealed class Sum
    {
        public int Hits;
        public long Total, Main;
    }

    public static void Run(string path, TextWriter output)
    {
        var flows = new Dictionary<FlowKey, TcpFlow>();
        var me = new HashSet<uint>();
        var dealt = new Dictionary<uint, Sum>();
        var taken = new Dictionary<uint, Sum>();
        foreach (var frame in PcapReader.Read(path))
        {
            var segment = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
            if (segment is null) continue;
            if (!flows.TryGetValue(segment.Flow, out var flow)) flows[segment.Flow] = flow = new TcpFlow();
            foreach (var raw in flow.Feed(segment).Messages)
            {
                switch (MessageParser.Parse(raw))
                {
                    case EnterWorldMessage w:
                        me.Add(w.Entity);
                        break;
                    case DamageMessage d when (d.Flags & 0x02) != 0 && d.Source != d.Target:
                        var table = me.Contains(d.Source) ? dealt : me.Contains(d.Target) ? taken : null;
                        if (table is null) break;
                        if (!table.TryGetValue(d.SkillKey, out var s)) table[d.SkillKey] = s = new Sum();
                        s.Hits++;
                        s.Total += d.Total;
                        s.Main += d.Main;
                        break;
                }
            }
        }
        Print(output, "damage I dealt (to anyone), per skill", dealt);
        Print(output, "damage I took, per skill of the attacker", taken);
    }

    private static void Print(TextWriter output, string title, Dictionary<uint, Sum> table)
    {
        long all = table.Values.Sum(s => s.Total);
        output.WriteLine($"\n{title}: {table.Values.Sum(s => s.Hits):N0} hits, {all:N0} total");
        output.WriteLine("key   skill                      hits        total   share   avg/hit");
        foreach (var (key, s) in table.OrderByDescending(x => x.Value.Total).Take(25))
            output.WriteLine($"{key:x4}  {Skills.Name(key),-22} {s.Hits,7} {s.Total,12:N0} {100.0 * s.Total / Math.Max(1, all),6:0.0}% {s.Total / Math.Max(1, s.Hits),9:N0}");
    }
}
