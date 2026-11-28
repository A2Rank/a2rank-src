using System.Text.Json;

namespace Aion2Meter.Core;

/// <summary>The scores of a ranking list the game sent (the top 500 of ALL or of one class, one race), by rank, and the ranking reset it belongs to.</summary>
public sealed record RankList(DateTimeOffset AsOf, IReadOnlyDictionary<int, long> ScoreByRank)
{
    public int Count => ScoreByRank.Count;
    public int LastRank => ScoreByRank.Count == 0 ? 0 : ScoreByRank.Keys.Max();

    /// <summary>"a" = reset time (unix seconds), "r" = ranks, "s" = scores: kept in the database so that the goal can be shown after a restart.</summary>
    public string Encode()
    {
        var ranks = ScoreByRank.Keys.OrderBy(r => r).ToArray();
        return JsonSerializer.Serialize(new Stored(AsOf.ToUnixTimeSeconds(), ranks, ranks.Select(r => ScoreByRank[r]).ToArray()));
    }

    public static RankList? Decode(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var s = JsonSerializer.Deserialize<Stored>(json);
            if (s is null || s.R.Length != s.S.Length) return null;
            var map = new Dictionary<int, long>();
            for (int i = 0; i < s.R.Length; i++) map[s.R[i]] = s.S[i];
            return new RankList(DateTimeOffset.FromUnixTimeSeconds(s.A), map);
        }
        catch (JsonException) { return null; }
    }

    private sealed record Stored(long A, int[] R, long[] S);
}

public enum GoalState
{
    /// <summary>No list of that board was seen yet (the ranking window of the game has not been opened).</summary>
    NoList,
    /// <summary>The class board: my class's filter is not known yet (the class tab of the ranking window has not been opened).</summary>
    NoClass,
    /// <summary>My total points are not known yet.</summary>
    NoPoints,
    /// <summary>The goal is below what the list holds (the game sends only the top 500).</summary>
    Beyond,
    /// <summary>My points are above the score of the goal rank.</summary>
    Reached,
    /// <summary>Points still needed to pass the one at the goal rank.</summary>
    Needed,
}

/// <param name="Needed">Points to pass the one at the goal rank (Needed), or by how many points I am ahead of them (Reached); for Beyond the points to the last rank the list holds.</param>
/// <param name="ScoreAtTarget">The score of the goal rank in the list (Beyond: of the last rank of the list).</param>
public sealed record RankGoal(int Target, GoalState State, long? ScoreAtTarget, long? Needed, int ListLastRank, DateTimeOffset? AsOf);

public static class RankGoals
{
    /// <summary>
    /// How far my live total is from the goal rank. The scores of the list are the totals at the ranking reset (hourly), mine is live: it is the best the game's own data allows,
    /// and the time of the list is part of the answer.
    /// </summary>
    public static RankGoal Evaluate(RankList? list, ulong? myTotal, int? myRank, int target)
    {
        if (list is null || list.Count == 0) return new RankGoal(target, GoalState.NoList, null, null, 0, null);
        if (myTotal is not ulong mine) return new RankGoal(target, GoalState.NoPoints, null, null, list.LastRank, list.AsOf);
        if (!list.ScoreByRank.TryGetValue(target, out long score))
        {
            if (target > list.LastRank && list.ScoreByRank.TryGetValue(list.LastRank, out long last))
                // Below the last rank of the list: I am surely inside when I am above its score; otherwise only that last rank is known.
                return (long)mine > last
                    ? new RankGoal(target, GoalState.Reached, last, (long)mine - last, list.LastRank, list.AsOf)
                    : new RankGoal(target, GoalState.Beyond, last, last - (long)mine + 1, list.LastRank, list.AsOf);
            return new RankGoal(target, GoalState.NoList, null, null, list.LastRank, list.AsOf);
        }
        // Ahead of the one at the goal rank (a tie counts only when my own rank says I am inside).
        bool ahead = (long)mine > score || ((long)mine == score && myRank is int r && r <= target);
        return ahead
            ? new RankGoal(target, GoalState.Reached, score, (long)mine - score, list.LastRank, list.AsOf)
            : new RankGoal(target, GoalState.Needed, score, score - (long)mine + 1, list.LastRank, list.AsOf);
    }
}
