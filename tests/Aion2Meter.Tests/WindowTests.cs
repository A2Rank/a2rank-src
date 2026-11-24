using Aion2Meter.Core;
using Aion2Meter.Protocol;

namespace Aion2Meter.Tests;

/// <summary>"구간 기록": every counted period in which rank points moved is a record; the whole-hour ones are marked complete.</summary>
public class WindowTests
{
    private static readonly DateTimeOffset H21 = new(2026, 10, 10, 21, 0, 0, Kst.Offset);   // 21:00:00 KST
    private static readonly FlowKey FlowA = new("10.0.0.1", 13328, "192.168.0.2", 50001);
    private static readonly StreamToken A = new(FlowA, 0);

    private static DateTimeOffset At(int minutes, double seconds = 0) => H21.AddMinutes(minutes).AddSeconds(seconds);
    private static MessageEvent Msg(GameMessage m, DateTimeOffset t) => new(A, t, m);

    // The game's ranking resets at xx:28:20 (offset 1700 s): windows are 21:28:20-22:28:20, 22:28:20-23:28:20, ...
    private static RankingMessage Ranking() => new(false, 0,
        [new RankingEntry(12, 1019, 777, "아라", "길드", 1, 55, 3, 40_000_000)], At(28, 20).AddDays(1), 500);

    private static (MeterStore Store, MeterSession Session, List<WindowRecord> Windows) Make()
    {
        var store = new MeterStore(":memory:");
        var s = new MeterSession(store, ResetMode.OnTheHour, 3, At(0));
        var windows = new List<WindowRecord>();
        s.WindowClosed += windows.Add;
        return (store, s, windows);
    }

    [Fact]
    public void A_whole_ranking_hour_is_a_complete_record_with_its_totals_times_and_kills()
    {
        var (store, s, windows) = Make();
        using var _ = store;
        s.Start(At(0));
        s.Handle(Msg(Ranking(), At(1)));
        s.Handle(Msg(new PointMessage(40_000_000), At(10)));                                  // baseline
        s.Handle(Msg(new IdentityMessage(3068, "료이치", 55, 2, 2004), At(30, 1)));
        s.Handle(Msg(new DeathMessage(3068, 6374, 1019, "사랑", 1, true), At(31)));
        s.Handle(Msg(new PointMessage(40_000_640), At(31, 0.02)));                           // a kill
        s.Handle(Msg(new PointMessage(40_000_740), At(40)));                                  // +100, no death seen
        s.Tick(At(60));
        Assert.Empty(windows);                                                                // the hour is not over

        s.Tick(At(88, 25));
        Assert.Empty(windows);                                                                // over, but the last seconds settle first
        s.Tick(At(89));
        var w = Assert.Single(windows, x => x.Start == At(28, 20));
        Assert.Equal((At(28, 20), At(88, 20), 1700), (w.Start, w.End, w.OffsetSeconds));
        Assert.Equal((true, true), (w.Aligned, w.Complete));
        Assert.Equal((At(31, 0.02), At(40)), (w.First, w.Last));
        Assert.Equal((40_000_000UL, 40_000_740UL), (w.StartTotal, w.EndTotal));
        Assert.Equal((740L, 0L, 2), (w.Gain, w.Loss, w.Events));
        Assert.Equal(100, w.Unmatched);
        var kill = Assert.Single(w.Kills);
        Assert.Equal((KillKind.Kill, 640L, At(31).ToUnixTimeSeconds()), (kill.Kind, kill.Delta, kill.Time));
        Assert.Equal(("아라", ((ulong)1019 << 48) | 777UL), (w.CharacterName, w.RankingId ?? 0));   // ranking id: server code + character number
        Assert.Equal((1, 740L), (Assert.Single(w.Parties).Size, w.Parties[0].Gain));
    }

    [Fact]
    public void A_period_in_which_rank_points_did_not_move_is_not_sent()
    {
        var (store, s, windows) = Make();
        using var _ = store;
        s.Start(At(0));
        s.Handle(Msg(Ranking(), At(1)));
        s.Handle(Msg(new PointMessage(40_000_000), At(10)));
        s.Handle(Msg(new PointMessage(40_000_300), At(30)));
        s.Tick(At(89));                                   // 21:28:20-22:28:20 had a change
        s.Tick(At(210));                                  // the two idle hours after it had none
        Assert.Contains(windows, w => w.Start == At(28, 20));
        Assert.DoesNotContain(windows, w => w.Start >= At(88, 20));
    }

