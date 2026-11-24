using Aion2Meter.Core;
using Aion2Meter.Protocol;

namespace Aion2Meter.Tests;

public class CoreTests
{
    // 2026-10-10 21:00:00 KST
    private static readonly DateTimeOffset H21 = new(2026, 10, 10, 21, 0, 0, Kst.Offset);
    private static readonly FlowKey FlowA = new("10.0.0.1", 13328, "192.168.0.2", 50001);
    private static readonly FlowKey FlowB = new("10.0.0.1", 13328, "192.168.0.2", 50002);
    private static readonly StreamToken A = new(FlowA, 0), B = new(FlowB, 0);

    private static DateTimeOffset At(int minutes, double seconds = 0) => H21.AddMinutes(minutes).AddSeconds(seconds);

    [Fact]
    public void Points_first_total_is_baseline()
    {
        var p = new PointTracker();
        Assert.Null(p.OnPoint(A, 1000, At(0))); // not measuring
        p.Start();
        Assert.Equal(new PointUpdate(0, false), p.OnPoint(A, 1000, At(0)));
        Assert.Equal(new PointUpdate(640, false), p.OnPoint(A, 1640, At(1)));
        Assert.Equal(new PointUpdate(-628, false), p.OnPoint(A, 1012, At(2)));

        p.Stop();
        p.Start();
        Assert.Equal(new PointUpdate(0, false), p.OnPoint(A, 1012, At(3))); // a restart never carries over

        p.Stop();
        p.Start(remembered: 1012);
        Assert.Equal(new PointUpdate(30_000, false), p.OnPoint(A, 31_012, At(4)));    // the character's last known total
        p.Reset(remembered: 1012);
        Assert.Equal(new PointUpdate(0, false), p.OnPoint(A, 31_013, At(5)));         // more than 30,000 apart: baseline only
    }

    [Fact]
    public void Points_carry_over_to_a_new_connection_only_when_close()
    {
        var p = new PointTracker();
        p.Start();
        p.OnPoint(A, 1000, At(0));
        Assert.Null(p.OnPoint(B, 5000, At(1)));          // A still alive: second connection ignored
        p.Lost(FlowA);
        Assert.Equal(new PointUpdate(23, false), p.OnPoint(B, 1023, At(2)));  // carried over

        p.Lost(FlowB);
        Assert.Equal(new PointUpdate(0, true), p.OnPoint(A, 1023 + 50_001, At(3)));   // too far apart: new baseline
        p.Lost(FlowA);
        Assert.Equal(new PointUpdate(0, true), p.OnPoint(B, 51_100, At(14)));         // over 10 minutes later

        // The measured connection went silent without closing.
        p.Seen(B, At(15));
        Assert.Null(p.OnPoint(A, 51_200, At(15, 30)));
        Assert.Equal(new PointUpdate(100, false), p.OnPoint(A, 51_200, At(16, 1)));
    }

    [Fact]
    public void Points_after_a_gap_use_the_new_stream_token()
    {
        var p = new PointTracker();
        p.Start();
        p.OnPoint(A, 1000, At(0));
        p.Lost(FlowA); // gap: the pipeline continues the flow with a new generation
        Assert.Equal(new PointUpdate(10, false), p.OnPoint(A with { Generation = 1 }, 1010, At(0, 5)));
    }

    [Fact]
    public void Period_start_follows_the_offset()
    {
        var book = new BucketBook(0, [], H21);
        Assert.Equal(H21, book.PeriodStart(At(59, 59)));
        book.SetOffset(1700, H21); // 28:20 after the hour
        Assert.Equal(H21.AddHours(-1).AddSeconds(1700), book.PeriodStart(At(28, 19)));
        Assert.Equal(H21.AddSeconds(1700), book.PeriodStart(At(28, 20)));
        Assert.Equal(0, BucketBook.Normalize(-3600));
        Assert.Equal(3599, BucketBook.Normalize(-1));
    }

