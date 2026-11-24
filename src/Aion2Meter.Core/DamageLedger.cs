using Aion2Meter.Protocol;

namespace Aion2Meter.Core;

/// <summary>Damage of one skill (<see cref="DamageMessage.SkillKey"/>): number of hits and their total.</summary>
public sealed record SkillDamage(uint Key, int Hits, long Total, HitStats? How = null)
{
    /// <summary>Adds two lists up per skill, biggest total first.</summary>
    public static IReadOnlyList<SkillDamage> Merge(IReadOnlyList<SkillDamage>? a, IReadOnlyList<SkillDamage>? b) =>
        [.. (a ?? []).Concat(b ?? []).GroupBy(s => Skills.Canonical(s.Key))
            .Select(g => new SkillDamage(g.Key, g.Sum(s => s.Hits), g.Sum(s => s.Total), g.Aggregate((HitStats?)null, (acc, s) => HitStats.Merge(acc, s.How)) is { IsEmpty: false } how ? how : null))
            .OrderByDescending(s => s.Total).ThenBy(s => s.Key)];

    /// <summary>"6563:3:12345:3,2,1,0,2,1,3,1,0,0;69ad:2:999" (key hex, hits, total, how the hits landed) - for the database.</summary>
    public static string Encode(IReadOnlyList<SkillDamage>? list) =>
        string.Join(';', (list ?? []).Select(s => $"{s.Key:x}:{s.Hits}:{s.Total}" + (s.How is { IsEmpty: false } h ? ":" + h.Encode() : "")));

    /// <summary>The inverse of <see cref="Encode"/>; anything that does not parse is skipped.</summary>
    public static IReadOnlyList<SkillDamage> Decode(string? text)
    {
        var list = new List<SkillDamage>();
        foreach (var part in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split(':');
            if (f.Length is 3 or 4 && uint.TryParse(f[0], System.Globalization.NumberStyles.HexNumber, null, out var key)
                && int.TryParse(f[1], out var hits) && long.TryParse(f[2], out var total))
                list.Add(new SkillDamage(key, hits, total, f.Length == 4 ? HitStats.Decode(f[3]) is { IsEmpty: false } how ? how : null : null));
        }
        return list;
    }
}

/// <summary>
/// One source of the damage I took shortly before I died: a player (or all monsters and NPCs together), with the total and its skills.
/// </summary>
/// <param name="Name">The player's name; empty when it was not known (then <paramref name="Entity"/> is all there is) or for the NPC line.</param>
public sealed record AttackerDamage(string Name, int Server, uint Entity, bool IsNpc, long Total, int Hits, IReadOnlyList<SkillDamage> Skills)
{
    /// <summary>Adds attacker lists up (the same player, or the NPC line), biggest total first.</summary>
    public static IReadOnlyList<AttackerDamage> Merge(IReadOnlyList<AttackerDamage>? a, IReadOnlyList<AttackerDamage>? b) =>
        [.. (a ?? []).Concat(b ?? [])
            .GroupBy(x => x.IsNpc ? "npc" : x.Name.Length > 0 ? $"{x.Name}|{x.Server}" : $"#{x.Entity}")
            .Select(g => g.First() with
            {
                Total = g.Sum(x => x.Total), Hits = g.Sum(x => x.Hits),
                Skills = g.Aggregate((IReadOnlyList<SkillDamage>)[], (acc, x) => SkillDamage.Merge(acc, x.Skills)),
            })
            .OrderByDescending(x => x.Total)];
}

/// <summary>
/// Damage between me and one other player over a fight: what I did to them and what they did to me, in total and per skill.
/// The skill lists are empty when nothing was seen (never null from the ledger; null only when a record was made without them).
/// </summary>
public sealed record DamageStats(long Dealt, long Received, int DealtHits, int ReceivedHits,
    IReadOnlyList<SkillDamage>? DealtBySkill = null, IReadOnlyList<SkillDamage>? ReceivedBySkill = null,
    HitStats? DealtHow = null, HitStats? ReceivedHow = null, IReadOnlyList<SkillDamage>? HealBySkill = null);

/// <summary>
/// Adds up the damage (04 38, docs/PROTOCOL.md §3.6) between me and every other player I fight, per opponent, until the opponent
/// is credited for a kill or I die to them (<see cref="Take"/>). Monsters and NPCs (entity number from 16384, see
/// <see cref="Entities"/>) and damage between other people are ignored. Entity numbers belong to one connection / zone: <see cref="Clear"/> on a change.
/// </summary>
public sealed class DamageLedger
{
    /// <summary>A fight whose last hit is older than this is not what a kill now belongs to.</summary>
    public static readonly TimeSpan Stale = TimeSpan.FromMinutes(5);
    private const int MaxOpponents = 256;

