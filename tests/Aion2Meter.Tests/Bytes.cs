using System.Buffers.Binary;
using System.Text;

namespace Aion2Meter.Tests;

/// <summary>Builds protocol bytes for tests, following docs/PROTOCOL.md.</summary>
internal sealed class Bytes
{
    private readonly List<byte> _b = [];

    public Bytes Raw(params byte[] data) { _b.AddRange(data); return this; }
    public Bytes U8(int v) { _b.Add((byte)v); return this; }
    public Bytes U16(int v) { Span<byte> s = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(s, (ushort)v); _b.AddRange(s.ToArray()); return this; }
    public Bytes U32(uint v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(s, v); _b.AddRange(s.ToArray()); return this; }
    public Bytes U64(ulong v) { Span<byte> s = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(s, v); _b.AddRange(s.ToArray()); return this; }
    public Bytes Varint(uint v) { _b.AddRange(VarintBytes(v)); return this; }
    public Bytes Str(string s) { var u = Encoding.UTF8.GetBytes(s); Varint((uint)u.Length); _b.AddRange(u); return this; }
    public byte[] ToArray() => [.. _b];

    public static byte[] VarintBytes(uint v)
    {
        var o = new List<byte>();
        do
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            o.Add(v != 0 ? (byte)(b | 0x80) : b);
        } while (v != 0);
        return [.. o];
    }

    /// <summary>[varint L][payload] with L = payload length + 4.</summary>
    public static byte[] Frame(byte[] payload) => [.. VarintBytes((uint)payload.Length + 4), .. payload];

    /// <summary>LZ4 block that stores everything as literals (valid, uncompressed).</summary>
    public static byte[] Lz4Literals(byte[] data)
    {
        var o = new List<byte>();
        int n = data.Length;
        o.Add((byte)(Math.Min(n, 15) << 4));
        if (n >= 15)
        {
            int rest = n - 15;
            while (rest >= 255) { o.Add(255); rest -= 255; }
            o.Add((byte)rest);
        }
        o.AddRange(data);
        return [.. o];
    }

    /// <summary>FF FF bundle holding the given messages as inner frames.</summary>
    public static byte[] Bundle(params byte[][] messages)
    {
        byte[] plain = [.. messages.SelectMany(Frame)];
        return [0xFF, 0xFF, .. new Bytes().U32((uint)plain.Length).ToArray(), .. Lz4Literals(plain)];
    }

    public static byte[] Point(ulong total) => new Bytes().Raw(0x73, 0xE2, 0x00).U64(total).ToArray();

    public static byte[] Death(uint victim, uint killer, int server, string killerName, int race, bool pvp) =>
        new Bytes().Raw(0x04, 0x8D).Varint(victim).U32(1234).Varint(killer).U16(server).Str(killerName).Str("길드")
            .U16(race).Raw(0, 0, 0).U8(pvp ? 1 : 0).ToArray();

    public static byte[] Identity(uint entity, string name, int level, int race, int server) =>
        new Bytes().Raw(0x45, 0x36).Varint(entity).Raw(0, 0, 0, 0, 0).Str(name).U32((uint)level).U8(race)
            .Raw(9, 9, 9).U16(server).Raw(1, 2, 3, 4, 5, 6).U16(server).Str("길드").U16(race).Raw(0, 0, 0, 0).ToArray();

    /// <summary>The other identity layout: no repeated server code, marker 100000 twice, then flag + varints + code.</summary>
    public static byte[] IdentityWithoutRepeat(uint entity, string name, int level, int race, int server) =>
        new Bytes().Raw(0x45, 0x36).Varint(entity).Raw(0, 0, 0, 0, 0).Str(name).U32((uint)level).U8(race)
            .Raw(9, 9, 9, 9, 9, 9, 9).U32(100000).U32(100000).Raw(1, 0x90, 0xD8, 0xCC, 1, 0x7D, 0xD3, 0xCC, 1).U16(server)
            .Raw(0x12, 0x11, 0x12, 0x13, 0xC8, 0xC9, 1).ToArray();

    public static byte[] EnterWorld(uint entity, string name, int server) =>
        new Bytes().Raw(0x33, 0x36).Varint(entity).Raw(0x5F, 0xA1, 0xEB, 0x3A, 0x37).Str(name).U16(server).Raw(new byte[300]).ToArray();

    /// <summary>An enter message that carries my total after the tag f4 cb 1f, as in the recordings.</summary>
    public static byte[] EnterWorldWithTotal(uint entity, string name, int server, ulong total) =>
        new Bytes().Raw(0x33, 0x36).Varint(entity).Raw(0x5F, 0xA1, 0xEB, 0x3A, 0x37).Str(name).U16(server).Raw(new byte[100])
            .Raw(0x03, 0x21, 0xF4, 0xCB, 0x1F).U64(total).Raw(0xF3, 0xCD, 0x0C, 0xF1, 0xA0, 1, 0, 0).Raw(new byte[200]).ToArray();

    public static byte[] RankingEntry(uint rank, int server, ulong characterId, string name, ulong score) =>
        new Bytes().U32(rank).U64(((ulong)server << 48) | characterId).Str(name).Str("길드").U16(Protocol.Servers.RaceOf(server))
            .U32(55).U32(0).U8(3).U64(score).Str("{\"kills\":1}").U32(0).U32(0).U32(0).U32(0).ToArray();

    /// <summary>57 8D: my rank on <paramref name="board"/> (01 = rank points; the game sends other boards in the same form) for a class filter.</summary>
    public static byte[] PersonalRanking(byte[] entry, int total, ulong deadlineMs, int filter = 0, int board = 1) =>
        new Bytes().Raw(0x57, 0x8D, 0, 0, (byte)board).U8(filter).Raw(entry).U32((uint)total).U64(deadlineMs).ToArray();

    /// <summary>55 8D: the list of a class filter (entries from <see cref="RankingEntry"/>).</summary>
    public static byte[] ListRanking(byte[][] entries, int total, ulong deadlineMs, int filter = 0, int board = 1)
    {
        var b = new Bytes().Raw(0x55, 0x8D, 0, 0, (byte)board).U8(filter).Varint((uint)entries.Length);
        foreach (var e in entries) b.Raw(e);
        return b.U64(deadlineMs).U32((uint)total).ToArray();
    }

    /// <summary>Ethernet + IPv4 + TCP frame from the game server port.</summary>
    public static byte[] Ethernet(uint seq, byte[] payload, byte flags = 0x18, int sport = 13328)
    {
        var tcp = new Bytes().U8(sport >> 8).U8(sport & 0xFF).U8(0xC0).U8(0x01)
            .U8((int)(seq >> 24)).U8((int)(seq >> 16 & 0xFF)).U8((int)(seq >> 8 & 0xFF)).U8((int)(seq & 0xFF))
            .U32(0).U8(0x50).U8(flags).U16(0xFFFF).U16(0).U16(0).Raw(payload).ToArray();
        int total = 20 + tcp.Length;
        var ip = new Bytes().U8(0x45).U8(0).U8(total >> 8).U8(total & 0xFF).U16(0).U8(0x40).U8(0).U8(64).U8(6).U16(0)
            .Raw(10, 0, 0, 1).Raw(192, 168, 0, 2).Raw(tcp).ToArray();
        return new Bytes().Raw(new byte[12]).U8(0x08).U8(0x00).Raw(ip).ToArray();
    }
}