    [Fact]
    public void Buckets_roll_hourly_and_mark_partial_periods()
    {
        var book = new BucketBook(0, [], At(10));
        var saved = new List<Bucket>();
        book.Changed += saved.Add;
        book.Resume(At(10));
        book.Add(640, At(20));
        book.Add(-628, At(30));
        var first = book.Current!;
        Assert.Equal((640L, 628L, 12L, 2, true), (first.Gain, first.Loss, first.Net, first.Events, first.Partial)); // started at :10

        book.Roll(At(60));
        Assert.Same(first, book.Previous);
        Assert.False(book.Current!.Partial);                   // measured from its first second
        book.Add(500, At(70));
        book.Interrupt(At(75));
        Assert.True(book.Current.Partial);
        Assert.Contains(first, saved);

        book.Pause(At(130));
        book.Roll(At(180));
        Assert.Null(book.Current);                             // not measuring: no new period
    }

    [Fact]
    public void Offset_change_closes_the_running_period()
    {
        var book = new BucketBook(0, [], H21);
        book.Resume(H21);
        book.Add(100, At(5));
        Assert.True(book.SetOffset(1700, At(40)));
        Assert.Equal(At(40), book.Previous!.End);
        Assert.True(book.Previous.Partial);
        Assert.Equal(H21.AddSeconds(1700), book.Current!.Start);
        Assert.True(book.Current.Partial);
        Assert.False(book.SetOffset(1700, At(41)));
    }

    [Fact]
    public void Day_total_counts_periods_started_that_day()
    {
        var day = Kst.Day(H21);
        var late = new Bucket { Start = new DateTimeOffset(2026, 10, 10, 23, 28, 20, Kst.Offset), End = new DateTimeOffset(2026, 10, 11, 0, 28, 20, Kst.Offset), Gain = 300, Events = 1 };
        var next = new Bucket { Start = new DateTimeOffset(2026, 10, 11, 0, 28, 20, Kst.Offset), End = new DateTimeOffset(2026, 10, 11, 1, 28, 20, Kst.Offset), Gain = 7, Events = 1 };
        var book = new BucketBook(1700, [new Bucket { Start = H21, End = H21.AddHours(1), Gain = 1000, Loss = 200, Events = 3 }, late, next],
            next.Start.AddMinutes(1));
        Assert.Same(next, book.Current);
        Assert.Equal(new Totals(1300, 200, 4), book.DayTotal(day));
        Assert.Equal(new Totals(7, 0, 1), book.DayTotal(day.AddDays(1)));
    }

    private static KillRecord Kill(string name, int server, DateTimeOffset t, long delta = 640) => new(KillKind.Kill, name, server, 2, delta, t);

    [Fact]
    public void Kill_list_pins_repeat_names_and_resets_each_hour()
    {
        var list = new HourlyKillList(threshold: 3);
        foreach (var (name, minute) in new[] { ("가", 1), ("나", 2), ("가", 3), ("다", 4), ("가", 5), ("나", 6) })
            Assert.True(list.Add(Kill(name, 2004, At(minute))));
        list.Add(Kill("가", 2005, At(7)));             // same name, other server: someone else
        list.AddUnknown(new UnmatchedGain(550, At(8)));
        list.Add(new KillRecord(KillKind.Death, "적", 2004, 2, -628, At(9)));

        var sorted = list.Sorted();
        Assert.Equal(["가/2004", "가/2005", "나/2004", "다/2004"], sorted.Select(t => $"{t.Name}/{t.Server}"));
        Assert.True(list.IsPinned(sorted[0]));
        Assert.Equal((3, 1920L, At(5)), (sorted[0].Count, sorted[0].Points, sorted[0].Last));
        Assert.Equal((1, 550L, 1, 7), (list.UnknownCount, list.UnknownPoints, list.Deaths, list.KillCount));

        Assert.True(list.Roll(At(60)));
        Assert.Empty(list.Sorted());
        Assert.False(list.Add(Kill("가", 2004, At(59)))); // a late name for the previous hour
    }