    private sealed class Entry
    {
        public long Dealt, Received;
        public int DealtHits, ReceivedHits;
        public HitStats DealtHow = HitStats.Empty, ReceivedHow = HitStats.Empty;
        public readonly Dictionary<uint, (int Hits, long Total, HitStats How)> DealtSkills = [], ReceivedSkills = [];
        public DateTimeOffset First, Last;
    }

    /// <summary>The window before my death whose damage taken is attributed to it.</summary>
    public static readonly TimeSpan DeathWindow = TimeSpan.FromSeconds(30);
    private const int MaxRecent = 4000;

    private readonly Dictionary<uint, Entry> _entries = [];
    // Every hit I took lately (also from monsters), kept briefly to tell who hurt me before I died.
    private readonly List<(DateTimeOffset Time, uint Source, uint Skill, long Total, HitStats How)> _recent = [];

    // Life I received lately (from myself or others), kept for the fights that include it. Action points are not life.
    private readonly List<(DateTimeOffset Time, uint Skill, long Amount)> _heals = [];
    private const int MaxHeals = 20000;
    private static readonly TimeSpan HealKeep = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Effects (skill key + the low half of the stamp, one per effect of a skill) that gave a life amount that is not a multiple of 100:
    /// recoveries of action points and of mana arrive as 100 times the number shown ("200의 행동력" = 20000), life amounts are plain numbers.
    /// </summary>
    private readonly HashSet<(uint Skill, uint Effect)> _lifeEffects = [];

    /// <summary>Not an effect of a recovery message (the 재생 stat, over-time ticks): no guess from the amount.</summary>
    private const uint NoEffect = uint.MaxValue;

    /// <summary>
    /// A recovery of <paramref name="amount"/> for me (04 38 without the damage flag, or a 05 38 tick of an effect on myself).
    /// The message does not say what is restored. Life, action points (행동력) and mana (정신력) look the same, except that the points come as 100 times the
    /// number shown, so an effect whose amount is a multiple of 100 and that never gave another kind of number is points, not life
    /// (checked on 40 recordings: 생명의 권능 20000, 충격 해제 100000, 보호의 방패 26000, and two skills not named yet: 25000 and 100000; of the life
    /// effects about 1 in 100 messages is a multiple of 100 by chance, and once an effect has shown a plain number it counts as life for good).
    /// </summary>
    private void AddHeal(uint skill, long amount, DateTimeOffset time, uint effect = NoEffect)
    {
        if (!Skills.IsLifeRecovery(skill)) return;   // known not to be life (보호의 방패, 충격 해제)
        if (effect != NoEffect)
        {
            if (amount % 100 != 0) _lifeEffects.Add((skill, effect));
            else if (!_lifeEffects.Contains((skill, effect))) return;   // points (or the 1 in 100 round life amount of an effect not seen yet)
        }
        _heals.Add((time, skill, amount));
        if (_heals.Count > MaxHeals) _heals.RemoveRange(0, _heals.Count - MaxHeals);
        else if (_heals.Count > 0 && time - _heals[0].Time > HealKeep) _heals.RemoveAll(h => time - h.Time > HealKeep);
    }

    /// <summary>
    /// An over-time effect tick (05 38). Type 0B: a heal when it is on me from me (재생의 빛). Type 0A: damage over time of a debuff; it counts
    /// like a hit between me (or my summons) and another player, in the total and under the skill, but not as a hit (the in-game table does not count it as one either).
    /// </summary>
    public void Add(PeriodicMessage p, uint? self, DateTimeOffset time)
    {
        if (p.NoAmount) return;   // no amount read (a tick a shield took, or a shape of tail that is not understood): the effect is there, no damage to add
        if (Skills.IsShield(p.SkillKey)) return;   // a shield is neither damage nor life (유스티엘의 권능 of a party member arrived as damage on me)
        if (!p.Damage)
        {
            if (self is uint me0 && p.Target == me0 && p.Source == me0) AddHeal(Skills.Canonical(p.SkillKey), p.Amount, time);
            return;
        }
        if (Skills.IsHelpfulOnly(p.SkillKey)) return;   // 면죄, 십자 방어, 대자연의 숨결, 재생의 빛 of a party member arrive flagged as damage: they only heal or shield
        if (self is not uint me || p.Source == p.Target) return;
        bool dealt = p.Source == me || _summons.Contains(p.Source);
        uint key = Skills.Canonical(Skills.SpiritKey(_templates.GetValueOrDefault(p.Source), p.SkillKey));
        if (!dealt && p.Target == me)
        {
            _recent.Add((time, p.Source, key, p.Amount, HitStats.Empty));
            if (_recent.Count > MaxRecent) _recent.RemoveRange(0, _recent.Count - MaxRecent);
        }
        if (!dealt && p.Target != me) return;
        uint other = dealt ? p.Target : p.Source;
        if (!Entities.IsPlayer(other)) return;
        Each(other, time, e =>
        {
            var skills = dealt ? e.DealtSkills : e.ReceivedSkills;
            var s = skills.GetValueOrDefault(key, (0, 0L, HitStats.Empty));
            skills[key] = (s.Item1, s.Item2 + p.Amount, s.Item3);
            if (dealt) e.Dealt += p.Amount; else e.Received += p.Amount;
            e.Last = time;
        });
    }

