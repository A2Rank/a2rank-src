using System.Buffers.Binary;
using Aion2Meter.Capture;
using Aion2Meter.Protocol;

/// <summary>
/// --find-totals: looks for my rank-point total (as u64 / u32 little endian) in messages other than 73 E2, to find a way
/// to know the total before the first change (the first change after a login is otherwise only a baseline).
/// </summary>
static class TotalSearch
{
    public static void Run(IEnumerable<string> paths, TextWriter output)
    {
        foreach (var path in paths)
        {
            var msgs = new List<(DateTimeOffset Time, FlowKey Flow, byte[] Data)>();
            var flows = new Dictionary<FlowKey, TcpFlow>();
            foreach (var frame in PcapReader.Read(path))
            {
                var seg = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
                if (seg is null) continue;
                if (!flows.TryGetValue(seg.Flow, out var flow)) flows[seg.Flow] = flow = new TcpFlow();
                foreach (var m in flow.Feed(seg).Messages) msgs.Add((frame.Time, seg.Flow, m));
            }
            var totals = msgs.Select(m => MessageParser.Parse(m.Data)).OfType<PointMessage>().Select(p => p.Total).Distinct().ToList();
            output.WriteLine($"== {Path.GetFileName(path)}: {msgs.Count} messages, {totals.Count} distinct totals");
            if (totals.Count == 0) continue;
            var found = new Dictionary<string, int>();
            int shown = 0;
            DateTimeOffset? firstPoint = msgs.FirstOrDefault(m => m.Data is [0x73, 0xE2, ..]).Time;
            foreach (var (time, flow, data) in msgs)
            {
                if (data.Length < 10 || data is [0x73, 0xE2, ..]) continue;
                foreach (ulong t in totals)
                {
                    var needle = new byte[8];
                    BinaryPrimitives.WriteUInt64LittleEndian(needle, t);
                    int at = data.AsSpan().IndexOf(needle);
                    if (at < 0) continue;
                    string key = $"{data[0]:x2} {data[1]:x2} @{at}";
                    found[key] = found.GetValueOrDefault(key) + 1;
                    if (shown++ < 12)
                        output.WriteLine($"  {time.ToOffset(TimeSpan.FromHours(9)):HH:mm:ss.fff} op={key} len={data.Length} total={t}{(time < firstPoint ? "  (before the first point message)" : "")}");
                }
            }
            foreach (var (k, n) in found.OrderByDescending(f => f.Value)) output.WriteLine($"  {k}: {n}");
        }
    }
}
