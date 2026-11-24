namespace Aion2Meter.Protocol;

/// <summary>
/// Splits one TCP byte stream into messages. When the capture starts mid-stream it locks onto the first
/// offset where three valid frames follow (or one valid compressed bundle with 3+ messages). See docs/PROTOCOL.md §2.2.
/// </summary>
public sealed class StreamDecoder
{
    private const int MaxBuffer = 4 * Frames.MaxFrameSize;
    private const int SyncScan = 64 * 1024;
    private readonly List<byte> _buffer = [];
    public bool Locked { get; private set; }

    public void Clear()
    {
        _buffer.Clear();
        Locked = false;
    }

    /// <exception cref="ProtocolException">Corrupt stream; the caller should <see cref="Clear"/> and start over.</exception>
    public List<byte[]> Feed(ReadOnlySpan<byte> data)
    {
        _buffer.AddRange(data);
        if (_buffer.Count > MaxBuffer)
        {
            Clear();
            throw new ProtocolException("frame buffer overflow");
        }
        var messages = new List<byte[]>();
        if (!Locked && !Synchronize())
            return messages;

        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer);
        int consumed = 0;
        while (consumed < span.Length)
        {
            if (!Frames.TryReadHeader(span[consumed..], out int size, out int header) || consumed + size > span.Length)
                break;
            messages.AddRange(Frames.UnpackPayload(span.Slice(consumed + header, size - header)));
            consumed += size;
        }
        _buffer.RemoveRange(0, consumed);
        return messages;
    }

    private bool Synchronize()
    {
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer);
        for (int start = 0; start < Math.Min(span.Length, SyncScan); start++)
        {
            int pos = start, good = 0;
            try
            {
                while (good < 3)
                {
                    if (!Frames.TryReadHeader(span[pos..], out int size, out int header) || pos + size > span.Length)
                        break;
                    var payload = span.Slice(pos + header, size - header);
                    var inner = Frames.UnpackPayload(payload);
                    if (inner.Count == 0 || !inner.TrueForAll(Known))
                        break;
                    good = payload.Length >= 2 && payload[0] == 0xFF && payload[1] == 0xFF && inner.Count >= 3 ? 3 : good + 1;
                    pos += size;
                }
            }
            catch (ProtocolException) { }
            if (good >= 3)
            {
                _buffer.RemoveRange(0, start);
                Locked = true;
                return true;
            }
        }
        if (_buffer.Count > Frames.MaxFrameSize)
            _buffer.RemoveRange(0, _buffer.Count - Frames.MaxFrameSize); // keep room for one large frame
        return false;
    }

    private static bool Known(byte[] m) => m.Length >= 2 && MessageParser.KnownSecondBytes.Contains(m[1]);
}