    [Fact]
    public void Store_round_trip()
    {
        using var store = new MeterStore(":memory:");
        var b = new Bucket { Start = H21, End = H21.AddHours(1), Gain = 5, Loss = 1, Events = 2, Partial = true };
        store.SaveBucket("아라", b);
        b.Gain = 50;
        store.SaveBucket("아라", b);
        var loaded = Assert.Single(store.BucketsOnDay("아라", Kst.Day(H21)));
        Assert.Empty(store.BucketsOnDay("보리", Kst.Day(H21)));
        Assert.Equal((b.Id, H21, 50L, 1L, 2, true), (loaded.Id, loaded.Start, loaded.Gain, loaded.Loss, loaded.Events, loaded.Partial));
        Assert.Empty(store.BucketsOnDay("아라", Kst.Day(H21).AddDays(1)));

        store.AddKill("아라", Kill("료이치", 2004, At(1)));
        store.AddUnmatched("아라", new UnmatchedGain(23, At(2)));
        Assert.Equal(Kill("료이치", 2004, At(1)), Assert.Single(store.KillsSince("아라", H21)));
        Assert.Empty(store.KillsSince("아라", At(2)));
        Assert.Equal(new UnmatchedGain(23, At(2)), Assert.Single(store.UnmatchedSince("아라", H21)));

        Assert.Null(store.Get("x"));
        store.Set("x", "1");
        store.Set("x", "2");
        Assert.Equal("2", store.Get("x"));
    }

    private static MessageEvent Msg(GameMessage m, DateTimeOffset t, StreamToken? token = null) => new(token ?? A, t, m);

    private static RankingMessage Ranking(string self, DateTimeOffset deadline) => new(false, 0,
        [new RankingEntry(12, 1019, 777, self, "길드", 1, 55, 3, 40_000_000)], deadline, 500);

    [Fact]
    public void Session_keeps_each_character_apart()
    {
        using var store = new MeterStore(":memory:");
        var s = new MeterSession(store, ResetMode.OnTheHour, 3, At(0));
        Assert.Null(s.Character);
        s.Start(At(0));
        s.Handle(Msg(new PointMessage(41_000_000), At(0, 30)));                       // before anyone is known
        s.Handle(Msg(new EnterWorldMessage(13313, "아라", 1019), At(1)));
        Assert.Equal(new CharacterKey(1019, "아라"), s.Character);
        s.Handle(Msg(new IdentityMessage(3068, "료이치", 55, 2, 2004), At(1, 1)));
        s.Handle(Msg(new DeathMessage(3068, 13313, 1019, "아라", 1, true), At(2)));
        s.Handle(Msg(new PointMessage(41_000_640), At(2, 0.02)));
        Assert.Equal((640L, 1), (s.Buckets.Current!.Net, s.Kills.KillCount));

        // Character change on the same connection: 보리's total must not be diffed against 아라's.
        s.Handle(Msg(new EnterWorldMessage(7113, "보리", 1019), At(3)));
        Assert.Equal("보리", s.Character!.Name);
        Assert.Equal((0L, 0), (s.Buckets.Current!.Net, s.Kills.KillCount));
        s.Handle(Msg(new PointMessage(41_000_000 - 40_000), At(4)));                  // within 50,000 of 아라's total
        s.Handle(Msg(new PointMessage(41_000_000 - 39_944), At(5)));
        Assert.Equal(56, s.Buckets.Current.Net);
        Assert.Equal(1, s.Kills.UnknownCount);

        s.Handle(Msg(new EnterWorldMessage(12234, "아라", 1019), At(6)));
        Assert.Equal((640L, 1), (s.Buckets.Current!.Net, s.Kills.KillCount));       // 아라's hour continues
        Assert.True(s.Buckets.Current.Partial);
        s.Handle(Msg(new PointMessage(41_000_700), At(8)));                         // counted against 아라's remembered total
        Assert.Equal(new Totals(700, 0, 2), s.Today(At(8)));
        Assert.Equal(["1019:보리", "1019:아라"], store.Characters().Order());

        var restarted = new MeterSession(store, ResetMode.OnTheHour, 3, At(9));
        Assert.Equal(("아라", false), (restarted.Character!.Name, restarted.CharacterConfirmed));
        Assert.Equal(700, restarted.Buckets.Current!.Net);
    }

