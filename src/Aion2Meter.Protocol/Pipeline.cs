namespace Aion2Meter.Protocol;

/// <summary>Identifies one continuous stretch of one connection; a new value means "do not diff against before".</summary>
public readonly record struct StreamToken(FlowKey Flow, int Generation);

public abstract record CaptureEvent(StreamToken Token, DateTimeOffset Time);
public sealed record MessageEvent(StreamToken Token, DateTimeOffset Time, GameMessage Message) : CaptureEvent(Token, Time);
/// <summary>Continuity lost (SYN, gap, corrupt data): baselines must not span it.</summary>
public sealed record GapEvent(StreamToken Token, DateTimeOffset Time) : CaptureEvent(Token, Time);
public sealed record ClosedEvent(StreamToken Token, DateTimeOffset Time) : CaptureEvent(Token, Time);

/// <summary>Captured frames → game events, for all server flows at once. Not thread-safe.</summary>
public sealed class Pipeline
{
    private const int MaxFlows = 64;
    private readonly Dictionary<FlowKey, TcpFlow> _flows = [];

    public List<CaptureEvent> Feed(ReadOnlySpan<byte> frame, int linkType, DateTimeOffset time)
    {
        var events = new List<CaptureEvent>();
        var segment = PacketParser.Parse(frame, linkType, time);
        if (segment is null) return events;
        if (!_flows.TryGetValue(segment.Flow, out var flow))
        {
            if (_flows.Count >= MaxFlows)
                _flows.Remove(_flows.MinBy(f => f.Value.LastSeen).Key);
            _flows[segment.Flow] = flow = new TcpFlow();
        }
        var (messages, broken) = flow.Feed(segment);
        var token = new StreamToken(segment.Flow, flow.Generation);
        if (broken) events.Add(new GapEvent(token, time));
        // A kill's death message and the point update it caused usually share a segment, in either order:
        // emit everything else before the segment's point updates so the matcher already knows the death.
        var parsed = new List<GameMessage>();
        foreach (var raw in messages)
            if (MessageParser.Parse(raw) is { } message)
                parsed.Add(message);
        foreach (var message in parsed.Where(m => m is not PointMessage).Concat(parsed.OfType<PointMessage>()))
            events.Add(new MessageEvent(token, time, message));
        if (segment.FinOrRst)
        {
            events.Add(new ClosedEvent(token, time));
            _flows.Remove(segment.Flow);
        }
        return events;
    }
}
