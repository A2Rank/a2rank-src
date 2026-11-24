namespace Aion2Meter.Protocol;

/// <summary>Decoder for the raw LZ4 block format (no frame header). See docs/PROTOCOL.md §2.1.</summary>
public static class Lz4Block
{
    /// <summary>Decodes <paramref name="src"/>; the result must be exactly <paramref name="expectedSize"/> bytes.</summary>
    /// <exception cref="ProtocolException">Truncated or inconsistent block.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> src, int expectedSize)
    {
        if (expectedSize <= 0 || expectedSize > Frames.MaxFrameSize)
            throw new ProtocolException("invalid decompressed size");
        var dst = new byte[expectedSize];
        int s = 0, d = 0;
        while (s < src.Length)
        {
            byte token = src[s++];

            int literals = ReadLength(src, ref s, token >> 4);
            if (s + literals > src.Length || d + literals > dst.Length)
                throw new ProtocolException("LZ4 literals out of range");
            src.Slice(s, literals).CopyTo(dst.AsSpan(d));
            s += literals;
            d += literals;
            if (s == src.Length)
                break; // last sequence has literals only

            if (s + 2 > src.Length)
                throw new ProtocolException("truncated LZ4 offset");
            int offset = src[s] | (src[s + 1] << 8);
            s += 2;
            int match = ReadLength(src, ref s, token & 0x0F) + 4;
            if (offset == 0 || offset > d || d + match > dst.Length)
                throw new ProtocolException("LZ4 match out of range");
            // Byte-by-byte copy: the source may overlap the destination (offset < match).
            for (int i = 0; i < match; i++, d++)
                dst[d] = dst[d - offset];
        }
        if (d != expectedSize)
            throw new ProtocolException("LZ4 size mismatch");
        return dst;
    }

    private static int ReadLength(ReadOnlySpan<byte> src, ref int s, int nibble)
    {
        int length = nibble;
        if (nibble == 15)
        {
            byte b;
            do
            {
                if (s >= src.Length)
                    throw new ProtocolException("truncated LZ4 length");
                b = src[s++];
                length += b;
            } while (b == 255);
        }
        return length;
    }
}
