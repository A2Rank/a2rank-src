using Aion2Meter.Core;

namespace Aion2Meter.Tests;

/// <summary>The record files in the user's folder, made from the counted periods ("구간 기록").</summary>
public class WindowFilesTests
{
    private static readonly DateTimeOffset H21 = new(2026, 10, 10, 21, 13, 54, Kst.Offset);

    private static WindowRecord Record(DateTimeOffset start, DateTimeOffset end, bool complete = true, params WindowKill[] kills) => new(
        "1019:아라", "아라", 1019, 777UL, start, end, 834, start.AddMinutes(2), end.AddMinutes(-3), 41_000_000, 41_001_690,
        2_090, 400, 9, true, complete, [new WindowParty(1, 900, 0, 3), new WindowParty(3, 1_190, 400, 6), new WindowParty(6, 5, 0, 1)], kills, 23);

    private static WindowKill Kill(int sec, KillKind kind, long delta, string name, int server = 2004) =>
        new(H21.AddSeconds(sec).ToUnixTimeSeconds(), kind, delta, server, 2, name);

    [Fact]
    public void A_period_is_a_row_with_totals_counts_and_gain_per_party_size()
    {
        var r = Record(H21, H21.AddHours(1), true,
            Kill(60, KillKind.Kill, 640, "예시가"), Kill(120, KillKind.Kill, 550, "예시나"), Kill(180, KillKind.Death, -400, "예시마"),
            Kill(240, KillKind.Monster, 47, ""), Kill(300, KillKind.Duel, 1, "예시바"), Kill(360, KillKind.Duel, -1, "예시사"));
        Assert.Equal("2026-10-10,21:13:54,22:13:54,완전한 구간,1690,2090,400,9,41000000,41001690,3,2,1,1,1,900,0,1190,0,5,21:15:54,22:10:54", WindowFiles.PeriodRow(r));
        Assert.Equal(WindowFiles.PeriodHeader.Split(',').Length, WindowFiles.PeriodRow(r).Split(',').Length);   // a cell for every column
    }

    [Fact]
    public void A_period_that_runs_past_midnight_names_the_day_of_its_end_and_a_partial_one_says_so()
    {
        var start = new DateTimeOffset(2026, 10, 10, 23, 13, 54, Kst.Offset);
        var row = WindowFiles.PeriodRow(Record(start, start.AddHours(1), complete: false));
        Assert.StartsWith("2026-10-10,23:13:54,10-11 00:13:54,일부 집계,", row);
    }

    [Fact]
    public void Kills_deaths_and_duels_are_rows_in_the_order_they_happened_with_the_other_players_name()
    {
        var r = Record(H21, H21.AddHours(1), true,
            Kill(300, KillKind.Duel, 1, "예시바"), Kill(60, KillKind.Kill, 640, "예시가"), Kill(120, KillKind.Death, -400, "예시마", server: 2003),
            Kill(240, KillKind.Monster, 47, ""));
        Assert.Equal([
            $"2026-10-10,21:14:54,처치,640,예시가,{Protocol.Servers.Label(2004)},21:13:54",
            $"2026-10-10,21:15:54,사망,-400,예시마,{Protocol.Servers.Label(2003)},21:13:54",
            $"2026-10-10,21:17:54,몬스터,47,,{Protocol.Servers.Label(2004)},21:13:54",
            $"2026-10-10,21:18:54,결투 승,,예시바,{Protocol.Servers.Label(2004)},21:13:54"], WindowFiles.KillRows(r));
    }

