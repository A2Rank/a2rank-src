namespace Aion2Meter.Protocol;

/// <summary>7-bit little-endian variable-length integer (max 5 bytes). See docs/PROTOCOL.md §2.</summary>
public static class Varint
{
    public const int MaxBytes = 5;

    /// <summary>Reads a varint at the start of <paramref name="data"/>.</summary>
    /// <returns>True and the value/length when complete; false when more bytes are needed.</returns>
    /// <exception cref="ProtocolException">More than 5 continuation bytes.</exception>
    public static bool TryRead(ReadOnlySpan<byte> data, out uint value, out int length)
    {
        value = 0;
        for (int i = 0; i < MaxBytes; i++)
        {
            if (i >= data.Length)
            {
                length = 0;
                return false;
            }
            byte b = data[i];
            value |= (uint)(b & 0x7F) << (7 * i);
            if (b < 0x80)
            {
                length = i + 1;
                return true;
            }
        }
        throw new ProtocolException("varint longer than 5 bytes");
    }
}

/// <summary>Malformed data: the caller resynchronizes instead of trusting the stream.</summary>
public sealed class ProtocolException(string message) : Exception(message);
