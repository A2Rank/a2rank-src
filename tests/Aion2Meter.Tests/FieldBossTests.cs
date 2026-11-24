using Aion2Meter.Core;
using Aion2Meter.Protocol;
using Xunit;

namespace Aion2Meter.Tests;

/// <summary>Field boss timers: the 01 91 message of the map's boss list (docs/PROTOCOL.md) and the alert schedule.</summary>
public class FieldBossTests
{

    private static DateTimeOffset Game(int month, int day, int hour, int minute, int second = 0) =>
        new(2026, month, day, hour, minute, second, FieldBosses.GameOffset);

    [Fact]
    public void The_boss_list_is_parsed_slot_by_slot()
    {
        var m = Assert.IsType<FieldBossMessage>(MessageParser.Parse(Convert.FromHexString(Real.BossList)));
        Assert.Equal(8, m.Entries.Count);
        Assert.Equal([0x0FD4, 0x0FD1, 0x0FD2, 0x0FD3, 0x0FD8, 0x0FD5, 0x0FD6, 0x0FD7], m.Entries.Select(e => (int)e.Id));
        Assert.Equal(1791937756579L, m.Entries.Single(e => e.Id == 0x0FD1).Millis);
        Assert.Equal(0L, m.Entries.Single(e => e.Id == 0x0FD2).Millis);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1792159200000L), Game(10, 16, 23, 0));
    }

    [Fact]
    public void A_cut_or_foreign_message_is_not_a_boss_list()
    {
        var raw = Convert.FromHexString(Real.BossList);
        Assert.Null(MessageParser.Parse(raw.AsSpan(0, 40)));
        raw[2] = 1;
        Assert.Null(MessageParser.Parse(raw));
    }

    [Theory]
    [InlineData(10, 13, 23, 40, 10, 14, 1)]    // late evening: 01:00 of the next day
    [InlineData(10, 14, 0, 30, 10, 14, 1)]
    [InlineData(10, 14, 1, 0, 10, 14, 9)]      // exactly at the spawn: the next one (01 -> 09: 05 is gone)
    [InlineData(10, 14, 8, 59, 10, 14, 9)]
    [InlineData(10, 14, 12, 59, 10, 14, 13)]
    [InlineData(10, 14, 13, 0, 10, 14, 21)]
    [InlineData(10, 14, 21, 0, 10, 15, 1)]
    [InlineData(10, 14, 20, 59, 10, 14, 21)]
    public void Kaira_comes_at_fixed_game_hours(int mo, int d, int h, int mi, int emo, int ed, int eh)
    {
        Assert.Equal(Game(emo, ed, eh, 0), FieldBosses.NextKaira(Game(mo, d, h, mi)));
    }

    [Fact]
    public void Kaira_follows_the_game_clock_not_the_local_one()
    {
        var utcNow = Game(10, 14, 12, 30).ToUniversalTime();
        Assert.Equal(Game(10, 14, 13, 0), FieldBosses.NextKaira(utcNow));
        Assert.Equal(Game(10, 14, 13, 0), FieldBosses.NextKaira(utcNow.ToOffset(TimeSpan.FromHours(-5))));
    }

    [Theory]
    [InlineData(10, 14, 0, 0, 10, 14, 9)]
    [InlineData(10, 14, 8, 59, 10, 14, 9)]
    [InlineData(10, 14, 9, 0, 10, 14, 21)]      // exactly at the spawn: the next one
    [InlineData(10, 14, 20, 59, 10, 14, 21)]
    [InlineData(10, 14, 21, 0, 10, 15, 9)]
    [InlineData(10, 14, 23, 59, 10, 15, 9)]
    public void Agro_comes_at_nine_and_twenty_one_game_time(int mo, int d, int h, int mi, int emo, int ed, int eh)
    {
        Assert.Equal(Game(emo, ed, eh, 0), FieldBosses.NextAgro(Game(mo, d, h, mi)));
        Assert.Equal(Game(emo, ed, eh, 0), FieldBosses.NextAgro(Game(mo, d, h, mi).ToUniversalTime()));   // whatever the local clock
    }

    [Fact]
    public void Alerts_fire_ten_and_five_minutes_before_each_once()
    {
        var alerts = new BossAlerts();
        var spawn = Game(10, 14, 13, 0);
        List<BossAlert> Poll(DateTimeOffset now) => alerts.Poll([("감시자 카이라", spawn)], now, BossAlerts.Default);

        Assert.Empty(Poll(spawn.AddMinutes(-11)));
        var ten = Assert.Single(Poll(spawn.AddMinutes(-10)));
        Assert.Equal(10, ten.MinutesBefore);
        Assert.Equal("감시자 카이라 10분 전 (13:00)", ten.Text);
        Assert.Empty(Poll(spawn.AddMinutes(-9)));
        Assert.Equal(5, Assert.Single(Poll(spawn.AddMinutes(-5))).MinutesBefore);
        Assert.Empty(Poll(spawn.AddMinutes(-4)));
        Assert.Empty(Poll(spawn));
    }

    [Fact]
    public void An_app_started_inside_the_window_gets_only_the_latest_alert()
    {
        var alerts = new BossAlerts();
        var spawn = Game(10, 14, 13, 0);
        Assert.Equal(4, Assert.Single(alerts.Poll([("정령왕 아그로", spawn)], spawn.AddMinutes(-4), BossAlerts.Default)).MinutesBefore);   // the 5 minute alert, saying the 4 that are left
        Assert.Empty(alerts.Poll([("정령왕 아그로", spawn)], spawn.AddMinutes(-3), BossAlerts.Default));
        Assert.Empty(alerts.Poll([("정령왕 아그로", null)], spawn.AddMinutes(-3), BossAlerts.Default));   // switched off or not known
    }

    [Fact]
    public void The_next_spawn_of_a_boss_alerts_again()
    {
        var alerts = new BossAlerts();
        var first = Game(10, 14, 9, 0);
        var second = first.AddHours(4);
        alerts.Poll([("감시자 카이라", first)], first.AddMinutes(-5), BossAlerts.Default);
        Assert.Equal(10, Assert.Single(alerts.Poll([("감시자 카이라", second)], second.AddMinutes(-10), BossAlerts.Default)).MinutesBefore);
    }

    [Fact]
    public void The_user_picks_the_minutes_and_only_those_ring()
    {
        var alerts = new BossAlerts();
        var spawn = Game(10, 14, 13, 0);
        List<BossAlert> Poll(DateTimeOffset now, params int[] minutes) => alerts.Poll([("감시자 카이라", spawn)], now, minutes);
        Assert.Empty(Poll(spawn.AddMinutes(-10), 1));
        Assert.Empty(Poll(spawn.AddMinutes(-5), 1));
        Assert.Equal(1, Assert.Single(Poll(spawn.AddMinutes(-1), 1)).MinutesBefore);
        Assert.Empty(Poll(spawn.AddSeconds(-30), 1));
        Assert.Empty(Poll(spawn.AddMinutes(-10)));                    // nothing picked: silence
        Assert.Empty(Poll(spawn.AddMinutes(-10), 7, 0, -3));          // numbers that are not offered are ignored
    }

    [Fact]
    public void A_late_alert_tells_the_minutes_that_really_are_left()
    {
        var alerts = new BossAlerts();
        var spawn = Game(10, 14, 13, 0);
        // the 10 minute alert is switched on seven minutes before the spawn
        var late = Assert.Single(alerts.Poll([("정령왕 아그로", spawn)], spawn.AddMinutes(-7), [10]));
        Assert.Equal(7, late.MinutesBefore);
    }

    [Fact]
    public void The_alert_sound_is_a_valid_wav_that_gets_quieter_and_longer_with_repeats()
    {
        var one = AlertTone.Build(1, 70);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(one, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(one, 8, 4));
        Assert.Equal(one.Length - 8, BitConverter.ToInt32(one, 4));
        Assert.True(Build(2).Length > Build(1).Length);
        int Peak(byte[] wav) { int p = 0; for (int i = 44; i + 1 < wav.Length; i += 2) p = Math.Max(p, Math.Abs((int)BitConverter.ToInt16(wav, i))); return p; }
        Assert.True(Peak(AlertTone.Build(1, 100)) > Peak(AlertTone.Build(1, 40)));
        Assert.Equal(0, Peak(AlertTone.Build(1, 0)));
        Assert.True(Peak(AlertTone.Build(1, 100)) < short.MaxValue);
        static byte[] Build(int times) => AlertTone.Build(times, 70);
    }
}