    [Fact]
    public void Resurrection_keeps_the_tally_and_monster_kills_are_counted_apart()
    {
        using var store = new MeterStore(":memory:");
        var s = new MeterSession(store, ResetMode.OnTheHour, 3, At(0));
        s.Start(At(0));
        s.Handle(Msg(new EnterWorldMessage(16005, "보리", 1019), At(1)));
        // The ranking row carries another code (1006) than the in-game server: still the same character.
        s.Handle(Msg(Ranking("보리", At(28, 20)) with { Entries = [new RankingEntry(3070, 1006, 113395, "보리", "", 1, 30, 3, 6_733_244)] }, At(1, 1)));
        Assert.Equal(new CharacterKey(1019, "보리"), s.Character);
        Assert.Equal(1019, s.Character!.Server);

        s.Handle(Msg(new PointMessage(6_735_864), At(2)));
        s.Handle(Msg(new DeathMessage(28014, 16005, 1019, "보리", 1, false), At(3)));            // a monster I killed
        s.Handle(Msg(new PointMessage(6_735_911), At(3)));
        s.Handle(Msg(new DeathMessage(16005, 12114, 2004, "찍새", 2, true), At(4)));             // I die
        s.Handle(Msg(new PointMessage(6_734_911), At(4, 0.01)));
        s.Handle(Msg(new EnterWorldMessage(16005, "보리", 1019), At(5)));                        // resurrection
        Assert.Equal((47L, 1000L, 2), (s.Buckets.Current!.Gain, s.Buckets.Current.Loss, s.Buckets.Current.Events));
        Assert.Equal((1, 47L, 1, 0), (s.Kills.MonsterCount, s.Kills.MonsterPoints, s.Kills.Deaths, s.Kills.UnknownCount));
        s.Handle(Msg(new PointMessage(6_734_950), At(6)));
        Assert.Equal(86, s.Buckets.Current.Gain);                                                  // still counting after it
    }

    [Fact]
    public void After_restart_the_last_character_is_used_for_matching()
    {
        using var store = new MeterStore(":memory:");
        store.Set("character", "1019:보리");
        var s = new MeterSession(store, ResetMode.OnTheHour, 3, At(0));
        s.Start(At(0));
        s.Handle(Msg(new PointMessage(6_736_275), At(1)));
        // 02:02:54: an ally (댕유키) killed by an enemy, then my monster kill 0.3 s later with +44.
        s.Handle(Msg(new IdentityMessage(3576, "댕유키", 25, 1, 1019), At(1, 1)));
        s.Handle(Msg(new DeathMessage(3576, 377, 2004, "옹령", 2, true), At(2)));
        s.Handle(Msg(new DeathMessage(17976, 16005, 1019, "보리", 1, false), At(2, 0.3)));
        s.Handle(Msg(new PointMessage(6_736_319), At(2, 0.3)));
        Assert.Equal((0, 0), (s.Kills.KillCount, s.Kills.UnknownCount));
        Assert.Equal(44, s.Buckets.Current!.Gain);

        // Someone with my name on another server is not me.
        s.Handle(Msg(new DeathMessage(20000, 999, 1006, "보리", 1, false), At(3)));
        s.Handle(Msg(new PointMessage(6_736_350), At(3, 0.01)));
        Assert.Equal(1, s.Kills.UnknownCount);
    }

