namespace Aion2Meter.Core;

public enum ResetMode
{
    /// <summary>Periods start on the clock hour.</summary>
    OnTheHour,
    /// <summary>Periods start at the minute and second of the game's ranking reset (from the ranking deadline).</summary>
    RankingReset,
}

/// <summary>Points gained and lost in one period.</summary>
public sealed class Bucket
{
    /// <summary>Row id in the store; 0 until saved.</summary>
    public long Id { get; set; }
    public DateTimeOffset Start { get; init; }
    /// <summary>Planned end, or the moment the period was cut short by a change of reset time.</summary>
    public DateTimeOffset End { get; set; }
    public long Gain { get; set; }
    /// <summary>Points lost, as a positive number.</summary>
    public long Loss { get; set; }
    public int Events { get; set; }
    /// <summary>Not measured for the whole period (started or stopped midway, lost data, reset time changed).</summary>
    public bool Partial { get; set; }
    public long Net => Gain - Loss;
    /// <summary>The same points by the size of my party at the time (1 = alone). Empty for periods recorded before this was kept.</summary>
    public SortedDictionary<int, PartyPoints> Parties { get; set; } = [];

    /// <summary>"size:gain:loss:events;..." for the store.</summary>
    public string EncodeParties() => string.Join(';', Parties.Select(p => $"{p.Key}:{p.Value.Gain}:{p.Value.Loss}:{p.Value.Events}"));

    public static SortedDictionary<int, PartyPoints> DecodeParties(string text)
    {
        var d = new SortedDictionary<int, PartyPoints>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split(':');
            if (f.Length == 4 && int.TryParse(f[0], out int size) && long.TryParse(f[1], out long g) && long.TryParse(f[2], out long l) && int.TryParse(f[3], out int n))
                d[size] = new PartyPoints { Gain = g, Loss = l, Events = n };
        }
        return d;
    }
}

/// <summary>Points gained and lost while my party had one size.</summary>
public sealed class PartyPoints
{
    public long Gain { get; set; }
    public long Loss { get; set; }
    public int Events { get; set; }
}

public readonly record struct Totals(long Gain, long Loss, int Events)
{
    public long Net => Gain - Loss;
}

/// <summary>Splits point changes into one-hour periods. Rules: docs/BEHAVIOR.md §1.5-1.6.</summary>
public sealed class BucketBook
{
    public static readonly TimeSpan Length = TimeSpan.FromHours(1);

    private readonly List<Bucket> _closed = [];
    private DateTimeOffset? _measuringSince;

    /// <param name="history">Saved periods, oldest first; one still running at <paramref name="now"/> becomes current again.</param>
    public BucketBook(int offsetSeconds, IEnumerable<Bucket> history, DateTimeOffset now)
    {
        OffsetSeconds = Normalize(offsetSeconds);
        foreach (var b in history)
        {
            if (now < b.End && b.Start <= now) Current = b;
            else _closed.Add(b);
        }
    }

    /// <summary>A period was created or changed and should be saved.</summary>
    public event Action<Bucket>? Changed;

    /// <summary>Seconds after the hour at which periods start (0-3599).</summary>
    public int OffsetSeconds { get; private set; }
    public Bucket? Current { get; private set; }
    public IReadOnlyList<Bucket> Closed => _closed;
    public Bucket? Previous => _closed.Count > 0 ? _closed[^1] : null;
    public bool Measuring => _measuringSince is not null;

    public static int Normalize(long seconds) => (int)((seconds % 3600 + 3600) % 3600);

    public DateTimeOffset PeriodStart(DateTimeOffset t)
    {
        long s = t.ToUnixTimeSeconds() - OffsetSeconds;
        return DateTimeOffset.FromUnixTimeSeconds(s - Normalize(s) + OffsetSeconds).ToOffset(Kst.Offset);
    }

    public void Resume(DateTimeOffset now)
    {
        Roll(now);
        _measuringSince = now;
        if (Current is { } c && now > c.Start) MarkPartial(c);
        Roll(now);
    }

    public void Pause(DateTimeOffset now)
    {
        Roll(now);
        _measuringSince = null;
        if (Current is { } c) MarkPartial(c);
    }

    /// <summary>Changes in the current period were missed.</summary>
    public void Interrupt(DateTimeOffset now)
    {
        Roll(now);
        if (Current is { } c) MarkPartial(c);
    }

    /// <summary>Closes the current period when its time is over and, while measuring, opens the next.</summary>
    public void Roll(DateTimeOffset now)
    {
        if (Current is { } c && now >= c.End)
        {
            _closed.Add(c);
            Current = null;
        }
        if (Current is null && Measuring) Open(now);
    }

    /// <param name="partySize">People in my party, 1 when alone.</param>
    public Bucket Add(long delta, DateTimeOffset time, int partySize = 1)
    {
        Roll(time);
        var b = Current ?? Open(time);
        if (!b.Parties.TryGetValue(partySize, out var share)) b.Parties[partySize] = share = new PartyPoints();
        if (delta > 0) { b.Gain += delta; share.Gain += delta; }
        else { b.Loss -= delta; share.Loss -= delta; }
        b.Events++;
        share.Events++;
        Changed?.Invoke(b);
        return b;
    }

    /// <summary>A new reset time cuts the current period short; the next one starts now and is partial.</summary>
    /// <returns>Whether the offset changed.</returns>
    public bool SetOffset(int offsetSeconds, DateTimeOffset now)
    {
        offsetSeconds = Normalize(offsetSeconds);
        if (offsetSeconds == OffsetSeconds) return false;
        Roll(now);
        OffsetSeconds = offsetSeconds;
        if (Current is { } c)
        {
            c.End = now;
            c.Partial = true;
            Changed?.Invoke(c);
            _closed.Add(c);
            Current = null;
            if (Measuring) _measuringSince = now;
        }
        Roll(now);
        return true;
    }

    /// <summary>
    /// Ends the current period now (it becomes a short, partial one) so that what it holds can be taken before measuring stops or the app
    /// closes. Measuring on afterwards opens a new period in the same hour.
    /// </summary>
    public void CloseCurrent(DateTimeOffset now)
    {
        Roll(now);
        if (Current is not { } c) return;
        if (now < c.End) c.End = now;
        c.Partial = true;
        Changed?.Invoke(c);
        _closed.Add(c);
        Current = null;
    }

    /// <summary>Sum of the periods that started on <paramref name="day"/> (KST).</summary>
    public Totals DayTotal(DateOnly day)
    {
        long gain = 0, loss = 0;
        int events = 0;
        foreach (var b in _closed.Append(Current))
            if (b is not null && Kst.Day(b.Start) == day)
                (gain, loss, events) = (gain + b.Gain, loss + b.Loss, events + b.Events);
        return new Totals(gain, loss, events);
    }

    /// <summary>Drops closed periods that started before <paramref name="before"/> from memory (they stay in the store).</summary>
    public void Trim(DateTimeOffset before) => _closed.RemoveAll(b => b.Start < before);

    /// <summary>Drops the oldest <paramref name="count"/> closed periods from memory (the caller has taken what it needs from them).</summary>
    public void DropClosed(int count) => _closed.RemoveRange(0, Math.Min(count, _closed.Count));

    private Bucket Open(DateTimeOffset now)
    {
        var start = PeriodStart(now);
        var b = new Bucket { Start = start, End = start + Length, Partial = _measuringSince is not { } since || since > start };
        Current = b;
        Changed?.Invoke(b);
        return b;
    }

    private void MarkPartial(Bucket b)
    {
        if (b.Partial) return;
        b.Partial = true;
        Changed?.Invoke(b);
    }
}
