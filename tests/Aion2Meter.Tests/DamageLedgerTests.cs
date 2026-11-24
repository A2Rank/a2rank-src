using Aion2Meter.Core;
using Aion2Meter.Protocol;
using Microsoft.Data.Sqlite;

namespace Aion2Meter.Tests;

public class DamageLedgerTests
{
    private const uint Me = 13313, Enemy = 3068, Monster = 27094;
    private static readonly DateTimeOffset H21 = new(2026, 10, 10, 21, 0, 0, Kst.Offset);
    private static DateTimeOffset At(int minutes, double seconds = 0) => H21.AddMinutes(minutes).AddSeconds(seconds);

    private static DamageMessage Hit(uint target, uint source, uint total, byte flags = 0x16, params uint[] extras) =>
        new(target, source, flags, 20993, total, extras, 0x6563_0000);

    private static DamageMessage Skill(uint target, uint source, uint total, uint skillKey) =>
        new(target, source, 0x16, 20993, total, [], skillKey << 16);

    [Fact]
    public void Adds_up_what_I_did_and_what_I_took_per_opponent()
    {
        var ledger = new DamageLedger();
        ledger.Add(Hit(Enemy, Me, 1000), Me, At(1));
        ledger.Add(Hit(Enemy, Me, 500, 0x26, 100, 100), Me, At(1, 1));      // total already includes the extras
        ledger.Add(Hit(Me, Enemy, 300), Me, At(1, 2));
        ledger.Add(Hit(Me, 4444, 999), Me, At(1, 2));                       // another opponent
        var enemy = ledger.Take(Enemy, At(1, 3))!;
        Assert.Equal((1500L, 300L, 2, 1), (enemy.Dealt, enemy.Received, enemy.DealtHits, enemy.ReceivedHits));
        var other = ledger.Take(4444, At(1, 3))!;
        Assert.Equal((0L, 999L, 0, 1), (other.Dealt, other.Received, other.DealtHits, other.ReceivedHits));
    }

    [Fact]
    public void Monsters_recovery_third_parties_and_an_unknown_self_are_ignored()
    {
        var ledger = new DamageLedger();
        ledger.Add(Hit(Monster, Me, 69_000_000), Me, At(1));                // a monster (entity >= 16384)
        ledger.Add(Hit(Me, Monster, 77), Me, At(1));
        ledger.Add(Hit(Enemy, Me, 2924, flags: 0x04), Me, At(1));           // recovery, not damage
        ledger.Add(Hit(Enemy, 5555, 800), Me, At(1));                       // two other people
        ledger.Add(Hit(Me, Me, 1064), Me, At(1));                           // my own tick
        ledger.Add(Hit(Enemy, Me, 800), null, At(1));                       // self not known yet
        Assert.Null(ledger.Take(Monster, At(1)));
        Assert.Null(ledger.Take(Enemy, At(1)));
        Assert.Null(ledger.Take(5555, At(1)));
    }

    [Fact]
    public void A_kill_takes_the_damage_and_the_next_fight_starts_over()
    {
        var ledger = new DamageLedger();
        ledger.Add(Hit(Enemy, Me, 1000), Me, At(1));
        Assert.NotNull(ledger.Take(Enemy, At(1, 5)));
        Assert.Null(ledger.Take(Enemy, At(1, 6)));
        ledger.Add(Hit(Enemy, Me, 40), Me, At(2));
        Assert.Equal(40, ledger.Take(Enemy, At(2, 1))!.Dealt);
    }

    [Fact]
    public void A_fight_that_ended_long_ago_does_not_belong_to_a_kill_now()
    {
        var ledger = new DamageLedger();
        ledger.Add(Hit(Enemy, Me, 1000), Me, At(1));
        Assert.Null(ledger.Take(Enemy, At(1).Add(DamageLedger.Stale).AddSeconds(1)));
    }

