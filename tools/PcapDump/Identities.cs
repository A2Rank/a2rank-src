using System.Buffers.Binary;
using System.Text;
using Aion2Meter.Capture;
using Aion2Meter.Protocol;

/// <summary>
/// --identities: tests candidate rules for the server code in 45 36 messages (docs/PROTOCOL.md §3.3).
/// A rule is checked on the messages where the strict rule already found the server: it must agree there.
/// </summary>
static class Identities
{
    public static void Run(IEnumerable<string> paths, TextWriter output)
    {
        int agree = 0, disagree = 0, rescued = 0, stillMissing = 0, ambiguous = 0, shown = 0, disShown = 0;
        foreach (var path in paths)
        {
            var flows = new Dictionary<FlowKey, TcpFlow>();
            foreach (var frame in PcapReader.Read(path))
            {
                var segment = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
                if (segment is null) continue;
                if (!flows.TryGetValue(segment.Flow, out var flow)) flows[segment.Flow] = flow = new TcpFlow();
                foreach (var m in flow.Feed(segment).Messages)
                {
                    if (MessageParser.Parse(m) is not IdentityMessage id) continue;
                    int tail = m.AsSpan().IndexOf(Encoding.UTF8.GetBytes(id.Name)) + Encoding.UTF8.GetByteCount(id.Name);
                    var candidates = Loose(m, tail, id.Race);
                    if (candidates.Count > 1) ambiguous++;
                    if (id.Server != 0)
                    {
                        if (candidates.Count > 0 && candidates[0].Code == id.Server) agree++;
                        else
                        {
                            disagree++;
                            if (disShown++ < 6) output.WriteLine($"DISAGREE {id.Name} strict={id.Server} loose={string.Join(",", candidates)}");
                        }
                    }
                    else if (candidates.Count > 0)
                    {
                        rescued++;
                        if (shown++ < 12) output.WriteLine($"RESCUE {id.Name} race={id.Race} -> {string.Join(",", candidates)}");
                    }
                    else stillMissing++;
                }
            }
        }
        output.WriteLine($"strict found: loose agrees {agree}, differs {disagree}; strict missing: loose finds {rescued}, still none {stillMissing}; messages with >1 candidate {ambiguous}");
    }

    // First valid server code of the right race after the name (offset from the name end).
    private static List<(int Offset, int Code)> Loose(byte[] m, int start, int race)
    {
        var list = new List<(int, int)>();
        int anchor = m.AsSpan(start).IndexOf(new byte[] { 0xa0, 0x86, 0x01, 0x00, 0xa0, 0x86, 0x01, 0x00 });
        if (anchor < 0) return list;
        for (int pos = start + anchor + 8; pos < Math.Min(start + anchor + 80, m.Length - 2); pos++)
        {
            int code = BinaryPrimitives.ReadUInt16LittleEndian(m.AsSpan(pos));
            if (Servers.IsValid(code) && Servers.RaceOf(code) == race) list.Add((pos - start, code));
        }
        return list;
    }
}
