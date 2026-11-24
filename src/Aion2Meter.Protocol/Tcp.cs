using System.Buffers.Binary;

namespace Aion2Meter.Protocol;

/// <summary>Direction-specific TCP flow: server address/port → client address/port.</summary>
public readonly record struct FlowKey(string Source, int SourcePort, string Destination, int DestinationPort);

public sealed record TcpSegment(FlowKey Flow, uint Sequence, byte Flags, byte[] Payload, DateTimeOffset Time)
{
    public bool Syn => (Flags & 0x02) != 0;
    public bool FinOrRst => (Flags & 0x05) != 0;
}

/// <summary>Extracts server→client TCP segments from captured link-layer frames (IPv4/IPv6).</summary>
public static class PacketParser
{
    public const int ServerPort = 13328;

    /// <param name="linkType">pcap DLT: 1 Ethernet, 0/108 loopback, 12/101 raw IP.</param>
    public static TcpSegment? Parse(ReadOnlySpan<byte> frame, int linkType, DateTimeOffset time)
    {
        ReadOnlySpan<byte> ip;
        switch (linkType)
        {
            case 1:
                if (frame.Length < 14) return null;
                int etherType = BinaryPrimitives.ReadUInt16BigEndian(frame[12..]);
                int offset = 14;
                while (etherType is 0x8100 or 0x88A8 or 0x9100) // VLAN tags
                {
                    if (frame.Length < offset + 4) return null;
                    etherType = BinaryPrimitives.ReadUInt16BigEndian(frame[(offset + 2)..]);
                    offset += 4;
                }
                if (etherType is not (0x0800 or 0x86DD)) return null;
                ip = frame[offset..];
                break;
            case 0 or 108:
                if (frame.Length < 4) return null;
                ip = frame[4..];
                break;
            case 12 or 101:
                ip = frame;
                break;
            default:
                return null;
        }
        if (ip.IsEmpty) return null;

        ReadOnlySpan<byte> tcp;
        string src, dst;
        switch (ip[0] >> 4)
        {
            case 4:
                if (ip.Length < 20) return null;
                int ihl = (ip[0] & 0x0F) * 4;
                int total = BinaryPrimitives.ReadUInt16BigEndian(ip[2..]);
                if (ihl < 20 || total > ip.Length || total < ihl || ip[9] != 6) return null;
                if ((BinaryPrimitives.ReadUInt16BigEndian(ip[6..]) & 0x3FFF) != 0) return null; // fragments
                src = Convert.ToHexString(ip.Slice(12, 4));
                dst = Convert.ToHexString(ip.Slice(16, 4));
                tcp = ip[ihl..total];
                break;
            case 6:
                if (ip.Length < 40) return null;
                int end = 40 + BinaryPrimitives.ReadUInt16BigEndian(ip[4..]);
                if (end > ip.Length) return null;
                src = Convert.ToHexString(ip.Slice(8, 16));
                dst = Convert.ToHexString(ip.Slice(24, 16));
                int next = ip[6], pos = 40;
                for (int i = 0; i < 8 && next != 6; i++) // skip extension headers
                {
                    if (next is not (0 or 43 or 60 or 51) || pos + 2 > end) return null;
                    int len = next == 51 ? (ip[pos + 1] + 2) * 4 : (ip[pos + 1] + 1) * 8;
                    next = ip[pos];
                    pos += len;
                }
                if (next != 6 || pos > end) return null;
                tcp = ip[pos..end];
                break;
            default:
                return null;
        }
        if (tcp.Length < 20) return null;
        int sport = BinaryPrimitives.ReadUInt16BigEndian(tcp);
        int dport = BinaryPrimitives.ReadUInt16BigEndian(tcp[2..]);
        uint seq = BinaryPrimitives.ReadUInt32BigEndian(tcp[4..]);
        int headerLen = (tcp[12] >> 4) * 4;
        if (sport != ServerPort || headerLen < 20 || headerLen > tcp.Length) return null;
        return new TcpSegment(new FlowKey(src, sport, dst, dport), seq, tcp[13], tcp[headerLen..].ToArray(), time);
    }
}

/// <summary>
/// Reorders one flow's segments by sequence number. A hole that persists for 2 s (or too much buffered data)
/// is a capture gap: buffered data after the hole is used and the decoder restarts (Generation increments).
/// </summary>
public sealed class TcpFlow
{
    private const int MaxPending = 4 * Frames.MaxFrameSize;
    private static readonly TimeSpan GapTimeout = TimeSpan.FromSeconds(2);
    private readonly SortedDictionary<long, byte[]> _pending = [];
    private long? _expected;
    private DateTimeOffset? _waitingSince;
    private readonly StreamDecoder _decoder = new();

    public int Generation { get; private set; }
    public DateTimeOffset LastSeen { get; private set; }

    /// <returns>Messages decoded from this segment, and whether continuity was broken (SYN, gap, corrupt data).</returns>
    public (List<byte[]> Messages, bool Broken) Feed(TcpSegment segment)
    {
        LastSeen = segment.Time;
        bool broken = false;
        uint seq = segment.Sequence;
        if (segment.Syn)
        {
            Reset();
            seq++;
            broken = true;
        }
        byte[] data = segment.Payload;
        if (data.Length == 0) return ([], broken);

        _expected ??= seq;
        // Map the 32-bit sequence into the absolute space nearest to the expected position.
        long abs = _expected.Value + (int)(seq - (uint)_expected.Value);
        if (abs + data.Length <= _expected) return ([], broken);
        if (abs < _expected)
        {
            data = data[(int)(_expected.Value - abs)..];
            abs = _expected.Value;
        }
        if (abs > _expected)
        {
            _pending[abs] = data;
            _waitingSince ??= segment.Time;
            if (_pending.Values.Sum(p => p.Length) <= MaxPending && segment.Time - _waitingSince < GapTimeout)
                return ([], broken);
            // Gap: continue from the earliest buffered data with a fresh decoder.
            abs = _pending.Keys.First();
            data = _pending[abs];
            Reset();
            _expected = abs;
            broken = true;
        }

        var chunk = new List<byte>(data);
        _expected += data.Length;
        _pending.Remove(abs);
        while (_pending.Count > 0)
        {
            var (first, more) = _pending.First();
            if (first > _expected) break;
            _pending.Remove(first);
            long overlap = _expected.Value - first;
            if (overlap < more.Length)
            {
                chunk.AddRange(more.AsSpan((int)overlap).ToArray());
                _expected += more.Length - overlap;
            }
        }
        if (_pending.Count == 0) _waitingSince = null;

        try
        {
            return (_decoder.Feed(chunk.ToArray()), broken);
        }
        catch (ProtocolException)
        {
            _decoder.Clear();
            Generation++;
            return ([], true);
        }
    }

    private void Reset()
    {
        _expected = null;
        _pending.Clear();
        _waitingSince = null;
        _decoder.Clear();
        Generation++;
    }
}
