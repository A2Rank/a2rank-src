namespace Aion2Meter.Core;

/// <summary>The two field bosses with a timer: 감시자 카이라 and 정령왕 아그로, both at fixed game hours.</summary>
public static class FieldBosses
{
    /// <summary>The game's clock (Korea, no daylight saving): the fixed hours below are in this time.</summary>
    public static readonly TimeSpan GameOffset = TimeSpan.FromHours(9);

    private static readonly int[] KairaHours = [1, 9, 13, 21];
    private static readonly int[] AgroHours = [9, 21];

    /// <summary>The next 감시자 카이라 after <paramref name="now"/>: 01, 09, 13 or 21 o'clock game time (fixed by the game since 2026-11).</summary>
    public static DateTimeOffset NextKaira(DateTimeOffset now) => NextAt(KairaHours, now);

    /// <summary>The next 정령왕 아그로 after <paramref name="now"/>: 09 or 21 o'clock game time (it used to be 12 hours after it was taken).</summary>
    public static DateTimeOffset NextAgro(DateTimeOffset now) => NextAt(AgroHours, now);

    private static DateTimeOffset NextAt(int[] hours, DateTimeOffset now)
    {
        var game = now.ToOffset(GameOffset);
        foreach (int hour in hours)
        {
            var at = new DateTimeOffset(game.Year, game.Month, game.Day, hour, 0, 0, GameOffset);
            if (at > now) return at;
        }
        return new DateTimeOffset(game.Year, game.Month, game.Day, hours[0], 0, 0, GameOffset).AddDays(1);
    }
}

/// <summary>One alert: the boss, the spawn it is about, and how many minutes are left.</summary>
public sealed record BossAlert(string Boss, DateTimeOffset Spawn, int MinutesBefore)
{
    public string Text => $"{Boss} {MinutesBefore}분 전 ({Spawn.ToOffset(FieldBosses.GameOffset):HH:mm})";
}

/// <summary>Tells when a boss alert is due: at each chosen number of minutes (10, 5, 1) before a spawn, each once per spawn (an app started inside the window gets only the latest one).</summary>
public sealed class BossAlerts
{
    /// <summary>What the user can pick: minutes before a spawn.</summary>
    public static readonly int[] Choices = [10, 5, 1];

    public static readonly int[] Default = [10, 5];

    private readonly HashSet<(string Boss, long Spawn, int Minutes)> _fired = [];

    /// <param name="spawns">The next spawn per boss name (null: not known / switched off).</param>
    public List<BossAlert> Poll(IEnumerable<(string Boss, DateTimeOffset? Spawn)> spawns, DateTimeOffset now, IEnumerable<int> minutesBefore)
    {
        var chosen = minutesBefore.Where(Choices.Contains).Distinct().OrderByDescending(x => x).ToArray();   // largest first
        var due = new List<BossAlert>();
        foreach (var (boss, spawn) in spawns)
        {
            if (spawn is not { } at || at <= now) continue;
            int pending = -1;
            foreach (int minutes in chosen)
            {
                if (now < at - TimeSpan.FromMinutes(minutes)) continue;
                if (_fired.Add((boss, at.ToUnixTimeSeconds(), minutes))) pending = minutes;   // the later threshold replaces the earlier one
            }
            // an alert that comes late (app started, threshold switched on inside the window) says the minutes that really are left
            if (pending > 0) due.Add(new BossAlert(boss, at, Math.Min(pending, Math.Max(1, (int)Math.Ceiling((at - now).TotalMinutes)))));
        }
        // keep the set small: spawns long gone
        _fired.RemoveWhere(f => f.Spawn < now.ToUnixTimeSeconds() - 3600);
        return due;
    }
}
