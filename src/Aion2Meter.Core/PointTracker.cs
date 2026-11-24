using Aion2Meter.Protocol;

namespace Aion2Meter.Core;

/// <param name="Delta">Change since the previous accepted total; 0 when this total only became the baseline.</param>
/// <param name="Rebased">Continuity was lost: the previous total could not be carried over, changes in between are missing.</param>
public readonly record struct PointUpdate(long Delta, bool Rebased);

/// <summary>
/// Turns total rank points into changes, following one connection at a time. Rules: docs/BEHAVIOR.md §1.1-1.4.
/// </summary>
public sealed class PointTracker
{
    public const long MaxCarry = 50_000;
    /// <summary>Largest difference from a character's remembered last total that is still counted (BEHAVIOR.md §1.2).</summary>
    public const long MaxRemembered = 30_000;
    public static readonly TimeSpan CarryWindow = TimeSpan.FromMinutes(10);
    /// <summary>The measured connection is treated as dead when it has sent nothing for this long.</summary>
    public static readonly TimeSpan SilentSwitch = TimeSpan.FromSeconds(60);

    private ulong? _last;
    private DateTimeOffset _lastTime;
    private StreamToken? _active;
    private DateTimeOffset _activeSeen;
    private bool _activeLost;
    private bool _seeded;

    public bool Measuring { get; private set; }
    public ulong? Total => _last;
    public FlowKey? ActiveFlow => _active?.Flow;
    /// <summary><see cref="Total"/> came from the game in this measurement (not only the remembered value).</summary>
    public bool TotalIsLive => _active is not null;

    /// <param name="remembered">The character's last known total, if any: the first total is counted against it
    /// when it differs by at most <see cref="MaxRemembered"/>; otherwise the first total only becomes the baseline.</param>
    public void Start(ulong? remembered = null)
    {
        Measuring = true;
        Seed(remembered);
    }

    /// <summary>Stopping drops the baseline: a later start only uses the remembered total.</summary>
    public void Stop()
    {
        Measuring = false;
        _last = null;
        _active = null;
    }

    /// <summary>Another character: its first total is never diffed against the previous character's.</summary>
    /// <param name="remembered">The new character's last known total, as in <see cref="Start"/>.</param>
    public void Reset(ulong? remembered = null) => Seed(remembered);

    private void Seed(ulong? remembered)
    {
        _last = remembered;
        _seeded = remembered is not null;
        _active = null;
        _activeLost = false;
    }

    /// <summary>Any traffic on <paramref name="token"/>, to notice when the measured connection goes silent.</summary>
    public void Seen(StreamToken token, DateTimeOffset time)
    {
        if (token == _active) _activeSeen = time;
    }

    /// <summary>The connection was closed or lost data (gap).</summary>
    public void Lost(FlowKey flow)
    {
        if (_active?.Flow == flow) _activeLost = true;
    }

    /// <returns>null when not measuring or when the total comes from a second connection while the measured one is alive.</returns>
    /// <param name="exact">The total is known to be the current one (from the enter-world message), not the result of a change:
    /// without a live baseline it becomes the baseline whatever was remembered, so the first change after it is counted in full.</param>
    public PointUpdate? OnPoint(StreamToken token, ulong total, DateTimeOffset time, bool exact = false)
    {
        if (!Measuring) return null;
        bool carry;
        if (_active is null) carry = !exact && _seeded && Math.Abs((long)total - (long)_last!.Value) <= MaxRemembered;
        else if (token == _active) carry = true;
        else if (_activeLost || time - _activeSeen > SilentSwitch)
            carry = _last is ulong last && Math.Abs((long)total - (long)last) <= MaxCarry && time - _lastTime <= CarryWindow;
        else return null;

        bool hadBaseline = _active is not null;
        long delta = carry ? (long)total - (long)_last!.Value : 0;
        _active = token;
        _activeSeen = time;
        _activeLost = false;
        _last = total;
        _lastTime = time;
        return new PointUpdate(delta, Rebased: !carry && hadBaseline);
    }
}
