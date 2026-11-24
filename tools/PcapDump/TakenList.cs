using Aion2Meter.Capture;
using Aion2Meter.Protocol;

/// <summary>--taken &lt;pcap&gt; &lt;HH:mm&gt;: every damage/recovery message that involves me in that minute, to match against the combat-log lines.</summary>
static class TakenList
{
    public static void Run(string path, string minute, TextWriter output)
    {
        var flows = new Dictionary<FlowKey, TcpFlow>();
        var me = new HashSet<uint>();
        foreach (var frame in PcapReader.Read(path))
        {
            var segment = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
            if (segment is null) continue;
            if (!flows.TryGetValue(segment.Flow, out var flow)) flows[segment.Flow] = flow = new TcpFlow();
            foreach (var raw in flow.Feed(segment).Messages)
            {
                switch (MessageParser.Parse(raw))
                {
                    case EnterWorldMessage w: me.Add(w.Entity); break;
                    case DamageMessage d when me.Contains(d.Source) || me.Contains(d.Target) || Environment.GetEnvironmentVariable("ANYONE") == "1":
                        string t = frame.Time.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss.fff");
                        if (!t.StartsWith(minute)) break;
                        string dir = d.Source == d.Target ? "heal " : me.Contains(d.Target) ? "TAKEN" : "dealt";
                        output.WriteLine($"{t} {dir} flags={d.Flags:x2} src={d.Source} tgt={d.Target} key={d.SkillKey:x4} main={d.Main} total={d.Total} extras=[{string.Join(',', d.Extras)}] stamp={d.Stamp:x8} dir={Dir(raw, d.HitIndex)} idx={d.HitIndex} tail={(raw[^2] == d.HitIndex && raw[^1] == 0 ? 0 : 1)} mods={Mods(raw)} raw={Convert.ToHexString(raw).ToLowerInvariant()}");
                        break;
                }
            }
        }
    }
    private static string Dir(byte[] raw, uint index)
    {
        for (int p = 8; p + 4 <= raw.Length; p++)
            if (raw[p] == index && raw[p + 1] == 0 && raw[p + 2] == 0 && raw[p + 3] == 0) return raw[p - 5].ToString("x2");
        return "?";
    }

    /// <summary>The bytes after the source: 4 bytes, 1 byte, a count, then the modifier byte and the two bytes after it.</summary>
    private static string Mods(byte[] raw)
    {
        int p = 2;
        for (int n = 0; n < 2; n++)           // target and (after flags + pad) source varints
        {
            if (!Varint.TryRead(raw.AsSpan(p), out _, out int len)) return "?";
            p += len;
            if (n == 0) p += 2;
        }
        p += 5;                                // 4 bytes + 1 byte
        if (p + 5 > raw.Length) return "?";
        return $"count={raw[p]:x2} F={raw[p + 1]:x2} next={raw[p + 2]:x2}{raw[p + 3]:x2}{raw[p + 4]:x2}";
    }
}

/// <summary>--hits &lt;pcap&gt; &lt;my entity&gt; &lt;from HH:mm:ss&gt; &lt;to HH:mm:ss&gt;: how the hits I took in that span landed (compare with the in-game statistics).</summary>
static class HitsSummary
{
    public static void Run(string path, uint me, string from, string to, TextWriter output)
    {
        var flows = new Dictionary<FlowKey, TcpFlow>();
        var how = Aion2Meter.Core.HitStats.Empty;
        long total = 0, regained = 0, expected = 0;
        int regenHits = 0;
        foreach (var frame in PcapReader.Read(path))
        {
            var segment = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
            if (segment is null) continue;
            if (!flows.TryGetValue(segment.Flow, out var flow)) flows[segment.Flow] = flow = new TcpFlow();
            foreach (var raw in flow.Feed(segment).Messages)
            {
                if (MessageParser.Parse(raw) is not DamageMessage d || d.Target != me || d.Source == me || (d.Flags & 0x02) == 0) continue;
                string t = frame.Time.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss");
                if (string.CompareOrdinal(t, from) < 0 || string.CompareOrdinal(t, to) > 0) continue;
                how = how.Add(d);
                total += d.Total;
                if (d.Regained > 0 || d.Mods.HasFlag(HitMods.Regen)) { regained += d.Regained; expected += (long)(d.Total * 0.2); regenHits++; }
            }
        }
        output.WriteLine($"total damage {total:N0}");
        output.WriteLine(how);
        output.WriteLine($"regen hits {regenHits}: amounts read {regained:N0}, 20% of their totals {expected:N0}");
    }
}