    [Fact]
    public void Session_attaches_the_damage_to_the_kill_and_the_list_adds_it_up_per_name()
    {
        using var store = new MeterStore(":memory:");
        var s = new MeterSession(store, ResetMode.OnTheHour, 3, At(0));
        s.Start(At(0));
        s.Handle(new MessageEvent(new StreamToken(new FlowKey("10.0.0.1", 13328, "192.168.0.2", 50001), 0), At(0, 30), new PointMessage(41_000_000)));
        void Send(GameMessage m, DateTimeOffset t) =>
            s.Handle(new MessageEvent(new StreamToken(new FlowKey("10.0.0.1", 13328, "192.168.0.2", 50001), 0), t, m));
        Send(new EnterWorldMessage(Me, "아라", 1019), At(1));
        Send(new IdentityMessage(Enemy, "료이치", 55, 2, 2004), At(1, 1));

        // first fight and kill
        Send(Hit(Enemy, Me, 40_000), At(1, 20));
        Send(Hit(Enemy, Me, 30_000), At(1, 21));
        Send(Hit(Me, Enemy, 5_000), At(1, 22));
        Send(Hit(Monster, Me, 9_999_999), At(1, 22));                       // a monster in the same fight
        Send(new DeathMessage(Enemy, Me, 1019, "아라", 1, true), At(2));
        Send(new PointMessage(41_000_640), At(2, 0.02));
        // the same player again later
        Send(Hit(Enemy, Me, 10_000), At(5));
        Send(new DeathMessage(Enemy, Me, 1019, "아라", 1, true), At(5, 10));
        Send(new PointMessage(41_001_280), At(5, 10.02));

        var tally = Assert.Single(s.Kills.Sorted());
        Assert.Equal((2, "료이치", 80_000L, 5_000L), (tally.Count, tally.Name, tally.Dealt, tally.Received));
    }

    [Fact]
    public void Damage_is_stored_with_the_kill_and_old_databases_get_the_columns()
    {
        using (var store = new MeterStore(":memory:"))
        {
            var kill = new KillRecord(KillKind.Kill, "적", 2004, 2, 640, At(1), Enemy, new DamageStats(1500, 300, 2, 1));
            store.AddKill("1019:아라", kill);
            store.AddKill("1019:아라", new KillRecord(KillKind.Kill, "무피해", 2004, 2, 640, At(2)));
            var back = store.KillsSince("1019:아라", At(0));
            Assert.Equal((1500L, 300L, 2, 1), (back[0].Damage!.Dealt, back[0].Damage!.Received, back[0].Damage!.DealtHits, back[0].Damage!.ReceivedHits));
            Assert.Null(back[1].Damage);
        }

        // a database written before damage was recorded
        var file = Path.Combine(Path.GetTempPath(), "a2m-old-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var old = new SqliteConnection($"Data Source={file}"))
            {
                old.Open();
                using var cmd = old.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE kills (id INTEGER PRIMARY KEY, character TEXT NOT NULL DEFAULT '', time_ms INTEGER NOT NULL, kind INTEGER NOT NULL,
                        name TEXT NOT NULL, server INTEGER NOT NULL, race INTEGER NOT NULL, delta INTEGER NOT NULL);
                    INSERT INTO kills (character, time_ms, kind, name, server, race, delta) VALUES ('1019:아라', 1000, 0, '옛기록', 2004, 2, 640);
                    """;
                cmd.ExecuteNonQuery();
            }
            using var store = new MeterStore(file);
            var row = Assert.Single(store.KillsSince("1019:아라", DateTimeOffset.FromUnixTimeMilliseconds(0)));
            Assert.Equal(("옛기록", 640L), (row.Name, row.Delta));
            Assert.Null(row.Damage);
            store.AddKill("1019:아라", new KillRecord(KillKind.Kill, "새기록", 2004, 2, 1, At(1), Enemy, new DamageStats(7, 8, 1, 1)));
            Assert.Equal(2, store.KillsSince("1019:아라", DateTimeOffset.FromUnixTimeMilliseconds(0)).Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(file);
            foreach (var extra in new[] { file + "-wal", file + "-shm" }) if (File.Exists(extra)) File.Delete(extra);
        }
    }
}