    /// <summary>
    /// An attack that did not land (회피), in either direction: an attack of a player on me that I dodged (counted in what they did to me), or an attack of mine that
    /// the player dodged (counted in what I did to them). Players only: the details of a fight are about players, mobs are not shown.
    /// </summary>
    public void Add(DodgeMessage d, uint? self, DateTimeOffset time)
    {
        if (self is not uint me || d.Source == d.Target) return;
        // (the skill row of the dodged attack gets the dodge too: a row with no hit of its own, only dodges, shows 회피 100%)
        uint key = Skills.Canonical(Skills.SpiritKey(_templates.GetValueOrDefault(d.Source), d.SkillKey));
        if (d.Target == me && Entities.IsPlayer(d.Source))
        {
            // (an attack of theirs that I dodged also belongs to who hurt me before a death: no damage, a dodge in how it landed)
            _recent.Add((time, d.Source, key, 0, HitStats.Empty.AddDodge()));
            if (_recent.Count > MaxRecent) _recent.RemoveRange(0, _recent.Count - MaxRecent);
            Each(d.Source, time, e =>
            {
                e.ReceivedHow = e.ReceivedHow.AddDodge();
                var s = e.ReceivedSkills.GetValueOrDefault(key, (0, 0L, HitStats.Empty));
                e.ReceivedSkills[key] = (s.Item1, s.Item2, s.Item3.AddDodge());
                e.Last = time;
            });
        }
        else if (d.Source == me && Entities.IsPlayer(d.Target))
        {
            Each(d.Target, time, e =>
            {
                e.DealtHow = e.DealtHow.AddDodge();
                var s = e.DealtSkills.GetValueOrDefault(key, (0, 0L, HitStats.Empty));
                e.DealtSkills[key] = (s.Item1, s.Item2, s.Item3.AddDodge());
                e.Last = time;
            });
        }
    }

    // The line of an opponent; with <paramref name="idle"/> a line without a hit for that long starts over.
    private static Entry EntryIn(Dictionary<uint, Entry> entries, uint other, DateTimeOffset time, TimeSpan? idle = null)
    {
        if (entries.TryGetValue(other, out var e) && (idle is null || time - e.Last <= idle)) return e;
        if (e is null && entries.Count >= MaxOpponents) entries.Remove(entries.MinBy(x => x.Value.Last).Key);
        return entries[other] = new Entry { First = time };
    }

    /// <summary>Does <paramref name="apply"/> on the fight with <paramref name="other"/> in both books: the one a kill or death closes, and the live board.</summary>
    private void Each(uint other, DateTimeOffset time, Action<Entry> apply)
    {
        apply(EntryIn(_entries, other, time));
        apply(EntryIn(_live, other, time, LiveIdle));
    }

    // ---- the live board: the same numbers, but a kill or death does not close a line (it stays while the fight goes on)

    /// <summary>A line of the live board that has seen no hit for this long starts over with the next one (and is not shown any more).</summary>
    public static readonly TimeSpan LiveIdle = TimeSpan.FromSeconds(120);
    private readonly Dictionary<uint, Entry> _live = [];

    /// <summary>One opponent of the live board: what we did to each other since the fight began (or the last <see cref="ClearLive"/>).</summary>
    public sealed record LiveOpponent(uint Entity, string Name, int Server, int Class, long Dealt, long Received, int DealtHits, int ReceivedHits,
        IReadOnlyList<SkillDamage> DealtSkills, IReadOnlyList<SkillDamage> ReceivedSkills, HitStats DealtHow, HitStats ReceivedHow, DateTimeOffset First, DateTimeOffset Last);