    [Fact]
    public void Gains_and_losses_that_cancel_are_still_movement()
    {
        var (store, s, windows) = Make();
        using var _ = store;
        s.Start(At(0));
        s.Handle(Msg(Ranking(), At(1)));
        s.Handle(Msg(new PointMessage(40_000_000), At(10)));
        s.Handle(Msg(new PointMessage(40_000_100), At(30)));
        s.Handle(Msg(new PointMessage(40_000_000), At(31)));
        s.Tick(At(89));
        var w = Assert.Single(windows, x => x.Start == At(28, 20));
        Assert.Equal((100L, 100L), (w.Gain, w.Loss));
    }

    [Fact]
    public void The_two_pieces_of_a_hour_cut_in_two_do_not_both_list_the_same_kills()
    {
        var (store, s, windows) = Make();
        using var _ = store;
        s.Start(At(0));
        s.Handle(Msg(Ranking(), At(1)));
        s.Handle(Msg(new PointMessage(40_000_000), At(10)));
        s.Handle(Msg(new IdentityMessage(3068, "료이치", 55, 2, 2004), At(30, 1)));
        s.Handle(Msg(new DeathMessage(3068, 6374, 1019, "사랑", 1, true), At(31)));
        s.Handle(Msg(new PointMessage(40_000_640), At(31, 0.02)));                           // a kill in the first piece
        s.Stop(At(40));
        s.Start(At(41));
        s.Handle(Msg(new PointMessage(40_000_640), At(42)));
        s.Handle(Msg(new DeathMessage(3068, 6374, 1019, "사랑", 1, true), At(50)));
        s.Handle(Msg(new PointMessage(40_000_700), At(50, 0.02)));                           // another in the second
        s.Tick(At(89));
        var first = windows.Single(x => x.Start == At(28, 20) && x.End == At(40));
        var rest = windows.Single(x => x.Start == At(28, 20) && x.End == At(88, 20));
        Assert.Equal([At(31).ToUnixTimeSeconds()], first.Kills.Select(k => k.Time));
        Assert.Equal([At(50).ToUnixTimeSeconds()], rest.Kills.Select(k => k.Time));
    }

    [Fact]
    public void A_period_cut_short_is_a_record_too_and_not_complete()
    {
        var (store, s, windows) = Make();
        using var _ = store;
        s.Start(At(0));
        s.Handle(Msg(Ranking(), At(1)));
        s.Handle(Msg(new PointMessage(40_000_000), At(10)));
        s.Handle(Msg(new PointMessage(40_000_300), At(30)));
        s.Stop(At(40));                                   // stopped in the middle of 21:28:20-22:28:20
        var cut = Assert.Single(windows, x => x.Start == At(28, 20));
        Assert.Equal((At(40), false, true, 300L), (cut.End, cut.Complete, cut.Aligned, cut.Gain));

        s.Start(At(41));                                  // measuring on opens another period in the same hour
        s.Handle(Msg(new PointMessage(40_000_300), At(42)));   // baseline after the restart
        s.Handle(Msg(new PointMessage(40_000_350), At(50)));
        s.Tick(At(89));
        var rest = windows.Single(x => x.Start == At(28, 20) && x.End == At(88, 20));
        Assert.Equal((false, 50L), (rest.Complete, rest.Gain));
    }

    [Fact]
    public void Without_the_ranking_reset_time_the_records_are_made_but_not_aligned()
    {
        var (store, s, windows) = Make();
        using var _ = store;
        s.Start(At(0));
        s.Handle(Msg(new EnterWorldMessage(13313, "아라", 1019), At(1)));
        s.Handle(Msg(new PointMessage(40_000_000), At(2)));
        s.Handle(Msg(new PointMessage(40_000_100), At(30)));
        s.Tick(At(200));
        var w = Assert.Single(windows);
        Assert.Equal((false, false, 0), (w.Aligned, w.Complete, w.OffsetSeconds));   // cut on the hour: not the game's reset
    }

