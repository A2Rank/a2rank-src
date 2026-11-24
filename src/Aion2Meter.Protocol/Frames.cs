using System.Buffers.Binary;

namespace Aion2Meter.Protocol;

/// <summary>Frame layout: [varint L][payload]; total frame size = L - 4 + varint length. See docs/PROTOCOL.md §2.</summary>
public static class Frames
{
    public const int MaxFrameSize = 2 * 1024 * 1024;

    /// <summary>Reads the frame header at the start of <paramref name="data"/>.</summary>
    /// <returns>False when the header is not complete yet.</returns>
    /// <exception cref="ProtocolException">Impossible frame size.</exception>
    public static bool TryReadHeader(ReadOnlySpan<byte> data, out int frameSize, out int headerSize)
    {
        frameSize = headerSize = 0;
        if (!Varint.TryRead(data, out uint value, out headerSize))
            return false;
        long size = (long)value - 4 + headerSize;
        if (size < headerSize + 2 || size > MaxFrameSize)
            throw new ProtocolException("invalid frame size");
        frameSize = (int)size;
        return true;
    }

    /// <summary>Messages carried by one frame payload: itself, or the frames inside an LZ4 bundle.</summary>
    public static List<byte[]> UnpackPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2 || payload[0] != 0xFF || payload[1] != 0xFF)
            return [payload.ToArray()];
        if (payload.Length < 6)
            throw new ProtocolException("truncated compressed header");
        int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[2..]);
        byte[] plain = Lz4Block.Decode(payload[6..], size);
        var messages = new List<byte[]>();
        int pos = 0;
        while (pos < plain.Length)
        {
            if (!TryReadHeader(plain.AsSpan(pos), out int frameSize, out int headerSize))
                throw new ProtocolException("truncated inner header");
            if (pos + frameSize > plain.Length)
                throw new ProtocolException("truncated inner frame");
            messages.Add(plain.AsSpan(pos + headerSize, frameSize - headerSize).ToArray());
            pos += frameSize;
        }
        return messages;
    }
}