    [Fact]
    public void Ranks_from_my_rows_and_from_lists()
    {
        using var store = new MeterStore(":memory:");
        var s = new MeterSession(store, ResetMode.OnTheHour, 3, At(0));
        var deadline = At(13, 54).AddHours(1);
        RankingEntry Row(uint rank, ulong id, string name) => new(rank, 1006, id, name, "", 1, 55, 8, 41_054_773);
        s.Handle(Msg(new EnterWorldMessage(13313, "아라", 1019), At(1)));
        s.Handle(Msg(new RankingMessage(false, 0, [Row(167, 14417, "아라")], deadline, 10_000), At(2)));
        Assert.Equal((167, 10_000, (int?)null), (s.Rank!.AllRank, s.Rank.AllTotal, s.Rank.ClassRank));
        Assert.Equal(At(13, 54), s.Rank.AsOf);

        // A class list: someone else named 아라 (other id) must not be taken for me.
        s.Handle(Msg(new RankingMessage(true, 8, [Row(1, 1, "딜해"), Row(5, 99, "아라"), Row(23, 14417, "아라")], deadline, 3036), At(3)));
        Assert.Equal((167, 23, 3036, 8), (s.Rank.AllRank, s.Rank.ClassRank, s.Rank.ClassTotal, s.Rank.ClassFilter));

        var restarted = new MeterSession(store, ResetMode.OnTheHour, 3, At(4));
        Assert.Equal(23, restarted.Rank!.ClassRank);
    }

    [Fact]
    public void Session_end_to_end_and_restart()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aion2meter-test-{Guid.NewGuid():N}.db");
        try
        {
            using (var store = new MeterStore(path))
            {
                var s = new MeterSession(store, ResetMode.RankingReset, 3, At(0));
                s.Start(At(0));
                s.Handle(Msg(Ranking("아라", At(28, 20).AddDays(1)), At(1)));
                Assert.Equal(1700, s.RankingOffset);
                Assert.Equal(1700, s.Buckets.OffsetSeconds);

                s.Handle(Msg(new PointMessage(40_000_000), At(2)));                                  // baseline
                s.Handle(Msg(new IdentityMessage(3068, "료이치", 55, 2, 2004), At(2, 1)));
                s.Handle(Msg(new DeathMessage(3068, 6374, 1019, "사랑", 1, true), At(3)));
                s.Handle(Msg(new PointMessage(40_000_640), At(3, 0.02)));                            // kill
                s.Handle(Msg(new DeathMessage(9999, 6374, 1019, "사랑", 1, true), At(4)));
                s.Handle(Msg(new PointMessage(40_001_190), At(4, 0.02)));                            // name not known yet
                s.Handle(Msg(new IdentityMessage(9999, "일장", 55, 2, 2004), At(4, 5)));            // late name
                s.Handle(Msg(new PointMessage(40_001_213), At(5)));                                  // no death: unknown
                s.Handle(Msg(Ranking("아라", At(28, 21.5).AddDays(1)), At(6)));                      // jitter: ignored
                Assert.Equal(1700, s.RankingOffset);

                Assert.Equal(["료이치", "일장"], s.Kills.Sorted().Select(t => t.Name).Order());
                Assert.Equal((1, 23L), (s.Kills.UnknownCount, s.Kills.UnknownPoints));
                Assert.Equal(new Totals(1213, 0, 3), s.Today(At(6)));
            }

            using (var store = new MeterStore(path))
            {
                var s = new MeterSession(store, ResetMode.RankingReset, 3, At(10));
                Assert.Equal(1700, s.RankingOffset);
                Assert.Equal(1213, s.Buckets.Current!.Net);
                Assert.Equal(2, s.Kills.KillCount);
                Assert.Equal(1, s.Kills.UnknownCount);
                s.Start(At(10));
                Assert.True(s.Buckets.Current.Partial);

                s.SetMode(ResetMode.OnTheHour, At(11));
                Assert.Equal(H21, s.Buckets.Current.Start);
                Assert.Equal(1213, s.Buckets.Previous!.Net);
                Assert.Equal(new Totals(1213, 0, 3), s.Today(At(11)));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f);
        }
    }
}