    [Fact]
    public void The_files_get_their_header_once_and_every_period_adds_its_lines()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"aion2meter-files-{Guid.NewGuid():N}");
        try
        {
            var first = Record(H21, H21.AddHours(1), true, Kill(60, KillKind.Kill, 640, "예시,가"));      // a comma in a name is quoted
            var second = Record(H21.AddHours(1), H21.AddHours(1).AddMinutes(20), false);
            WindowFiles.Append(folder, first);
            WindowFiles.Append(folder, second);

            var periods = File.ReadAllLines(Path.Combine(folder, "랭킹포인트_아라_이슈타르_2026-10-10.csv"));
            Assert.Equal([WindowFiles.PeriodHeader, WindowFiles.PeriodRow(first), WindowFiles.PeriodRow(second)], periods);
            var kills = File.ReadAllLines(Path.Combine(folder, "처치사망_아라_이슈타르_2026-10-10.csv"));
            Assert.Equal(2, kills.Length);                                                                 // the second period had none
            Assert.Equal(WindowFiles.KillHeader, kills[0]);
            Assert.Contains("\"예시,가\"", kills[1]);
            Assert.Equal(0xEF, File.ReadAllBytes(Path.Combine(folder, "랭킹포인트_아라_이슈타르_2026-10-10.csv"))[0]);   // UTF-8 BOM
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_file_with_another_layout_gets_the_new_header_below_its_own()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"aion2meter-files-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "랭킹포인트_아라_이슈타르_2026-10-10.csv");
            File.WriteAllLines(path, ["날짜,시작,끝,순증,획득,손실,횟수,솔로 획득,2인 획득,3인 획득,4인 획득,5인 이상 획득,비고", "2026-10-10,20:13:54,21:13:54,9423,12501,3078,30,1294,0,0,11207,0,일부 집계"]);
            var r = Record(H21, H21.AddHours(1));
            WindowFiles.Append(folder, r);
            WindowFiles.Append(folder, r);
            var lines = File.ReadAllLines(path);
            Assert.Equal(5, lines.Length);                                       // old header + row, new header once, then both rows
            Assert.Equal(WindowFiles.PeriodHeader, lines[2]);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_window_survives_the_json_it_is_kept_as()
    {
        var r = Record(H21, H21.AddHours(1), false, Kill(60, KillKind.Kill, 640, "예시가"), Kill(120, KillKind.Monster, 47, ""));
        var back = WindowRecord.FromJson(r.ToJson());
        Assert.Equal(WindowFiles.PeriodRow(r), WindowFiles.PeriodRow(back));
        Assert.Equal(WindowFiles.KillRows(r), WindowFiles.KillRows(back));
        Assert.Equal(("1019:아라", 777UL, false), (back.CharacterId, back.RankingId ?? 0 & 0xFFFF, back.Complete));
    }

    [Fact]
    public void What_is_sent_to_the_server_has_no_names_of_other_players_what_is_kept_here_has()
    {
        var r = Record(H21, H21.AddHours(1), true, Kill(60, KillKind.Kill, 640, "예시가"), Kill(120, KillKind.Death, -400, "예시마"), Kill(180, KillKind.Monster, 47, ""));
        var kept = r.ToJson();                                   // the copy on this PC
        var sent = r.ToJson(withNames: false);                   // what goes to the server
        Assert.Contains("예시가", kept);
        Assert.Contains("예시마", kept);
        Assert.DoesNotContain("예시가", sent);
        Assert.DoesNotContain("예시마", sent);
        Assert.Contains("\"아라\"", sent);                        // the character itself is told: the server finds periods by name and server
        // the same rows, only the sixth cell is missing, and the sent form can still be read back (names empty)
        var back = WindowRecord.FromJson(sent);
        Assert.Equal(r.Kills.Select(k => (k.Time, k.Kind, k.Delta, k.Server, k.Race)), back.Kills.Select(k => (k.Time, k.Kind, k.Delta, k.Server, k.Race)));
        Assert.All(back.Kills, k => Assert.Equal("", k.Name));
    }

    [Fact]
    public void Names_older_than_a_week_are_deleted_and_the_points_per_period_stay()
    {
        using var store = new MeterStore(":memory:");
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, Kst.Offset);
        KillRecord K(DateTimeOffset t, string name) => new(KillKind.Kill, name, 2004, 2, 100, t);
        store.AddKill("1019:아라", K(now.AddDays(-8), "오래된상대"));
        store.AddKill("1019:아라", K(now.AddDays(-6), "최근상대"));
        store.AddUnmatched("1019:아라", new UnmatchedGain(10, now.AddDays(-9)));
        store.AddUnmatched("1019:아라", new UnmatchedGain(20, now.AddDays(-1)));
        store.SaveBucket("1019:아라", new Bucket { Start = now.AddDays(-9), End = now.AddDays(-9).AddHours(1), Gain = 500, Events = 3 });
        store.AddWindow("1019:아라", now.AddDays(-9), now.AddDays(-9).AddHours(1), "{\"a\":1}", send: false, export: false);
        store.AddWindow("1019:아라", now.AddDays(-1), now.AddDays(-1).AddHours(1), "{\"a\":2}", send: true, export: true);

        var (kills, windows) = store.PruneNames(now - TimeSpan.FromDays(7));
        Assert.Equal((1, 1), (kills, windows));
        Assert.Equal(["최근상대"], store.KillsSince("1019:아라", now.AddDays(-30)).Select(k => k.Name));
        Assert.Equal([20L], store.UnmatchedSince("1019:아라", now.AddDays(-30)).Select(g => g.Delta));
        Assert.Equal(["{\"a\":2}"], store.RecentWindows(10));
        Assert.Single(store.Buckets("1019:아라", now.AddDays(-30), now));                                // points have no names: they stay
        Assert.Equal((0, 0), store.PruneNames(now - TimeSpan.FromDays(7)));                          // nothing more to delete
    }

    [Fact]
    public void Names_kept_for_good_are_capped_by_dropping_the_oldest_kills()
    {
        using var store = new MeterStore(":memory:");
        var t = new DateTimeOffset(2026, 10, 20, 12, 0, 0, Kst.Offset);
        for (int i = 0; i < 5; i++) store.AddKill("1019:아라", new KillRecord(KillKind.Kill, "상대" + i, 2004, 2, 100, t.AddMinutes(i)));
        Assert.Equal(0, store.TrimKills(5));
        Assert.Equal(2, store.TrimKills(3));
        Assert.Equal(["상대2", "상대3", "상대4"], store.KillsSince("1019:아라", t.AddDays(-1)).Select(k => k.Name));
    }

    [Fact]
    public void Records_wait_for_the_files_across_a_restart_and_what_nobody_wants_is_not_kept()
    {
        using var store = new MeterStore(":memory:");
        var t = H21;
        store.AddWindow("1019:아라", t, t.AddHours(1), "{\"a\":1}", send: false, export: true);                 // only for the files
        store.AddWindow("1019:아라", t.AddHours(1), t.AddHours(2), "{\"a\":2}", send: true, export: true);      // both
        store.AddWindow("1019:아라", t.AddHours(2), t.AddHours(3), "{\"a\":3}", send: true, export: false);     // only to be sent
        Assert.Equal(["{\"a\":1}", "{\"a\":2}"], store.PendingExports(10).Select(p => p.Json));
        Assert.Equal(2, store.PendingWindowCount());                                                            // the ones to be sent

        store.MarkExported([store.PendingExports(10)[0].Id]);
        Assert.Equal(["{\"a\":2}"], store.PendingExports(10).Select(p => p.Json));

        store.DeleteWindows();                                                                                  // the consent is withdrawn: the files still get their line
        Assert.Equal(["{\"a\":2}"], store.PendingExports(10).Select(p => p.Json));
        Assert.Equal(0, store.PendingWindowCount());

        store.DiscardExports();                                                                                 // the folder is turned off
        Assert.Empty(store.PendingExports(10));
    }
}
