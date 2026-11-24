using System.Buffers.Binary;

namespace Aion2Meter.Capture;

public sealed record CapturedFrame(int LinkType, DateTimeOffset Time, byte[] Data);

/// <summary>
/// Classic pcap files (microsecond timestamps, little endian), readable by Wireshark.
/// Used to record raw game traffic for byte-level tests (BEHAVIOR.md §6).
/// </summary>
public sealed class PcapWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly byte[] _header = new byte[16];

    public PcapWriter(string path, int linkType)
    {
        LinkType = linkType;
        _stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var h = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(h, 0xA1B2C3D4);
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(6), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(16), 65535);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(20), (uint)linkType);
        _stream.Write(h);
    }

    public int LinkType { get; }

    public void Write(DateTimeOffset time, ReadOnlySpan<byte> data)
    {
        long us = (time - DateTimeOffset.UnixEpoch).Ticks / 10;
        BinaryPrimitives.WriteUInt32LittleEndian(_header, (uint)(us / 1_000_000));
        BinaryPrimitives.WriteUInt32LittleEndian(_header.AsSpan(4), (uint)(us % 1_000_000));
        BinaryPrimitives.WriteUInt32LittleEndian(_header.AsSpan(8), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(_header.AsSpan(12), (uint)data.Length);
        _stream.Write(_header);
        _stream.Write(data);
    }

    public void Flush() => _stream.Flush();

    public void Dispose() => _stream.Dispose();
}

public static class PcapReader
{
    public static IEnumerable<CapturedFrame> Read(string path)
    {
        using var s = File.OpenRead(path);
        var h = new byte[24];
        s.ReadExactly(h);
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(h);
        bool nanos = magic == 0xA1B23C4D;
        if (magic != 0xA1B2C3D4 && !nanos) throw new InvalidDataException("not a little-endian pcap file");
        int linkType = (int)BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(20));
        var record = new byte[16];
        // A recording still being written may end inside a record: stop there.
        while (s.ReadAtLeast(record, 16, throwOnEndOfStream: false) == 16)
        {
            long seconds = BinaryPrimitives.ReadUInt32LittleEndian(record);
            long fraction = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(4));
            var data = new byte[BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(8))];
            if (s.ReadAtLeast(data, data.Length, throwOnEndOfStream: false) < data.Length) yield break;
            var time = DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(nanos ? fraction / 100 : fraction * 10);
            yield return new CapturedFrame(linkType, time, data);
        }
    }
}
