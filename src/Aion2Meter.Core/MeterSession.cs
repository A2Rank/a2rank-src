using System.Globalization;
using System.Text.Json;
using Aion2Meter.Protocol;

namespace Aion2Meter.Core;

/// <summary>
/// Everything the meter knows, fed with capture events in arrival order and a periodic <see cref="Tick"/>.
/// Periods and kill lists belong to the character being played (BEHAVIOR.md §1.8).
/// Not thread-safe: the app calls it from one thread.
/// </summary>
public sealed class MeterSession
{
    private const string RankingOffsetKey = "ranking_offset", CharacterKeyName = "character", TotalKeyPrefix = "total:", RankKeyPrefix = "rank:";
    /// <summary>Ranking deadlines this close to the saved one are jitter, not a new reset time.</summary>
    private const int OffsetJitterSeconds = 2;

    private readonly MeterStore _store;
    private readonly int _pinThreshold;

    public MeterSession(MeterStore store, ResetMode mode, int pinThreshold, DateTimeOffset now)
    {
        _store = store;
        _pinThreshold = pinThreshold;
        Mode = mode;
        if (int.TryParse(store.Get(RankingOffsetKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int saved))
            RankingOffset = saved;
        // Until the game says who I am, assume the character played last.
        Character = CharacterKey.Parse(store.Get(CharacterKeyName));
        if (Character is not null) Matcher.SetSelf(Character.Name, Servers.RaceOf(Character.Server), Character.Server);
        (Buckets, Kills) = Load(now);
        Rank = LoadRank();
        _windows = new BucketBook(RankingOffset ?? 0, [], now);
    }

    // ---- Window records ("구간 기록"): whole hours of the game's ranking clock, whatever the display mode is. ----

    /// <summary>The map's boss list was sent (01 91): the slots with their next spawn (epoch ms) and when we saw it.</summary>

    /// <summary>A hour that was measured from reset to reset is told here (once). Names of other players are not part of it.</summary>
    public event Action<WindowRecord>? WindowClosed;

    /// <summary>Records of the previous period are made this long after it ended, so the last kills and unmatched gains are in.</summary>
    private static readonly TimeSpan WindowSettle = TimeSpan.FromSeconds(10);
    private BucketBook _windows;
    /// <summary>My total before the first and after the last change of a period, and when they happened.</summary>
    private sealed record PeriodSpan(ulong StartTotal, ulong EndTotal, DateTimeOffset First, DateTimeOffset Last);
    private readonly Dictionary<Bucket, PeriodSpan> _windowTotals = [];
    private int _windowsDone;

    public KillMatcher Matcher { get; } = new();
    public PointTracker Points { get; } = new();
    public PartyTracker Party { get; } = new();
    public BucketBook Buckets { get; private set; }
    public HourlyKillList Kills { get; private set; }
    /// <summary>The character being measured; null before any was ever seen.</summary>
    public CharacterKey? Character { get; private set; }
    /// <summary>The game confirmed <see cref="Character"/> since the app started (otherwise it is the last one played).</summary>
    public bool CharacterConfirmed { get; private set; }
    public ResetMode Mode { get; private set; }
    /// <summary>Seconds after the hour of the game's ranking reset, once a ranking message was seen.</summary>
    public int? RankingOffset { get; private set; }
    public DateTimeOffset? RankingDeadline { get; private set; }
    public DateTimeOffset? LastMessage { get; private set; }
    public bool Measuring => Points.Measuring;

    /// <summary>Something shown on screen may have changed.</summary>
    public event Action? Changed;

    private string CharacterId => Character?.Id ?? "";
    private int CurrentOffset => Mode == ResetMode.RankingReset ? RankingOffset ?? 0 : 0;

    public void Start(DateTimeOffset now)
    {
        Points.Start(RememberedTotal(Character));
        Buckets.Resume(now);
        _windows.Resume(now);
        Changed?.Invoke();
    }

    public void Stop(DateTimeOffset now)
    {
        Points.Stop();
        Buckets.Pause(now);
        _windows.Pause(now);
        _windows.CloseCurrent(now);   // what was counted so far is a record of its own; measuring on opens another one
        FlushWindows(now, force: true);
        Changed?.Invoke();
    }

    /// <summary>The app is closing: the period in progress is made a record too (it would be lost otherwise).</summary>
    public void Shutdown(DateTimeOffset now)
    {
        _windows.Pause(now);
        _windows.CloseCurrent(now);
        FlushWindows(now, force: true);
    }

    public void SetMode(ResetMode mode, DateTimeOffset now)
    {
        Mode = mode;
        Buckets.SetOffset(CurrentOffset, now);
        Changed?.Invoke();
    }

    public Totals Today(DateTimeOffset now) => Buckets.DayTotal(Kst.Day(now));

    public void Handle(CaptureEvent e)
    {
        Points.Seen(e.Token, e.Time);
        switch (e)
        {
            case MessageEvent { Message: var message }:
                LastMessage = e.Time;
                Handle(message, e.Token, e.Time);
                break;
            case GapEvent:
                Points.Lost(e.Token.Flow);
                break;
            case ClosedEvent:
                if (Points.ActiveFlow == e.Token.Flow)
                {
                    Matcher.Expire(e.Time);
                    Drain();
                    Matcher.ClearConnection(); // entity numbers belong to the closed connection
                    Party.Clear();
                }
                Points.Lost(e.Token.Flow);
                break;
        }
    }

    public void Tick(DateTimeOffset now)
    {
        Buckets.Roll(now);
        _windows.Roll(now);
        FlushWindows(now);
        bool newHour = Kills.Roll(now);
        Matcher.Expire(now);
        bool party = Party.Expire(now);
        if (Drain() || newHour || party) Changed?.Invoke();
    }

    private void Handle(GameMessage message, StreamToken token, DateTimeOffset time)
    {
        if (message is SpawnMessage { Template: not 0 } spawned) Matcher.Damage.NoteSpawn(spawned.Entity, spawned.Template);
        switch (message)
        {
            case EnterWorldMessage m:
                Matcher.EnterWorld(m.Entity, m.Name, Servers.RaceOf(m.Server), m.Server);
                Drain();
                SwitchCharacter(new CharacterKey(m.Server, m.Name), time);
                // The enter message tells the current total: the first change after logging in is then counted, not only a baseline.
                if (m.Points is ulong total) OnPoint(token, total, time, exact: true);
                break;
            case IdentityMessage m:
                foreach (var k in Matcher.OnIdentity(m)) Record(k);
                Changed?.Invoke();
                break;
            case DeathMessage m:
                Matcher.OnDeath(m, time);
                break;
            case SpawnMessage m when !Entities.IsPlayer(m.Entity) && ((m.Owner.Length > 0 && m.Owner == Matcher.SelfName) || (m.OwnerEntity != 0 && m.OwnerEntity == Matcher.SelfEntity)):
                Matcher.Damage.AddSummon(m.Entity);
                break;
            case DodgeMessage m:
                Matcher.Damage.Add(m, Matcher.SelfEntity, time);
                break;
            case PeriodicMessage m:
                Matcher.Damage.Add(m, Matcher.SelfEntity, time);
                break;
            case DuelMessage m:
                if (Matcher.OnDuel(m, time) is { } duel) { Record(duel); Changed?.Invoke(); }
                break;
            case DamageMessage m:
                Matcher.Damage.Add(m, Matcher.SelfEntity, time);
                break;
            case PartyMessage m:
                if ((m is PartyStatusMessage st ? Party.Observe(st, time) : Party.Handle(m, Matcher.SelfName))) Changed?.Invoke();
                break;
            case RankingMessage m:
                OnRanking(m, time);
                break;
            case PointMessage m:
                OnPoint(token, m.Total, time);
                break;
        }
    }

    /// <summary>Login, character change or zone move told us who is playing.</summary>
    /// <param name="inGameServer">The key's server is the in-game one (enter-world), not the ranking's code.</param>
    /// <param name="key">Server 0 when it came from the ranking message (its code is not the in-game server).</param>
    private void SwitchCharacter(CharacterKey key, DateTimeOffset now)
    {
        CharacterConfirmed = true;
        if (key.Server != 0) _store.RenameCharacter(new CharacterKey(0, key.Name).Id, key.Id);
        if (key.Matches(Character))
        {
            // Same character (resurrection, zone move, ranking row): only learn the in-game server.
            if (key.Server != 0 && key.Server != Character!.Server)
            {
                Character = key;
                _store.Set(CharacterKeyName, key.Id);
            }
            Changed?.Invoke();
            return;
        }
        _store.Set(CharacterKeyName, key.Id);
        if (Character is null)
        {
            // Records so far were made before any character was known: they are this one's.
            _store.ClaimUnassigned(key.Id);
            Character = key;
            RememberTotal();
            Changed?.Invoke();
            return;
        }
        Party.Clear(); // the party belonged to the other character
        Buckets.Pause(now);
        _windows.Pause(now);
        _windows.CloseCurrent(now);
        FlushWindows(now, force: true); // what the old character counted so far is the old character's
        Character = key;
        Points.Reset(RememberedTotal(key)); // never diff one character's total against another's
        (Buckets, Kills) = Load(now);
        Rank = LoadRank();
        _windows = new BucketBook(RankingOffset ?? 0, [], now);
        _windowTotals.Clear();
        _windowsDone = 0;
        if (Measuring)
        {
            Buckets.Resume(now);
            _windows.Resume(now);
        }
        Changed?.Invoke();
    }

    private (BucketBook, HourlyKillList) Load(DateTimeOffset now)
    {
        var today = Kst.DayStart(Kst.Day(now));
        var buckets = new BucketBook(CurrentOffset, _store.Buckets(CharacterId, today.AddDays(-1), now.AddHours(1)), now);
        buckets.Changed += SaveBucket;
        var kills = new HourlyKillList(Kills?.Threshold ?? _pinThreshold);
        kills.Roll(now);
        foreach (var k in _store.KillsSince(CharacterId, kills.Hour)) kills.Add(k);
        foreach (var g in _store.UnmatchedSince(CharacterId, kills.Hour)) kills.AddUnknown(g);
        return (buckets, kills);
    }

    /// <summary>Last total seen for a character, to count the first change after entering it again (BEHAVIOR.md §1.2).</summary>
    private ulong? RememberedTotal(CharacterKey? key) =>
        key is not null && ulong.TryParse(_store.Get(TotalKeyPrefix + key.Id), NumberStyles.None, CultureInfo.InvariantCulture, out ulong total) ? total : null;

    private void RememberTotal()
    {
        if (Character is not null && Points.Total is ulong total)
            _store.Set(TotalKeyPrefix + Character.Id, total.ToString(CultureInfo.InvariantCulture));
    }

    private void SaveBucket(Bucket b) => _store.SaveBucket(CharacterId, b);

    /// <summary>My place in the game's ranking as of the last ranking reset (ALL and my class), when seen.</summary>
    public RankStanding? Rank { get; private set; }

    private void UpdateRank(RankingMessage m)
    {
        if (Character is null || m.Entries.Count == 0) return;
        var saved = Rank;
        RankingEntry? me;
        if (!m.IsList)
        {
            me = m.Entries[0];
            if (me.Name != Character.Name) return;
        }
        else
        {
            // Lists carry everyone; find me by the ranking id learned from my own row (names repeat across servers).
            me = saved?.RankingId is ulong id ? m.Entries.FirstOrDefault(e => RankingId(e) == id) : null;
            if (me is null) return;
        }
        var asOf = m.Deadline - BucketBook.Length;
        var r = saved ?? new RankStanding(RankingId(me), null, null, null, null, null, asOf);
        r = r with { RankingId = RankingId(me), AsOf = asOf > r.AsOf ? asOf : r.AsOf };
        r = m.Filter == 0
            ? r with { AllRank = (int)me.Rank, AllTotal = (int)m.Total }
            : r with { ClassRank = (int)me.Rank, ClassTotal = (int)m.Total, ClassFilter = m.Filter };
        if (r == saved) return;
        Rank = r;
        _store.Set(RankKeyPrefix + Character.Id, JsonSerializer.Serialize(r));
        Changed?.Invoke();
    }

    private static ulong RankingId(RankingEntry e) => ((ulong)e.Server << 48) | e.CharacterId;

    private RankStanding? LoadRank()
    {
        if (Character is null || _store.Get(RankKeyPrefix + Character.Id) is not { } json) return null;
        try
        {
            return JsonSerializer.Deserialize<RankStanding>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void OnRanking(RankingMessage m, DateTimeOffset time)
    {
        if (!m.IsList && m.Filter == 0 && m.Entries.Count > 0)
        {
            var me = m.Entries[0];
            SwitchCharacter(new CharacterKey(0, me.Name), time);
            Matcher.SetSelf(me.Name, me.Race, Character!.Server);
        }
        UpdateRank(m);
        RankingDeadline = m.Deadline;
        int offset = BucketBook.Normalize(m.Deadline.ToUnixTimeSeconds());
        if (RankingOffset is int saved && CircularDistance(saved, offset) <= OffsetJitterSeconds) return;
        RankingOffset = offset;
        _store.Set(RankingOffsetKey, offset.ToString(CultureInfo.InvariantCulture));
        _windows.SetOffset(offset, time);
        if (Mode == ResetMode.RankingReset) Buckets.SetOffset(offset, time);
        Changed?.Invoke();
    }

    private void OnPoint(StreamToken token, ulong total, DateTimeOffset time, bool exact = false)
    {
        if (Points.OnPoint(token, total, time, exact) is not { } update) return;
        RememberTotal();
        if (update.Rebased)
        {
            Buckets.Interrupt(time);
            _windows.Interrupt(time);
        }
        if (update.Delta != 0)
        {
            Buckets.Add(update.Delta, time, Math.Max(1, Party.Size));
            var window = _windows.Add(update.Delta, time, Math.Max(1, Party.Size));
            // The period's first change started from the total before it; every change moves its end total.
            ulong before = (ulong)((long)total - update.Delta);
            _windowTotals[window] = _windowTotals.TryGetValue(window, out var seen)
                ? seen with { EndTotal = total, Last = time }
                : new PeriodSpan(before, total, time, time);
            if (Matcher.OnPoint(update.Delta, time) is { } k) Record(k);
        }
        Matcher.Expire(time);
        Drain();
        FlushWindows(time);
        Changed?.Invoke();
    }

    /// <summary>
    /// Makes the records of the periods that are over (and settled), every one in which rank points moved. Periods without any change
    /// are not made (nothing to see, and players who leave the meter on while idle must not fill the server).
    /// </summary>
    /// <param name="force">Also those that ended less than <see cref="WindowSettle"/> ago.</param>
    private void FlushWindows(DateTimeOffset now, bool force = false)
    {
        var closed = _windows.Closed;
        for (; _windowsDone < closed.Count; _windowsDone++)
        {
            var b = closed[_windowsDone];
            if (!force && b.End + WindowSettle > now) break;
            _windowTotals.Remove(b, out var span);
            if (b.Events == 0 || span is null || Character is null) continue;
            WindowClosed?.Invoke(BuildWindow(b, span));
        }
        if (_windowsDone > 0)
        {
            _windows.DropClosed(_windowsDone);
            _windowsDone = 0;
        }
    }

    private WindowRecord BuildWindow(Bucket b, PeriodSpan span)
    {
        int own = BucketBook.Normalize(b.Start.ToUnixTimeSeconds());                    // the second of the hour this period was cut at
        bool aligned = RankingOffset is int reset && CircularDistance(reset, own) <= OffsetJitterSeconds;
        // The whole hour between two of the game's resets, measured from its first second to its last: its totals are the totals at the resets.
        bool complete = aligned && !b.Partial && b.End - b.Start == BucketBook.Length;
        // (a hour cut in two - the measurement stopped and came back - has two periods with the same start: the kills of each begin at its own first change)
        var from = span.First.AddSeconds(-1) > b.Start ? span.First.AddSeconds(-1) : b.Start;
        var kills = _store.KillsSince(CharacterId, from).Where(k => k.Time < b.End)
            .Select(k => new WindowKill(k.Time.ToUnixTimeSeconds(), k.Kind, k.Delta, k.Server, k.Race, k.Name)).ToList();
        long unmatched = _store.UnmatchedSince(CharacterId, from).Where(g => g.Time < b.End).Sum(g => g.Delta);
        var parties = b.Parties.Select(p => new WindowParty(p.Key, p.Value.Gain, p.Value.Loss, p.Value.Events)).ToList();
        return new WindowRecord(CharacterId, Character!.Name, Character.Server, Rank?.RankingId, b.Start, b.End, own, span.First, span.Last,
            span.StartTotal, span.EndTotal, b.Gain, b.Loss, b.Events, aligned, complete, parties, kills, unmatched);
    }

    private void Record(KillRecord k)
    {
        _store.AddKill(CharacterId, k);
        Kills.Add(k);
    }

    private bool Drain()
    {
        var gains = Matcher.TakeUnmatched();
        foreach (var g in gains)
        {
            _store.AddUnmatched(CharacterId, g);
            Kills.AddUnknown(g);
        }
        return gains.Count > 0;
    }

    private static int CircularDistance(int a, int b)
    {
        int d = Math.Abs(a - b);
        return Math.Min(d, 3600 - d);
    }
}

/// <param name="RankingId">My id in ranking rows (server code + character number), to find me in ranking lists.</param>
/// <param name="ClassFilter">Ranking filter of my class.</param>
/// <param name="AsOf">The ranking reset the ranks belong to.</param>
public sealed record RankStanding(ulong RankingId, int? AllRank, int? AllTotal, int? ClassRank, int? ClassTotal, int? ClassFilter,
    DateTimeOffset AsOf);