    [Fact]
    public void Learning_the_reset_time_in_the_middle_of_an_hour_cuts_that_hour_short()
    {
        var (store, s, windows) = Make();
        using var _ = store;
        s.Start(At(0));
        s.Handle(Msg(new PointMessage(40_000_000), At(2)));
        s.Handle(Msg(new PointMessage(40_000_020), At(30)));
        s.Handle(Msg(Ranking(), At(40)));                  // 21:40: the hour counted so far was cut on the hour, not at the game's reset
        s.Handle(Msg(new PointMessage(40_000_050), At(50)));
        s.Tick(At(89));
        Assert.Equal(2, windows.Count);
        Assert.All(windows, w => Assert.False(w.Complete));
        Assert.False(windows[0].Aligned);                  // the part before: on the hour
        Assert.True(windows[1].Aligned);                   // the part after: from the reset time on
    }

    [Fact]
    public void Switching_characters_gives_each_its_own_record()
    {
        var (store, s, windows) = Make();
        using var _ = store;
        s.Start(At(0));
        s.Handle(Msg(Ranking(), At(1)));
        s.Handle(Msg(new PointMessage(40_000_000), At(10)));
        s.Handle(Msg(new PointMessage(40_000_200), At(30)));
        s.Handle(Msg(new EnterWorldMessage(7113, "보리", 1019), At(50)));     // another character in the middle of the hour
        var first = Assert.Single(windows);
        Assert.Equal(("아라", false, 200L), (first.CharacterName, first.Complete, first.Gain));
    }

    [Fact]
    public void Closing_the_app_makes_the_period_in_progress_a_record()
    {
        var (store, s, windows) = Make();
        using var _ = store;
        s.Start(At(0));
        s.Handle(Msg(Ranking(), At(1)));
        s.Handle(Msg(new PointMessage(40_000_000), At(10)));
        s.Handle(Msg(new PointMessage(40_000_300), At(30)));
        s.Shutdown(At(45));
        var w = Assert.Single(windows);
        Assert.Equal((At(45), 300L, false), (w.End, w.Gain, w.Complete));
    }

    [Fact]
    public void The_kills_of_a_record_carry_the_other_players_name_and_nothing_else_does()
    {
        var (store, s, windows) = Make();
        using var _ = store;
        s.Start(At(0));
        s.Handle(Msg(Ranking(), At(1)));
        s.Handle(Msg(new PointMessage(40_000_000), At(10)));
        s.Handle(Msg(new IdentityMessage(3068, "료이치", 55, 2, 2004), At(30, 1)));
        s.Handle(Msg(new DeathMessage(3068, 6374, 1019, "사랑", 1, true), At(31)));
        s.Handle(Msg(new PointMessage(40_000_640), At(31, 0.02)));
        s.Tick(At(89));
        var record = Assert.Single(windows, x => x.Start == At(28, 20));
        var json = record.ToJson();
        Assert.Equal("료이치", Assert.Single(record.Kills).Name);
        Assert.Contains("\"료이치\"", json);                    // the victim, in the kill row
        Assert.DoesNotContain("사랑", json);                    // the one whose death message announced the kill is not part of it
        Assert.Equal(1, json.Split("료이치").Length - 1);       // and the name is only in that one row
        Assert.Contains("\"start\":" + At(28, 20).ToUnixTimeSeconds(), json);
        Assert.Contains("\"t0\":40000000", json);
        Assert.Contains("\"t1\":40000640", json);
        Assert.Contains("\"cp\":1", json);
        Assert.Contains("\"al\":1", json);
    }

    [Fact]
    public void Windows_are_queued_once_and_marked_sent()
    {
        using var store = new MeterStore(":memory:");
        store.AddWindow("1019:아라", At(28, 20), At(88, 20), "{\"a\":1}");
        store.AddWindow("1019:아라", At(28, 20), At(88, 20), "{\"a\":2}");                    // the same period again: kept once
        store.AddWindow("1019:아라", At(28, 20), At(40), "{\"a\":3}");                        // a shorter period of the same hour is another one
        store.AddWindow("1019:아라", At(88, 20), At(148, 20), "{\"a\":4}");
        Assert.Equal(3, store.PendingWindowCount());
        var pending = store.PendingWindows(10);
        Assert.Equal(["{\"a\":1}", "{\"a\":3}", "{\"a\":4}"], pending.Select(p => p.Json).Order());
        store.MarkWindowsSent([pending[0].Id]);
        Assert.Equal(2, store.PendingWindowCount());
        store.PruneWindows(At(60));
        Assert.Equal(2, store.WindowCount());                                    // the sent old one is pruned, the unsent ones stay
        store.DeleteWindows();
        Assert.Equal(0, store.WindowCount());
    }
}
