using System.Buffers.Binary;
using Aion2Meter.Capture;
using Aion2Meter.Protocol;

/// <summary>
/// --periodic: the over-time damage ticks of a recording (05 38 with type 0A: [target][0A][source][varint][u32 stamp, high half = skill key][amount][48][varint][01]):
/// per skill key the number of ticks and their sum. With a time prefix only those of that time are counted. Used to see what a combat analyzer
/// table adds to the hits of a key (damage over time); `list` as the third argument prints every tick.
/// </summary>
static class Periodic
{
    public static void Run(string path, string timePrefix, bool list, TextWriter output)
    {
        var flows = new Dictionary<FlowKey, TcpFlow>();
        var sums = new Dictionary<(uint Source, uint Key), (int Ticks, long Total)>();
        foreach (var frame in PcapReader.Read(path))
        {
            var segment = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
            if (segment is null) continue;
            if (!flows.TryGetValue(segment.Flow, out var flow)) flows[segment.Flow] = flow = new TcpFlow();
            foreach (var raw in flow.Feed(segment).Messages)
            {
                if (raw.Length < 14 || raw[0] != 0x05 || raw[1] != 0x38) continue;
                string t = frame.Time.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss.fff");
                if (!t.StartsWith(timePrefix)) continue;
                var r = new ByteReader(raw, 2);
                uint target = r.Varint();
                if (r.Byte() != 0x0A) continue;
                uint source = r.Varint();
                r.Varint();
                uint stamp = r.UInt32();
                uint amount = r.Varint();
                uint key = stamp >> 16;
                if (list) output.WriteLine($"{t}  #{source} -> #{target}  key {key:x4} {Skills.Name(key),-16} {amount,9:N0}");
                var s = sums.GetValueOrDefault((source, key));
                sums[(source, key)] = (s.Ticks + 1, s.Total + amount);
            }
        }
        output.WriteLine("ticks per source and skill key:");
        foreach (var ((source, key), s) in sums.OrderByDescending(x => x.Value.Total))
            output.WriteLine($"  #{source,-6} {key:x4} {Skills.Name(key),-16} {s.Ticks,4} ticks {s.Total,11:N0}");
    }
}
