using System.Buffers.Binary;
using Aion2Meter.Capture;
using Aion2Meter.Protocol;

/// <summary>
/// --enter-totals: my total rank points inside the 33 36 (enter world) message: found as a u64 directly followed by a u64 time
/// (Unix ms near the packet's time); compared with the neighbouring 73 E2 totals.
/// </summary>
static class EnterTotals
{
    public static void Run(IEnumerable<string> paths, TextWriter output)
    {
        foreach (var path in paths)
        {
            var flows = new Dictionary<FlowKey, TcpFlow>();
            var list = new List<(DateTimeOffset T, byte[] D)>();
            foreach (var frame in PcapReader.Read(path))
            {
                var seg = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
                if (seg is null) continue;
                if (!flows.TryGetValue(seg.Flow, out var flow)) flows[seg.Flow] = flow = new TcpFlow();
                foreach (var m in flow.Feed(seg).Messages) list.Add((frame.Time, m));
            }
            for (int i = 0; i < list.Count; i++)
            {
                var (t, d) = list[i];
                if (d is not [0x33, 0x36, ..] || d.Length < 256) continue;
                var enter = MessageParser.Parse(d) as EnterWorldMessage;
                string Near(int dir)
                {
                    for (int j = i + dir; j >= 0 && j < list.Count; j += dir)
                        if (MessageParser.Parse(list[j].D) is PointMessage p) return $"{p.Total} ({(list[j].T - t).TotalSeconds:0.0}s)";
                    return "-";
                }
                var found = Candidates(d, t).ToList();
                output.WriteLine($"{Path.GetFileName(path)} {t.ToOffset(TimeSpan.FromHours(9)):HH:mm:ss} len={d.Length} {enter?.Name} candidates=[{string.Join(", ", found.Select(f => $"{f.Value}@{f.At}(-{d.Length - f.At})"))}] prev={Near(-1)} next={Near(1)}");
            }
        }
    }

    public static IEnumerable<(int At, ulong Value)> Candidates(byte[] d, DateTimeOffset packetTime)
    {
        // The total follows a tag ending in f4 cb 1f (21 f4 cb 1f, 24 f4 cb 1f seen), after a long run of zero bytes.
        for (int at = 4; at + 12 <= d.Length; at++)
        {
            if (d[at] != 0xf4 || d[at + 1] != 0xcb || d[at + 2] != 0x1f) continue;
            ulong v = BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(at + 3));
            if (v is >= 1_000 and < 100_000_000_000) yield return (at + 3, v);
        }
    }
}

static class EnterHex
{
    /// <summary>--enter-hex &lt;pcap&gt; &lt;time HH:mm:ss&gt; &lt;value&gt;: bytes around a u64 value inside the 33 36 message at that time.</summary>
    public static void Run(string path, string time, ulong value, TextWriter output)
    {
        var flows = new Dictionary<FlowKey, TcpFlow>();
        foreach (var frame in PcapReader.Read(path))
        {
            var seg = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
            if (seg is null) continue;
            if (!flows.TryGetValue(seg.Flow, out var flow)) flows[seg.Flow] = flow = new TcpFlow();
            foreach (var m in flow.Feed(seg).Messages)
            {
                if (m is not [0x33, 0x36, ..] || m.Length < 256 || frame.Time.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss") != time) continue;
                var needle = BitConverter.GetBytes(value);
                int at = m.AsSpan().IndexOf(needle);
                output.WriteLine($"len={m.Length} at={at}");
                if (at >= 0) output.WriteLine(Convert.ToHexString(m.AsSpan(Math.Max(0, at - 48), Math.Min(m.Length - Math.Max(0, at - 48), 120))).ToLowerInvariant());
            }
        }
    }
}

static class FullHex
{
    /// <summary>--hex &lt;pcap&gt; &lt;op like "00 92"&gt; [time prefix]: the whole bytes of the messages with that opcode.</summary>
    public static void Run(string path, string op, string? timePrefix, TextWriter output)
    {
        byte[] want = Convert.FromHexString(op.Replace(" ", ""));
        var flows = new Dictionary<FlowKey, TcpFlow>();
        foreach (var frame in PcapReader.Read(path))
        {
            var seg = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
            if (seg is null) continue;
            if (!flows.TryGetValue(seg.Flow, out var flow)) flows[seg.Flow] = flow = new TcpFlow();
            foreach (var m in flow.Feed(seg).Messages)
            {
                if (m.Length < 2 || m[0] != want[0] || m[1] != want[1]) continue;
                string t = frame.Time.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss.fff");
                if (timePrefix is not null && !t.StartsWith(timePrefix)) continue;
                output.WriteLine($"{t} #{seg.Flow.DestinationPort} len={m.Length}");
                output.WriteLine(Convert.ToHexString(m).ToLowerInvariant());
            }
        }
    }
}
