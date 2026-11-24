namespace Aion2Meter.Core;

/// <param name="Dealt">Damage I did to this player over these kills (until each kill).</param>
/// <param name="Received">Damage this player did to me over the same fights.</param>
/// <param name="DealtSkills">My damage to this player per skill, biggest first (empty when none was seen).</param>
/// <param name="ReceivedSkills">This player's damage to me per skill of theirs, biggest first.</param>
/// <param name="Entries">Every single kill (or death) behind the totals, oldest first: each with its own damage and skills.</param>
public sealed record KillTally(string Name, int Server, int Race, int Count, long Points, DateTimeOffset Last, long Dealt = 0, long Received = 0,
    IReadOnlyList<SkillDamage>? DealtSkills = null, IReadOnlyList<SkillDamage>? ReceivedSkills = null,
    IReadOnlyList<AttackerDamage>? Attackers = null, IReadOnlyList<KillRecord>? Entries = null,
    HitStats? DealtHow = null, HitStats? ReceivedHow = null, IReadOnlyList<SkillDamage>? HealedSkills = null, int Class = 0);

/// <summary>
/// Who was killed this clock hour and who killed me (always on the hour, independent of the point periods). Rules: docs/BEHAVIOR.md §4.
/// </summary>
public sealed class HourlyKillList(int threshold = 3)
{
    private readonly Dictionary<(string, int), KillTally> _tallies = [];
    private readonly Dictionary<(string, int), KillTally> _killers = [];

    /// <summary>Kill count from which a name is pinned on top and highlighted.</summary>
    public int Threshold { get; set; } = threshold;
    public DateTimeOffset Hour { get; private set; }
    /// <summary>Goes up with every change of the list: a window that shows it redraws only when this changed.</summary>
    public int Revision { get; private set; }
    public int UnknownCount { get; private set; }
    public long UnknownPoints { get; private set; }
    /// <summary>Points from monsters I killed this hour.</summary>
    public int MonsterCount { get; private set; }
    public long MonsterPoints { get; private set; }
    /// <summary>My own deaths this hour.</summary>
    public int Deaths { get; private set; }
    public int KillCount => _tallies.Values.Sum(t => t.Count);

    public bool IsPinned(KillTally t) => t.Count >= Threshold;

    /// <returns>Whether a new hour started (the list was emptied).</returns>
    public bool Roll(DateTimeOffset now)
    {
        var hour = Kst.HourStart(now);
        if (hour <= Hour) return false;
        Hour = hour;
        Revision++;
        _tallies.Clear();
        _killers.Clear();
        UnknownCount = 0;
        UnknownPoints = 0;
        Deaths = 0;
        MonsterCount = 0;
        MonsterPoints = 0;
        return true;
    }

    /// <returns>Whether the record belongs to this hour and was counted.</returns>
    public bool Add(KillRecord r)
    {
        if (!InHour(r.Time)) return false;
        Revision++;
        // A duel is a kill when I won and a death when I lost (no points either way).
        if (r.Kind == KillKind.Duel) r = r with { Kind = r.Delta > 0 ? KillKind.Kill : KillKind.Death, Delta = 0, Duel = true };
        if (r.Kind == KillKind.Death)
        {
            Deaths++;
            Tally(_killers, r);
            return true;
        }
        if (r.Kind == KillKind.Monster)
        {
            MonsterCount++;
            MonsterPoints += r.Delta;
            return true;
        }
        Tally(_tallies, r);
        return true;
    }

    private static void Tally(Dictionary<(string, int), KillTally> tallies, KillRecord r)
    {
        var key = (r.Name, r.Server);
        long dealt = r.Damage?.Dealt ?? 0, received = r.Damage?.Received ?? 0;
        tallies[key] = tallies.TryGetValue(key, out var t)
            ? t with
            {
                Count = t.Count + 1, Points = t.Points + r.Delta, Last = r.Time > t.Last ? r.Time : t.Last,
                Dealt = t.Dealt + dealt, Received = t.Received + received,
                DealtSkills = SkillDamage.Merge(t.DealtSkills, r.Damage?.DealtBySkill),
                ReceivedSkills = SkillDamage.Merge(t.ReceivedSkills, r.Damage?.ReceivedBySkill),
                Attackers = AttackerDamage.Merge(t.Attackers, r.Attackers),
                Entries = [.. t.Entries ?? [], r],
                DealtHow = HitStats.Merge(t.DealtHow, r.Damage?.DealtHow), ReceivedHow = HitStats.Merge(t.ReceivedHow, r.Damage?.ReceivedHow),
                HealedSkills = SkillDamage.Merge(t.HealedSkills, r.Damage?.HealBySkill),
                Class = r.Class != 0 ? r.Class : t.Class,
            }
            : new KillTally(r.Name, r.Server, r.Race, 1, r.Delta, r.Time, dealt, received,
                SkillDamage.Merge(null, r.Damage?.DealtBySkill), SkillDamage.Merge(null, r.Damage?.ReceivedBySkill),
                AttackerDamage.Merge(null, r.Attackers), [r], r.Damage?.DealtHow ?? HitStats.Empty, r.Damage?.ReceivedHow ?? HitStats.Empty, SkillDamage.Merge(null, r.Damage?.HealBySkill), r.Class);
    }

    /// <summary>Who killed me this hour (Points are my losses, negative; damage as for kills), most recent first.</summary>
    public List<KillTally> Killers() => [.. _killers.Values.OrderByDescending(t => t.Last)];

    public bool AddUnknown(UnmatchedGain g)
    {
        if (!InHour(g.Time)) return false;
        Revision++;
        UnknownCount++;
        UnknownPoints += g.Delta;
        return true;
    }

    /// <summary>Pinned names first (most kills, then most recent), then the rest by most recent kill.</summary>
    public List<KillTally> Sorted() =>
        [.. _tallies.Values.Where(IsPinned).OrderByDescending(t => t.Count).ThenByDescending(t => t.Last),
         .. _tallies.Values.Where(t => !IsPinned(t)).OrderByDescending(t => t.Last)];

    private bool InHour(DateTimeOffset time)
    {
        Roll(time);
        return Kst.HourStart(time) == Hour;
    }
}