    /// <summary>
    /// The players I am (or just was) fighting, the ones with the most damage between us first (what I did plus what I took): with many opponents
    /// the top of this list is who matters. <paramref name="who"/> finds name, server and class from the entity number (null: not known).
    /// </summary>
    public IReadOnlyList<LiveOpponent> Live(DateTimeOffset now, Func<uint, (string Name, int Server, int Class)?> who)
    {
        foreach (var gone in _live.Where(x => now - x.Value.Last > LiveIdle).Select(x => x.Key).ToList()) _live.Remove(gone);
        return [.. _live.Select(x =>
        {
            var e = x.Value;
            var w = who(x.Key);
            return new LiveOpponent(x.Key, w?.Name ?? "", w?.Server ?? 0, w?.Class ?? 0, e.Dealt, e.Received, e.DealtHits, e.ReceivedHits,
                List(e.DealtSkills), List(e.ReceivedSkills), e.DealtHow, e.ReceivedHow, e.First, e.Last);
        }).OrderByDescending(o => o.Dealt + o.Received).ThenBy(o => o.Entity)];
    }

    /// <summary>Empties the live board (the damage kept for the next kill or death is not touched).</summary>
    public void ClearLive() => _live.Clear();

    // Entities of my summons (신성한 기운 ...): what they do counts as mine.
    private readonly HashSet<uint> _summons = [];
    private const int MaxSummons = 256;

    /// <summary>A summon of mine appeared (its spawn message named me as the owner).</summary>
    public void AddSummon(uint entity)
    {
        if (_summons.Count >= MaxSummons) _summons.Clear();
        _summons.Add(entity);
    }

    // What kind each spawned thing is (the template of its 41 36 message): elemental spirits share skill numbers, the kind tells their skills apart.
    private readonly Dictionary<uint, uint> _templates = [];
    private const int MaxTemplates = 2048;

    /// <summary>Something appeared (41 36): remember what kind it is.</summary>
    public void NoteSpawn(uint entity, uint template)
    {
        if (template == 0 || Entities.IsPlayer(entity)) return;
        if (_templates.Count >= MaxTemplates) _templates.Clear();
        _templates[entity] = template;
    }

    // 천벌 hits: key 65a0, and 0.05-0.15 s later 6591 on the main target and on the summons next to it. Since 심판의 번개 was upgraded its key is 6591 too.
    private readonly Dictionary<uint, DateTimeOffset> _lastCheonbeol = [];
    private readonly Dictionary<(uint Source, uint Key), DateTimeOffset> _lastCast = [];
    private static readonly TimeSpan CheonbeolFollowUp = TimeSpan.FromMilliseconds(250);

    /// <summary>The skill key of a hit: 6591 is 천벌 when its caster hit with 65a0 just before, otherwise 심판의 번개 (6590).</summary>
    private uint ResolveKey(DamageMessage d, DateTimeOffset time)
    {
        // One cast, two skills under one key (Skills.FollowUps): the second hit shortly after the first is the other skill.
        if (Skills.FollowUps.TryGetValue(d.SkillKey, out var followUp))
        {
            if (_lastCast.TryGetValue((d.Source, d.SkillKey), out var first) && time - first >= TimeSpan.Zero && time - first <= Skills.FollowUpWindow) return followUp;
            _lastCast[(d.Source, d.SkillKey)] = time;
            return d.SkillKey;
        }
        // 67b6: 대지의 축복 (a proc on every hit: flags 06 or 04) and 대지의 징벌 (a cast every ~22 s: flags 16 or 36, the multi-hit bit 0x10), 2026-11-21 02:38 table: 67b6 3 hits 1,038,101 with the bit = 징벌, 65 hits 2,121,607 without = 축복
        if (d.SkillKey == 0x67B6 && (d.Flags & 0x10) != 0) return 0x1067B6;
        if (d.SkillKey == 0x65A0) _lastCheonbeol[d.Source] = time;
        else if (d.SkillKey == 0x6591)
            return _lastCheonbeol.TryGetValue(d.Source, out var t) && time - t >= TimeSpan.Zero && time - t <= CheonbeolFollowUp ? 0x65A0u : 0x6590u;
        return Skills.SpiritKey(_templates.GetValueOrDefault(d.Source), d.SkillKey);
    }

