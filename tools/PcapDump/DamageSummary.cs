using Aion2Meter.Capture;
using Aion2Meter.Protocol;

/// <summary>
/// --damage: damage events (04 38) of a recording, summed per source -> target, with names where an identity or
/// enter-world message told them (docs/PROTOCOL.md §3.6). The first enter-world message is "me".
/// </summary>
static class DamageSummary
{
    private sealed class Sum
    {
        public int Hits;
        public long Total, Main, Extra;
        public DateTimeOffset First, Last;
    }

    public static void Run(string path, TextWriter output)
    {
        var flows = new Dictionary<FlowKey, TcpFlow>();
        var names = new Dictionary<uint, string>();
        var sums = new Dictionary<(uint Source, uint Target), Sum>();
        uint me = 0;
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
                        me = w.Entity;
                        names[w.Entity] = w.Name;
                        break;
                    case IdentityMessage i:
                        names[i.Entity] = i.Name;
                        break;
                    case DamageMessage d when (d.Flags & 0x02) != 0:   // 0x04 alone = recovery in the samples
                        var key = (d.Source, d.Target);
                        if (!sums.TryGetValue(key, out var s)) sums[key] = s = new Sum { First = frame.Time };
                        s.Hits++;
                        s.Total += d.Total;
                        s.Main += d.Main;
                        s.Extra += d.Total - d.Main;
                        s.Last = frame.Time;
                        break;
                }
            }
        }
        string Name(uint id) => (id == me ? "ME " : "") + (Entities.IsPlayer(id) ? "" : "[NPC]") + (names.TryGetValue(id, out var n) ? $"{n}#{id}" : $"#{id}");
        string Tm(DateTimeOffset t) => t.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss");
        output.WriteLine("source -> target                          hits       total        main       extra   first-last");
        foreach (var ((source, target), s) in sums.OrderByDescending(x => x.Value.Total))
            output.WriteLine($"{Name(source),-18} -> {Name(target),-18} {s.Hits,6} {s.Total,11:N0} {s.Main,11:N0} {s.Extra,11:N0}   {Tm(s.First)}-{Tm(s.Last)}");
    }
}