/// <summary>--rare &lt;pcap&gt; &lt;max count&gt; [from HH:mm:ss]: messages of opcodes that appear at most that often, with times and sizes (to find the messages of a rare event).</summary>
static class RareOps
{
    public static void Run(string path, int max, string? from, TextWriter output)
    {
        var flows = new Dictionary<FlowKey, TcpFlow>();
        var all = new List<(string Time, byte[] Data)>();
        foreach (var frame in PcapReader.Read(path))
        {
            var segment = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
            if (segment is null) continue;
            if (!flows.TryGetValue(segment.Flow, out var flow)) flows[segment.Flow] = flow = new TcpFlow();
            foreach (var m in flow.Feed(segment).Messages)
                if (m.Length >= 2) all.Add((frame.Time.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss.fff"), m));
        }
        foreach (var g in all.GroupBy(x => (x.Data[0], x.Data[1])).OrderBy(g => g.Key))
        {
            var items = g.Where(x => from is null || string.CompareOrdinal(x.Time, from) >= 0).ToList();
            if (g.Count() > max || items.Count == 0) continue;
            output.WriteLine($"op {g.Key.Item1:x2} {g.Key.Item2:x2} total={g.Count()}: " + string.Join("  ", items.Select(x => $"{x.Time[..8]}({x.Data.Length})")));
        }
    }
}

/// <summary>--grep &lt;pcap&gt; &lt;hex bytes&gt; &lt;time prefix&gt;: every message of that time containing the bytes.</summary>
static class GrepBytes
{
    public static void Run(string path, string hex, string timePrefix, TextWriter output)
    {
        var needle = Convert.FromHexString(hex);
        var flows = new Dictionary<FlowKey, TcpFlow>();
        foreach (var frame in PcapReader.Read(path))
        {
            var segment = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
            if (segment is null) continue;
            if (!flows.TryGetValue(segment.Flow, out var flow)) flows[segment.Flow] = flow = new TcpFlow();
            foreach (var m in flow.Feed(segment).Messages)
            {
                string t = frame.Time.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss.fff");
                if (!t.StartsWith(timePrefix) || m.AsSpan().IndexOf(needle) < 0) continue;
                output.WriteLine($"{t} op={m[0]:x2} {m[1]:x2} len={m.Length} {Convert.ToHexString(m).ToLowerInvariant()}");
            }
        }
    }
}

/// <summary>--by &lt;pcap&gt; &lt;HH:mm&gt; &lt;key hex&gt;: every damage message with that skill key in that minute, whoever did it.</summary>
static class ByKey
{
    public static void Run(string path, string minute, uint key, TextWriter output)
    {
        var flows = new Dictionary<FlowKey, TcpFlow>();
        foreach (var frame in PcapReader.Read(path))
        {
            var segment = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
            if (segment is null) continue;
            if (!flows.TryGetValue(segment.Flow, out var flow)) flows[segment.Flow] = flow = new TcpFlow();
            foreach (var raw in flow.Feed(segment).Messages)
            {
                if (MessageParser.Parse(raw) is not DamageMessage d || d.SkillKey != key) continue;
                string t = frame.Time.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss.fff");
                if (t.StartsWith(minute)) output.WriteLine($"{t} src={d.Source} tgt={d.Target} flags={d.Flags:x2} main={d.Main} total={d.Total} extras=[{string.Join(',', d.Extras)}]");
            }
        }
    }
}
