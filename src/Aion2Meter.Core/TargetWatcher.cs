using Aion2Meter.Protocol;

namespace Aion2Meter.Core;

/// <summary>
/// Tells at once, from the packet thread, who I aim at: the class of a player is learned from the identity message (45 36) when they come into view, and the target
/// messages name them. Kept apart from the session, which works in the UI timer (every 100 ms, later in a big fight): an alert must not wait for that.
/// Safe to call from several threads (one per network adapter).
/// <para>
/// Rules (2026-11-23 recordings, both directions): my click makes the server send 34 38 + the entity within 20-50 ms (<see cref="TargetMessage.Select"/>): that always counts, also for the
/// same one as before. 35 38 follows it, is repeated every 0.5-1.3 s while I fight the target and comes alone when the game aims for me: it counts only for another entity
/// than the current one, and not for one that was aimed at or cleared in the last second (the echo that comes with a clear).
/// </para>
/// </summary>
public sealed class TargetWatcher
{
    private const int MaxKnown = 20000;
    private static readonly TimeSpan EchoWindow = TimeSpan.FromSeconds(1);
    private readonly object _lock = new();
    private readonly Dictionary<ushort, TargetedPlayer> _known = [];
    private readonly Dictionary<uint, DateTimeOffset> _recent = [];
    private uint _current;
    private uint _awaiting;   // the target whose class was unknown: an identity message for it may still come

    /// <summary>For the log: identity messages seen, the biggest entity number among them, targets that counted.</summary>
    public int Identities { get; private set; }
    public int MaxSeen { get; private set; }
    public int Targets { get; private set; }

    /// <summary>Looks at one message; returns the player when I just aimed at someone (new, or by a click), otherwise null.</summary>
    public TargetedPlayer? Observe(GameMessage message, DateTimeOffset time)
    {
        switch (message)
        {
            case IdentityMessage { RawId: not 0 } m:
                lock (_lock)
                {
                    if (_known.Count >= MaxKnown) _known.Clear();
                    _known[m.RawId] = new TargetedPlayer(m.ClassCode, m.Name);
                    if (m.RawId > MaxSeen) MaxSeen = m.RawId;
                    Identities++;
                    if (_awaiting == m.RawId && m.ClassCode != 0) { _awaiting = 0; return _known[m.RawId] with { Id = m.RawId }; }   // the one I aim at was not known yet: tell now
                }
                return null;
            case DamageMessage { Flags: var fl } d when (fl & 0x02) != 0:
                return Learn(d.Source, d.SkillKey);
            case PeriodicMessage p:
                return Learn(p.Source, p.SkillKey);
            case TargetMessage m:
                lock (_lock)
                {
                    if (m.RawId == 0)
                    {
                        if (_current != 0) _recent[_current] = time;
                        _current = 0;
                        _awaiting = 0;
                        return null;
                    }
                    if (!m.Select)
                    {
                        if (m.RawId == _current) return null;                                                          // repeated while I fight it
                        if (_recent.TryGetValue(m.RawId, out var last) && time - last < EchoWindow) return null;      // the echo of a click or a clear
                    }
                    _current = m.RawId;
                    if (_recent.Count > 64) _recent.Clear();
                    _recent[m.RawId] = time;
                    _awaiting = 0;
                    Targets++;
                    if (m.RawId <= ushort.MaxValue && _known.TryGetValue((ushort)m.RawId, out var who) && who.ClassCode != 0) return who with { Id = m.RawId };
                    _awaiting = m.RawId;
                    return new TargetedPlayer(0, "", m.RawId);
                }
            default:
                return null;
        }
    }

    /// <summary>A player I never saw come into view (the app started later) shows their class by what they cast: the skill number starts with it (5678 of 5678 agreed with the identity message).</summary>
    private TargetedPlayer? Learn(uint entity, uint skillKey)
    {
        if (!Entities.IsPlayer(entity)) return null;
        int cls = PlayerClass.FromSkillKey(skillKey);
        ushort raw = cls == 0 ? (ushort)0 : PlayerClass.RawIdOf(entity);
        if (raw == 0) return null;
        lock (_lock)
        {
            if (_known.TryGetValue(raw, out var k) && k.ClassCode != 0) return null;
            if (_known.Count >= MaxKnown) _known.Clear();
            _known[raw] = new TargetedPlayer(cls, k?.Name ?? "");
            if (_awaiting == raw) { _awaiting = 0; return _known[raw] with { Id = raw }; }   // the one I aim at was not known yet: tell now
        }
        return null;
    }

    /// <summary>The connection or the character changed: what was known may be stale.</summary>
    public void Clear()
    {
        lock (_lock) { _known.Clear(); _recent.Clear(); _current = 0; _awaiting = 0; }
    }
}
