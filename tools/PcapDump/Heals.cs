using Aion2Meter.Capture;
using Aion2Meter.Protocol;

/// <summary>
/// --heals: the recovery messages (04 38 without the damage flag 0x02) of a recording that go to me: per skill key and stamp (low half of the u32)
/// the number, the sum and the smallest/biggest amount, and who sent them (stamp-low ffff = a heal-over-time tick, 05 38 type 0B, to me). Life, mana (정신력) and action points (행동력) are all "recoveries":
/// this shows which stamps belong to which.
/// Usage: --heals <pcap> [HH:mm start] [HH:mm end]
/// </summary>
static class Heals
{
    public static void Run(string path, string from, string to, TextWriter output)
    {
        var flows = new Dictionary<FlowKey, TcpFlow>();
        uint me = 0;
        var sums = new Dictionary<(uint Key, uint Low, bool Self, bool Foreign), (int N, long Sum, long Min, long Max)>();
        foreach (var frame in PcapReader.Read(path))
        {
            var segment = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
            if (segment is null) continue;
            if (!flows.TryGetValue(segment.Flow, out var flow)) flows[segment.Flow] = flow = new TcpFlow();
            foreach (var raw in flow.Feed(segment).Messages)
            {
                var message = MessageParser.Parse(raw);
                if (message is EnterWorldMessage entered) me = entered.Entity;
                if (message is PeriodicMessage { Damage: false } p)
                {
                    var pk = (p.SkillKey, 0xFFFFu, p.Source == me, p.Source != me);
                    if (p.Target == me) { var ps = sums.GetValueOrDefault(pk, (0, 0L, long.MaxValue, 0L)); sums[pk] = (ps.Item1 + 1, ps.Item2 + p.Amount, Math.Min(ps.Item3, p.Amount), Math.Max(ps.Item4, p.Amount)); }
                    continue;
                }
                if (message is not DamageMessage d || (d.Flags & 0x02) != 0 || d.Target != me) continue;
                string t = frame.Time.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm");
                if (string.Compare(t, from, StringComparison.Ordinal) < 0 || string.Compare(t, to, StringComparison.Ordinal) > 0) continue;
                var k = (d.SkillKey, d.Stamp & 0xFFFF, d.Source == me, d.Source != me);
                var s = sums.GetValueOrDefault(k, (0, 0L, long.MaxValue, 0L));
                sums[k] = (s.Item1 + 1, s.Item2 + d.Total, Math.Min(s.Item3, d.Total), Math.Max(s.Item4, d.Total));
            }
        }
        output.WriteLine("key   name                        stamp-low  from      count        sum        min        max");
        foreach (var (k, s) in sums.OrderBy(x => x.Key.Key).ThenBy(x => x.Key.Low))
            output.WriteLine($"{k.Key:x4}  {Skills.Name(k.Key),-24} {k.Low,9:x4}  {(k.Self ? "me" : "other"),-6} {s.N,6} {s.Sum,10:N0} {s.Min,10:N0} {s.Max,10:N0}");
    }
}
