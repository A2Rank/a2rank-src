using System.Buffers.Binary;
using Aion2Meter.Capture;
using Aion2Meter.Protocol;

/// <summary>
/// --near-points: which messages arrive around rank point gains, and which of them carry the entity number
/// of the death that explains the gain. Looks for messages that could tie points to a victim directly.
/// </summary>
static class NearPoints
{
    private sealed record Msg(DateTimeOffset Time, byte[] Data);

    public static void Run(IEnumerable<string> paths, TextWriter output)
    {
        var aroundPoints = new Dictionary<string, int>();   // opcode -> points that had it within ±1 s
        var withVictim = new Dictionary<string, int>();     // opcode -> points where it contained the victim number
        var examples = new Dictionary<string, string>();
        int points = 0, explained = 0;
        foreach (var path in paths)
        {
            var messages = Read(path);
            ulong? last = null;
            for (int i = 0; i < messages.Count; i++)
            {
                if (MessageParser.Parse(messages[i].Data) is not PointMessage p) continue;
                bool gain = last is ulong l && p.Total > l;
                last = p.Total;
                if (!gain) continue;
                points++;
                var t = messages[i].Time;
                var window = messages.Where(m => Math.Abs((m.Time - t).TotalSeconds) <= 1.0).ToList();
                var victims = window.Where(m => m.Time <= t && (t - m.Time).TotalSeconds <= 0.35)
                    .Select(m => MessageParser.Parse(m.Data)).OfType<DeathMessage>().Select(d => d.Victim).Distinct().ToList();
                if (victims.Count > 0) explained++;
                foreach (var op in window.Select(m => Op(m.Data)).Distinct()) aroundPoints[op] = aroundPoints.GetValueOrDefault(op) + 1;
                foreach (var m in window)
                {
                    string op = Op(m.Data);
                    if (op is "04 8d" or "73 e2") continue;
                    var hit = victims.FirstOrDefault(v => Contains(m.Data, v));
                    if (hit == 0) continue;
                    withVictim[op] = withVictim.GetValueOrDefault(op) + 1;
                    examples.TryAdd(op, $"victim={hit} dt={(m.Time - t).TotalSeconds:+0.000;-0.000} len={m.Data.Length} " +
                                        Convert.ToHexString(m.Data.AsSpan(0, Math.Min(64, m.Data.Length))).ToLowerInvariant());
                }
            }
        }
        output.WriteLine($"point gains: {points}, with a death in the 0.35 s before: {explained}");
        output.WriteLine("---- opcodes containing the victim's number (per point)");
        foreach (var (op, n) in withVictim.OrderByDescending(x => x.Value))
            output.WriteLine($"{op}  {n}/{aroundPoints.GetValueOrDefault(op)} around points   e.g. {examples[op]}");
        output.WriteLine("---- opcodes seen within ±1 s of most point gains");
        foreach (var (op, n) in aroundPoints.Where(x => x.Value * 2 >= points).OrderByDescending(x => x.Value))
            output.WriteLine($"{op}  {n}/{points}");
    }

    private static List<Msg> Read(string path)
    {
        var flows = new Dictionary<FlowKey, TcpFlow>();
        var list = new List<Msg>();
        foreach (var f in PcapReader.Read(path))
        {
            if (PacketParser.Parse(f.Data, f.LinkType, f.Time) is not { } s) continue;
            if (!flows.TryGetValue(s.Flow, out var flow)) flows[s.Flow] = flow = new TcpFlow();
            foreach (var m in flow.Feed(s).Messages) list.Add(new Msg(f.Time, m));
        }
        return list;
    }

    private static string Op(byte[] m) => m.Length >= 2 ? $"{m[0]:x2} {m[1]:x2}" : $"{m[0]:x2}";

    // The number as a varint (how deaths carry it) or as a little-endian u32.
    private static bool Contains(byte[] data, uint value)
    {
        var varint = new List<byte>();
        uint v = value;
        do
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            varint.Add(v != 0 ? (byte)(b | 0x80) : b);
        } while (v != 0);
        Span<byte> u32 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(u32, value);
        var span = data.AsSpan(2);
        return (varint.Count >= 2 && span.IndexOf(varint.ToArray()) >= 0) || span.IndexOf(u32) >= 0;
    }
}
