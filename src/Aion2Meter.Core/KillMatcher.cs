using Aion2Meter.Protocol;

namespace Aion2Meter.Core;

public enum KillKind { Kill, Death, Monster, Duel }

/// <summary>
/// A point change tied to a visible death. Kill: Name/Server are the victim's; Death: the killer's;
/// Monster: a non-PvP death I caused (no name); Duel: the opponent's name, Delta +1 when I won and -1 when I lost.
/// <paramref name="Entity"/> is the victim's entity number (kills; 0 when not known) and <paramref name="Damage"/> what we did to
/// each other until the kill (null: none seen). <paramref name="Attackers"/> (deaths only): everyone who hurt me just before I died.
/// </summary>
public sealed record KillRecord(KillKind Kind, string Name, int Server, int Race, long Delta, DateTimeOffset Time,
    uint Entity = 0, DamageStats? Damage = null, IReadOnlyList<AttackerDamage>? Attackers = null, bool Duel = false, int Class = 0);

/// <summary>A point gain that could not be tied to a visible death (out of view, split points, ...).</summary>
public sealed record UnmatchedGain(long Delta, DateTimeOffset Time);

/// <summary>
/// Ties rank point changes to death messages. Rules and their evidence: docs/BEHAVIOR.md §2-3.
/// Feed messages in arrival order; within one TCP segment deaths come before points (see Pipeline).
/// </summary>
public sealed class KillMatcher
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(0.35);
    public static readonly TimeSpan SameInstant = TimeSpan.FromSeconds(0.05);
    public static readonly TimeSpan SameInstantLoose = TimeSpan.FromSeconds(0.1);
    public static readonly TimeSpan NameWait = TimeSpan.FromSeconds(120);
    private const int MaxIdentities = 4096, MaxDeaths = 100, MaxCredited = 200, MaxSeen = 100;

    private sealed record Seen(DateTimeOffset Time, DeathMessage Death);
    private sealed record Character(string Name, int Server, int Race, int Class = 0);
    private sealed record Pending(uint Victim, long Delta, DateTimeOffset Time, DamageStats? Damage);

    private readonly Dictionary<uint, Character> _identities = [];
    private readonly LinkedList<Seen> _deaths = new();
    private readonly LinkedList<(uint Victim, DateTimeOffset Time)> _credited = new();
    private readonly List<Pending> _pending = [];
    private readonly List<UnmatchedGain> _unmatched = [];
    private readonly LinkedList<(string, int, long, long)> _seen = new();

    /// <summary>Damage between me and other players, attached to the kill it belongs to.</summary>
    public DamageLedger Damage { get; } = new();

    public uint? SelfEntity { get; private set; }
    public string SelfName { get; private set; } = "";
    /// <summary>My in-game server, 0 when unknown.</summary>
    public int SelfServer { get; private set; }
    /// <summary>Race from my own ranking row, used until my entity's identity is known.</summary>
    public int? SelfRankingRace { get; set; }

    /// <summary>Connection restarted: entity numbers are no longer valid.</summary>
    public void ClearConnection()
    {
        _identities.Clear();
        _deaths.Clear();
        _credited.Clear();
        _pending.Clear();
        _unmatched.Clear();
        Damage.Clear();
        SelfEntity = null;
    }

    /// <summary>
    /// I entered the world (login, character change, zone move) as <paramref name="entity"/>: nearby characters are new,
    /// and kills still waiting for a name become unmatched gains.
    /// </summary>
    public void EnterWorld(uint entity, string name, int race, int server)
    {
        SelfServer = server;
        Expire(DateTimeOffset.MaxValue);
        _identities.Clear();
        _deaths.Clear();
        _credited.Clear();
        Damage.Clear();
        SelfName = name;
        SelfRankingRace = race is 1 or 2 ? race : null;
        SelfEntity = entity;
    }

    /// <param name="server">In-game server, 0 to keep the known one (the ranking row's code is not it).</param>
    public void SetSelf(string name, int race, int server = 0)
    {
        if (server != 0 || name != SelfName) SelfServer = server;
        SelfName = name;
        SelfRankingRace = race is 1 or 2 ? race : null;
        foreach (var (entity, c) in _identities)
            if (c.Name == name) SelfEntity = entity;
    }

    public int? MyRace()
    {
        if (SelfEntity is uint e && _identities.TryGetValue(e, out var c) && c.Race is 1 or 2) return c.Race;
        return SelfRankingRace;
    }

    /// <returns>Kills whose victim name was still unknown when the points arrived.</returns>
    public List<KillRecord> OnIdentity(IdentityMessage m)
    {
        if (_identities.Count > MaxIdentities) _identities.Clear();
        _identities[m.Entity] = new Character(m.Name, m.Server, m.Race, m.ClassCode);
        // Only an exact server match: identities often lack the server, and a same-named player may be nearby.
        if (m.Name == SelfName && (SelfServer == 0 || m.Server == SelfServer)) SelfEntity = m.Entity;
        var resolved = new List<KillRecord>();
        foreach (var p in _pending.Where(p => p.Victim == m.Entity).ToList())
        {
            _pending.Remove(p);
            if (KillTarget(_identities[m.Entity], p.Delta, p.Time, p.Victim, p.Damage) is { } r) resolved.Add(r);
        }
        return resolved;
    }

    public void OnDeath(DeathMessage m, DateTimeOffset time)
    {
        _deaths.AddLast(new Seen(time, m));
        if (_deaths.Count > MaxDeaths) _deaths.RemoveFirst();
        _identities[m.Killer] = new Character(m.KillerName, m.KillerServer, m.KillerRace, _identities.TryGetValue(m.Killer, out var known) ? known.Class : 0);
        if (m.KillerName == SelfName && (SelfServer == 0 || m.KillerServer == SelfServer)) SelfEntity = m.Killer;
    }

    /// <summary>A duel ended (0B 94). Null when it is not mine (I am neither the winner nor the loser).</summary>
    public KillRecord? OnDuel(DuelMessage m, DateTimeOffset time)
    {
        bool wonByName = SelfName.Length > 0 && m.WinnerName == SelfName, lostByName = SelfName.Length > 0 && m.LoserName == SelfName;
        bool won = SelfEntity == m.WinnerEntity || wonByName, lost = SelfEntity == m.LoserEntity || lostByName;
        if (won == lost) return null;
        uint other = won ? m.LoserEntity : m.WinnerEntity;
        string name = won ? m.LoserName : m.WinnerName;
        var who = _identities.TryGetValue(other, out var c) ? c : null;
        // What we did to each other during the duel goes with it.
        return Remember(new KillRecord(KillKind.Duel, name, who?.Server ?? 0, who?.Race ?? 0, won ? 1 : -1, time, other, Damage.Take(other, time), Class: who?.Class ?? 0), withDelta: true);
    }

    /// <summary>A rank point change of <paramref name="delta"/> (non-zero) received at <paramref name="time"/>.</summary>
    public KillRecord? OnPoint(long delta, DateTimeOffset time)
    {
        if (delta == 0) return null;
        var near = _deaths.Where(d => d.Death.Pvp && time - d.Time >= TimeSpan.Zero && time - d.Time <= Window).ToList();
        int? mine = MyRace();
        return delta < 0 ? MyDeath(near, mine, delta, time) : Kill(near, mine, delta, time);
    }

    private KillRecord? MyDeath(List<Seen> near, int? mine, long delta, DateTimeOffset time)
    {
        var matches = near.Where(d => d.Death.Victim == SelfEntity).ToList();
        if (matches.Count == 0)
        {
            // Unknown or stale self entity (restart / reconnect): my death arrives with the point loss.
            var enemy = near.Where(d => mine is null || d.Death.KillerRace != mine).ToList();
            var same = enemy.Where(d => time - d.Time <= SameInstant).ToList();
            matches = same.Count == 1 ? same : enemy.Count == 1 ? enemy : [];
            if (matches.Count == 1) SelfEntity = matches[0].Death.Victim;
        }
        if (matches.Count != 1) return null;
        var death = matches[0].Death;
        // What we did to each other until my death (null when the killer is a monster, which the ledger does not follow).
        var damage = Damage.Take(death.Killer, time);
        var attackers = Damage.TakeAttackers(time, e => _identities.TryGetValue(e, out var c) ? (c.Name, c.Server) : null);
        return Remember(new KillRecord(KillKind.Death, death.KillerName, death.KillerServer, death.KillerRace, delta, time, death.Killer, damage, attackers, Class: _identities.TryGetValue(death.Killer, out var killer) ? killer.Class : 0), withDelta: false);
    }

    private KillRecord? Kill(List<Seen> near, int? mine, long delta, DateTimeOffset time)
    {
        var candidates = near.Where(d => d.Death.Victim != SelfEntity
                                         && !_credited.Contains((d.Death.Victim, d.Time))
                                         && (mine is null || d.Death.KillerRace == mine)).ToList();
        var same = candidates.Where(d => time - d.Time <= SameInstant).ToList();
        if (same.Count == 0) same = candidates.Where(d => time - d.Time <= SameInstantLoose).ToList();
        Seen chosen;
        if (same.Count > 0) chosen = same[0];
        // My own monster kill at the same instant explains the gain better than a PvP death up to 0.35 s earlier.
        else if (Monster(delta, time, SameInstantLoose) is { } monster) return monster;
        else if (candidates.Count == 1) chosen = candidates[0];
        else if (candidates.Count == 0 && Monster(delta, time, Window) is { } later) return later;
        else
        {
            _unmatched.Add(new UnmatchedGain(delta, time));
            return null;
        }
        _credited.AddLast((chosen.Death.Victim, chosen.Time));
        if (_credited.Count > MaxCredited) _credited.RemoveFirst();
        Expire(time);
        // Taken now: a name that arrives later must not pick up a newer fight with the same entity number.
        var damage = Damage.Take(chosen.Death.Victim, time);
        if (!_identities.TryGetValue(chosen.Death.Victim, out var victim))
        {
            _pending.Add(new Pending(chosen.Death.Victim, delta, time, damage));
            return null;
        }
        return KillTarget(victim, delta, time, chosen.Death.Victim, damage);
    }

    // Monsters also give rank points: a non-PvP death I caused in the window explains a gain with no PvP candidate.
    private KillRecord? Monster(long delta, DateTimeOffset time, TimeSpan within)
    {
        var hit = _deaths.FirstOrDefault(d => !d.Death.Pvp && IsMe(d.Death) && time - d.Time >= TimeSpan.Zero && time - d.Time <= within
                                             && !_credited.Contains((d.Death.Victim, d.Time)));
        if (hit is null) return null;
        _credited.AddLast((hit.Death.Victim, hit.Time));
        if (_credited.Count > MaxCredited) _credited.RemoveFirst();
        return new KillRecord(KillKind.Monster, "", 0, 0, delta, time);
    }

    // By entity, or by name and server (the same name can exist on another server) until my entity is known.
    private bool IsMe(DeathMessage d) =>
        d.Killer == SelfEntity || (SelfName.Length > 0 && d.KillerName == SelfName && (SelfServer == 0 || d.KillerServer == SelfServer));

    private KillRecord? KillTarget(Character victim, long delta, DateTimeOffset time, uint entity, DamageStats? damage) =>
        Remember(new KillRecord(KillKind.Kill, victim.Name, victim.Server, victim.Race, delta, time, entity, damage, Class: victim.Class), withDelta: true);

    // The same event must not be reported twice (e.g. a resolved pending kill and a replayed point).
    private KillRecord? Remember(KillRecord r, bool withDelta)
    {
        var key = (r.Name, r.Server, (long)Math.Round(r.Time.ToUnixTimeMilliseconds() / 10.0), withDelta ? r.Delta : long.MinValue);
        if (_seen.Contains(key)) return null;
        _seen.AddLast(key);
        if (_seen.Count > MaxSeen) _seen.RemoveFirst();
        return r;
    }

    /// <summary>Kills whose victim name did not arrive within <see cref="NameWait"/> become unmatched gains.</summary>
    public void Expire(DateTimeOffset now)
    {
        foreach (var p in _pending.Where(p => now - p.Time >= NameWait).ToList())
        {
            _pending.Remove(p);
            _unmatched.Add(new UnmatchedGain(p.Delta, p.Time));
        }
    }

    public List<UnmatchedGain> TakeUnmatched()
    {
        var items = _unmatched.ToList();
        _unmatched.Clear();
        return items;
    }
}
