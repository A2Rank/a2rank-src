using Aion2Meter.Protocol;

namespace Aion2Meter.Core;

/// <summary>
/// Tells, by the name of the skill, when a debuff of an enemy lands on me: 고통의 연쇄, 마르쿠탄의 분노, 봉인 화살 ... The user picks names (settings, "디버프 알림"); what the debuff does
/// is of no interest here. Two things in the packets say that one landed:
/// <list type="bullet">
/// <item>the message that an effect landed (<see cref="EffectAppliedMessage"/>, kind 13 = an effect with a time): the effect id names the skill that gave it; a generic effect (기절 ...) has no
/// skill of its own and is named by the hit of the same enemy that came just before it;</item>
/// <item>the ticks of a damage-over-time debuff (<see cref="PeriodicMessage"/>): the skill key of the tick.</item>
/// </list>
/// The same debuff is often told twice (a stun and a heal reduction of one skill, or a tick and the landing): one alert per name within <see cref="Debounce"/>. Ticks come every 0.5 s,
/// so a tick of the same debuff after a gap longer than <see cref="Rearm"/> is a new application. Hostile = a monster or NPC, or a player of the other race.
/// </summary>
public sealed class DebuffWatcher
{
    /// <summary>A tick of the same debuff after a gap longer than this is a new application (the ticks come every 0.5 s).</summary>
    public static readonly TimeSpan Rearm = TimeSpan.FromSeconds(2);
    /// <summary>The same name landing again within this time is one application.</summary>
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1);
    /// <summary>An effect with no skill of its own takes the name of the hit that came this shortly before it.</summary>
    private static readonly TimeSpan HitWindow = TimeSpan.FromSeconds(1);
    private const int MaxSeen = 200, MaxHits = 256;

    /// <summary>
    /// The debuffs known by name, per class (from the effects other players put on enemies in the recordings of 2026-11-23/24; heals, shields and buffs left out): offered in the settings
    /// before they have landed on me. Names are those the watcher reports (the skill that gave the effect, or 기절, 허점 ... for the effects of no skill).
    /// </summary>
    public static readonly IReadOnlyList<(string Group, string[] Names)> Catalog =
    [
        ("공통 효과", ["기절", "둔화", "속박", "봉인", "실명", "공포", "허점", "전투 속도 감소"]),
        ("검성", ["공중 결박", "도약 찍기", "격노 폭발", "유린의 검", "분쇄 파동", "광폭 파동"]),
        ("수호성", ["쇠약의 맹타", "징벌", "포획", "처형의 검", "도발"]),
        ("살성", ["독 바르기", "그림자 낙하"]),
        ("궁성", ["송곳 화살", "그리폰 화살", "봉인 화살"]),
        ("마도성", ["불꽃 폭발", "지연 폭발", "얼음 사슬", "불의 표식", "불꽃 작살", "빙결 폭발", "빙결", "지옥의 화염"]),
        ("정령성", ["공간 지배", "협공: 저주", "협공: 부식", "저주의 구름", "원소 융합"]),
        ("치유성", ["회복 차단", "약화의 낙인", "고통의 연쇄", "파멸의 목소리", "대지의 징벌"]),
        ("호법성", ["대지의 약속", "마르쿠탄의 분노", "분쇄격", "파열격", "돌진 격파", "파동격"]),
        ("권성", ["질풍격", "질풍격[폭주]"]),
    ];

    /// <summary>All the names of the <see cref="Catalog"/>.</summary>
    public static IEnumerable<string> CommonNames => Catalog.SelectMany(g => g.Names);

    /// <summary>Ground fields (장판), not debuffs: standing in one is not told (the user: field alerts are not wanted).</summary>
    public static readonly HashSet<string> FieldNames = ["불의 장벽", "냉기 폭풍"];

    private readonly Dictionary<string, DateTimeOffset> _lastTick = [];
    private readonly Dictionary<string, DateTimeOffset> _lastAlert = [];
    private readonly Dictionary<string, DateTimeOffset> _seen = [];
    private readonly Dictionary<uint, (string Name, DateTimeOffset Time)> _hits = [];

    /// <summary>The debuffs (by name) that have landed on me since the start, and when last.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> Seen => _seen;

    /// <summary>Of the name the last <see cref="Observe"/> returned: true when the debuff landed on me, false when on the one I aim at.</summary>
    public bool LastOnMe { get; private set; }

    /// <summary>
    /// Looks at one message; returns the name of the debuff when it just landed (a new application) on me or, with <paramref name="aimed"/> (the entity I aim at, 0/null: none), on that one:
    /// there it counts when the one who gave it and the one who got it are hostile to each other (a debuff of mine or of a friend on an enemy, an enemy's on a friend I aim at).
    /// </summary>
    public string? Observe(GameMessage message, uint? self, DateTimeOffset time, Func<uint, bool> isEnemy, uint? aimed = null)
    {
        if (self is not uint me) return null;
        uint target = aimed is uint a0 && a0 != me ? a0 : 0;
        // me: from an enemy (my own summons are not); the aimed one: from a side the other way round
        bool Lands(uint to, uint from) => from != to && from != 0 && (to == me ? from != me && isEnemy(from) : to == target && isEnemy(from) != isEnemy(to));
        switch (message)
        {
            case DamageMessage { Flags: var flags } d when (flags & 0x02) != 0 && d.Source != d.Target && (d.Target == me || d.Target == target || d.Source == me):
                if (_hits.Count >= MaxHits) _hits.Clear();
                _hits[d.Source] = (Skills.Name(d.SkillKey), time);   // what the next effect from this one is named after, if it has no skill of its own
                return null;
            case PeriodicMessage { Damage: true } p when (p.Target == me && p.Source != me && p.Source != 0) || (target != 0 && Lands(p.Target, p.Source)):
            {
                uint key = Skills.Canonical(p.SkillKey);
                if (Skills.IsShield(key) || Skills.IsHelpfulOnly(key)) return null;   // a shield or a heal that arrives flagged as damage
                string name = Skills.Name(key);
                string tickKey = p.Target == me ? name : $"{name}@{p.Target}";
                bool again = _lastTick.TryGetValue(tickKey, out var last) && time - last <= Rearm;
                _lastTick[tickKey] = time;
                return Told(Landed(name, time, notify: !again, remember: p.Target == me), p.Target == me);
            }
            case EffectAppliedMessage { Kind: 0x13 } a when Lands(a.Target, a.Source):
                return Told(Landed(NameOf(a, time), time, notify: true, remember: a.Target == me), a.Target == me);
            default:
                return null;
        }
    }

    /// <summary>The name of an effect: the one given by hand (기절, 허점 ...), else its skill's, else the skill of the hit of the same enemy just before it, else "효과 id".</summary>
    private string NameOf(EffectAppliedMessage a, DateTimeOffset time)
    {
        if (EffectNames.OfGeneric(a.EffectId) is { } generic) return generic;
        if (EffectNames.OfSkill(a.EffectId) is { } skill) return skill;
        if (_hits.TryGetValue(a.Source, out var hit) && time - hit.Time <= HitWindow) return hit.Name;
        return $"효과 {a.EffectId}";
    }

    private string? Told(string? name, bool onMe)
    {
        if (name is not null) LastOnMe = onMe;
        return name;
    }

    // remember: it landed on me, so the settings list may offer it (what lands on the one I aim at is not listed: heals and buffs of the other side are among it)
    private string? Landed(string name, DateTimeOffset time, bool notify, bool remember)
    {
        if (FieldNames.Contains(name)) return null;
        if (remember && (_seen.Count < MaxSeen || _seen.ContainsKey(name))) _seen[name] = time;
        if (!notify) return null;
        bool fresh = !_lastAlert.TryGetValue(name, out var last) || time - last > Debounce;
        if (fresh) _lastAlert[name] = time;
        return fresh ? name : null;
    }

    public void Clear()
    {
        _lastTick.Clear();
        _lastAlert.Clear();
        _seen.Clear();
        _hits.Clear();
    }
}