    public void Add(DamageMessage d, uint? self, DateTimeOffset time)
    {
        // Flag 0x0C is a fixed extra amount of a skill (docs/PROTOCOL.md 3.9): like a damage-over-time tick it counts in the total and the skill, not as a hit.
        if (d.Flags == 0x0C) { Add(new PeriodicMessage(d.Target, d.Source, d.SkillKey, d.Total, true), self, time); return; }
        if (self is uint who && (d.Flags & 0x02) == 0 && d.Target == who) AddHeal(Skills.Canonical(d.SkillKey), d.Total, time, d.Stamp & 0xFFFF);   // recovery
        // My 재생 stat triggered on a hit I took: it restores 20% of that hit.
        if (self is uint to && (d.Flags & 0x02) != 0 && d.Target == to && d.Source != to && d.Regained > 0) AddHeal(Skills.RegenStat, d.Regained, time);
        if (self is not uint me || (d.Flags & 0x02) == 0 || d.Source == d.Target) return;   // 0x02 = damage (without it: recovery)
        bool dealt = d.Source == me || _summons.Contains(d.Source);
        uint key = Skills.Canonical(ResolveKey(d, time));
        if (!dealt && d.Target == me)
        {
            _recent.Add((time, d.Source, key, d.Total, HitStats.Empty.Add(d)));
            if (_recent.Count > MaxRecent) _recent.RemoveRange(0, _recent.Count - MaxRecent);
        }
        if (!dealt && d.Target != me) return;
        uint other = dealt ? d.Target : d.Source;
        if (!Entities.IsPlayer(other)) return;
        // (the key is worked out once: a follow-up hit is told from the first one by what came before it)
        Each(other, time, e =>
        {
            var skills = dealt ? e.DealtSkills : e.ReceivedSkills;
            var s = skills.GetValueOrDefault(key, (0, 0L, HitStats.Empty));
            skills[key] = (s.Item1 + 1, s.Item2 + d.Total, s.Item3.Add(d));
            if (dealt) { e.Dealt += d.Total; e.DealtHits++; e.DealtHow = e.DealtHow.Add(d); }
            else { e.Received += d.Total; e.ReceivedHits++; e.ReceivedHow = e.ReceivedHow.Add(d); }
            e.Last = time;
        });
    }

    /// <summary>The damage exchanged with <paramref name="opponent"/> since the last kill of them; starts over afterwards. Null when there was none.</summary>
    public DamageStats? Take(uint opponent, DateTimeOffset now)
    {
        if (!_entries.Remove(opponent, out var e) || now - e.Last > Stale) return null;
        // The life I received from the first blow of this fight until now.
        var heals = SkillDamage.Merge([.. _heals.Where(h => h.Time >= e.First && h.Time <= now).Select(h => new SkillDamage(h.Skill, 1, h.Amount))], null);
        return new DamageStats(e.Dealt, e.Received, e.DealtHits, e.ReceivedHits, List(e.DealtSkills), List(e.ReceivedSkills), e.DealtHow, e.ReceivedHow, heals);
    }

    /// <summary>
    /// Who hurt me in the <see cref="DeathWindow"/> before <paramref name="now"/> (I just died), per attacker, biggest first; then forgets those hits.
    /// <paramref name="name"/> finds a player's name and server from the entity number (null when unknown). Monsters and NPCs are one line.
    /// </summary>
    public IReadOnlyList<AttackerDamage> TakeAttackers(DateTimeOffset now, Func<uint, (string Name, int Server)?> name)
    {
        var hits = _recent.Where(h => now - h.Time <= DeathWindow && h.Time <= now).ToList();
        _recent.Clear();
        var list = new List<AttackerDamage>();
        foreach (var g in hits.GroupBy(h => Entities.IsPlayer(h.Source) ? h.Source : 0u))
        {
            var skills = g.GroupBy(h => h.Skill).Select(s => new SkillDamage(s.Key, s.Count(h => h.How.Dodge == 0), s.Sum(h => h.Total),
                    s.Aggregate((HitStats?)null, (acc, h) => HitStats.Merge(acc, h.How)) is { IsEmpty: false } how ? how : null))
                .OrderByDescending(s => s.Total).ThenBy(s => s.Key).ToList();
            bool npc = g.Key == 0;
            var who = npc ? null : name(g.Key);
            list.Add(new AttackerDamage(who?.Name ?? "", who?.Server ?? 0, g.Key, npc, g.Sum(h => h.Total), g.Count(h => h.How.Dodge == 0), skills));
        }
        return [.. list.OrderByDescending(a => a.Total)];
    }

    private static IReadOnlyList<SkillDamage> List(Dictionary<uint, (int Hits, long Total, HitStats How)> skills) =>
        [.. skills.Select(x => new SkillDamage(x.Key, x.Value.Hits, x.Value.Total, x.Value.How)).OrderByDescending(s => s.Total).ThenBy(s => s.Key)];

    public void Clear()
    {
        _entries.Clear();
        _live.Clear();
        _recent.Clear();
        _heals.Clear();
        _summons.Clear();
        _templates.Clear();
        _lastCheonbeol.Clear();
        _lastCast.Clear();
    }
}
