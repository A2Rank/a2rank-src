using Aion2Meter.Core;
using Aion2Meter.Protocol;

namespace Aion2Meter.Tests;

public class SkillBreakdownTests
{
    private const uint Me = 13313, Enemy = 3068, Monster = 27094;
    private const uint Earth = 0x6563, Grace = 0x69AD, Chain = 0x65BF;   // 대지의 응보, 주신의 은총, 고통의 연쇄
    private static readonly DateTimeOffset H21 = new(2026, 10, 10, 21, 0, 0, Kst.Offset);
    private static DateTimeOffset At(int minutes, double seconds = 0) => H21.AddMinutes(minutes).AddSeconds(seconds);

    private static DamageMessage Hit(uint target, uint source, uint total, uint skillKey, params uint[] extras) =>
        new(target, source, 0x36, 20993, total, extras, skillKey << 16);

    [Fact]
    public void The_ledger_adds_damage_up_per_skill_on_both_sides_biggest_first()
    {
        var ledger = new DamageLedger();
        ledger.Add(Hit(Enemy, Me, 1000, Earth), Me, At(1));
        ledger.Add(Hit(Enemy, Me, 5000, Grace, 500), Me, At(1, 1));
        ledger.Add(Hit(Enemy, Me, 2000, Earth), Me, At(1, 2));
        ledger.Add(Hit(Me, Enemy, 700, Chain), Me, At(1, 3));
        var stats = ledger.Take(Enemy, At(1, 4))!;

        Assert.Equal(new SkillDamage[] { new SkillDamage(Grace, 1, 5000), new SkillDamage(Earth, 2, 3000) }.Select(s => (s.Key, s.Hits, s.Total)), stats.DealtBySkill!.Select(s => (s.Key, s.Hits, s.Total)));
        Assert.Equal(new SkillDamage[] { new SkillDamage(Chain, 1, 700) }.Select(s => (s.Key, s.Hits, s.Total)), stats.ReceivedBySkill!.Select(s => (s.Key, s.Hits, s.Total)));
        Assert.Equal(stats.Dealt, stats.DealtBySkill!.Sum(s => s.Total));        // the per-skill parts add up to the total
        Assert.Equal(stats.DealtHits, stats.DealtBySkill!.Sum(s => s.Hits));
    }

    [Fact]
    public void Merging_adds_per_skill_and_encoding_round_trips()
    {
        var merged = SkillDamage.Merge([new(Earth, 1, 100), new(Grace, 2, 50)], [new(Earth, 2, 300), new(Chain, 1, 10)]);
        Assert.Equal([new SkillDamage(Earth, 3, 400), new SkillDamage(Grace, 2, 50), new SkillDamage(Chain, 1, 10)], merged);
        Assert.Equal("6563:3:400;69ad:2:50;65bf:1:10", SkillDamage.Encode(merged));
        Assert.Equal(merged, SkillDamage.Decode(SkillDamage.Encode(merged)));
        Assert.Empty(SkillDamage.Decode(""));
        Assert.Equal([new SkillDamage(Grace, 2, 50)], SkillDamage.Decode("garbage;69ad:2:50;1:2"));   // bad parts are skipped
    }

    private static MessageEvent Msg(GameMessage m, DateTimeOffset t) =>
        new(new StreamToken(new FlowKey("10.0.0.1", 13328, "192.168.0.2", 50001), 0), t, m);

    [Fact]
    public void A_kill_carries_the_skill_breakdown_into_the_list_and_my_death_into_the_death_list()
    {
        using var store = new MeterStore(":memory:");
        var s = new MeterSession(store, ResetMode.OnTheHour, 3, At(0));
        s.Start(At(0));
        s.Handle(Msg(new PointMessage(41_000_000), At(0, 30)));
        s.Handle(Msg(new EnterWorldMessage(Me, "아라", 1019), At(1)));
        s.Handle(Msg(new IdentityMessage(Enemy, "료이치", 55, 2, 2004), At(1, 1)));
        s.Handle(Msg(new IdentityMessage(4444, "저승사자", 60, 2, 2004), At(1, 2)));

        // I kill 료이치 with two skills; he hurts me a little with one
        s.Handle(Msg(Hit(Enemy, Me, 40_000, Grace), At(1, 20)));
        s.Handle(Msg(Hit(Enemy, Me, 10_000, Earth), At(1, 21)));
        s.Handle(Msg(Hit(Me, Enemy, 300, Chain), At(1, 22)));
        s.Handle(Msg(new DeathMessage(Enemy, Me, 1019, "아라", 1, true), At(2)));
        s.Handle(Msg(new PointMessage(41_000_640), At(2, 0.02)));

        // 저승사자 kills me
        s.Handle(Msg(Hit(Me, 4444, 90_000, Grace), At(3)));
        s.Handle(Msg(Hit(4444, Me, 2_000, Earth), At(3, 1)));
        s.Handle(Msg(new DeathMessage(Me, 4444, 2004, "저승사자", 2, true), At(3, 2)));
        s.Handle(Msg(new PointMessage(41_000_640 - 628), At(3, 2.02)));

        var kill = Assert.Single(s.Kills.Sorted());
        Assert.Equal(("료이치", 50_000L, 300L), (kill.Name, kill.Dealt, kill.Received));
        Assert.Equal(new SkillDamage[] { new SkillDamage(Grace, 1, 40_000), new SkillDamage(Earth, 1, 10_000) }.Select(s => (s.Key, s.Hits, s.Total)), kill.DealtSkills!.Select(s => (s.Key, s.Hits, s.Total)));
        Assert.Equal(new SkillDamage[] { new SkillDamage(Chain, 1, 300) }.Select(s => (s.Key, s.Hits, s.Total)), kill.ReceivedSkills!.Select(s => (s.Key, s.Hits, s.Total)));

        var death = Assert.Single(s.Kills.Killers());
        Assert.Equal(("저승사자", 1, -628L), (death.Name, death.Count, death.Points));
        Assert.Equal((2_000L, 90_000L), (death.Dealt, death.Received));
        Assert.Equal(new SkillDamage[] { new SkillDamage(Grace, 1, 90_000) }.Select(s => (s.Key, s.Hits, s.Total)), death.ReceivedSkills!.Select(s => (s.Key, s.Hits, s.Total)));
        Assert.Equal(1, s.Kills.Deaths);
    }

    [Fact]
    public void Skills_are_stored_with_the_kill_and_a_repeated_killer_adds_up()
    {
        using var store = new MeterStore(":memory:");
        var damage = new DamageStats(50_000, 300, 2, 1, [new(Grace, 1, 40_000), new(Earth, 1, 10_000)], [new(Chain, 1, 300)]);
        store.AddKill("1019:아라", new KillRecord(KillKind.Kill, "적", 2004, 2, 640, At(1), Enemy, damage));
        var back = store.KillsSince("1019:아라", At(0)).Single().Damage!;
        Assert.Equal(damage.DealtBySkill, back.DealtBySkill);
        Assert.Equal(damage.ReceivedBySkill, back.ReceivedBySkill);

        var list = new HourlyKillList();
        list.Add(new KillRecord(KillKind.Death, "저승사자", 2004, 2, -600, At(1), 4444, new DamageStats(0, 100, 0, 1, [], [new(Grace, 1, 100)])));
        list.Add(new KillRecord(KillKind.Death, "저승사자", 2004, 2, -700, At(2), 4444, new DamageStats(5, 300, 1, 2, [new(Earth, 1, 5)], [new(Grace, 2, 300)])));
        var killer = Assert.Single(list.Killers());
        Assert.Equal((2, -1300L, 5L, 400L), (killer.Count, killer.Points, killer.Dealt, killer.Received));
        Assert.Equal(new SkillDamage[] { new SkillDamage(Grace, 3, 400) }.Select(s => (s.Key, s.Hits, s.Total)), killer.ReceivedSkills!.Select(s => (s.Key, s.Hits, s.Total)));
    }
}

public class AttackerBreakdownTests
{
    private const uint Me = 13313, KillerId = 4444, HelperId = 5555, UnknownPlayer = 6666, Monster = 27094;
    private const uint Earth = 0x6563, Grace = 0x69AD;
    private static readonly DateTimeOffset H21 = new(2026, 10, 10, 21, 0, 0, Kst.Offset);
    private static DateTimeOffset At(int minutes, double seconds = 0) => H21.AddMinutes(minutes).AddSeconds(seconds);

    private static DamageMessage Hit(uint target, uint source, uint total, uint skillKey) =>
        new(target, source, 0x36, 20993, total, [], skillKey << 16);

    private static (string Name, int Server)? Names(uint e) => e switch
    {
        KillerId => ("저승사자", 2004),
        HelperId => ("조수", 2004),
        _ => null,
    };

    [Fact]
    public void Everyone_who_hurt_me_just_before_I_died_is_listed_with_their_share_and_skills()
    {
        var ledger = new DamageLedger();
        ledger.Add(Hit(Me, KillerId, 9_000, Grace), Me, At(1, 1));
        ledger.Add(Hit(Me, KillerId, 3_000, Earth), Me, At(1, 2));
        ledger.Add(Hit(Me, HelperId, 4_000, Earth), Me, At(1, 3));
        ledger.Add(Hit(Me, UnknownPlayer, 500, Earth), Me, At(1, 4));       // a player whose name never arrived
        ledger.Add(Hit(Me, Monster, 700, Grace), Me, At(1, 5));             // monsters and NPCs together
        ledger.Add(Hit(Me, 28000, 300, Grace), Me, At(1, 5));
        ledger.Add(Hit(7777, KillerId, 99_999, Grace), Me, At(1, 5));       // damage to someone else is not mine

        var list = ledger.TakeAttackers(At(1, 10), Names);
        Assert.Equal(["저승사자", "조수", "", ""], list.Select(a => a.Name));
        Assert.Equal([12_000L, 4_000L, 1_000L, 500L], list.Select(a => a.Total));
        Assert.Equal([KillerId, HelperId, 0u, UnknownPlayer], list.Select(a => a.Entity));
        Assert.True(list[2].IsNpc);
        Assert.Equal(1_000, list[2].Total);
        Assert.Equal([new SkillDamage(Grace, 1, 9_000), new SkillDamage(Earth, 1, 3_000)], list[0].Skills);
        Assert.Equal(2, list[2].Hits);
        Assert.Empty(ledger.TakeAttackers(At(1, 11), Names));                // taken: the next death starts over
    }

    [Fact]
    public void Hits_from_before_the_window_do_not_belong_to_this_death()
    {
        var ledger = new DamageLedger();
        ledger.Add(Hit(Me, HelperId, 4_000, Earth), Me, At(1));                         // 40 s before: too old
        ledger.Add(Hit(Me, KillerId, 1_000, Earth), Me, At(1, 30));
        var list = ledger.TakeAttackers(At(1, 40), Names);
        Assert.Equal(["저승사자"], list.Select(a => a.Name));
    }

    private static MessageEvent Msg(GameMessage m, DateTimeOffset t) =>
        new(new StreamToken(new FlowKey("10.0.0.1", 13328, "192.168.0.2", 50001), 0), t, m);

    [Fact]
    public void A_death_carries_its_attackers_into_the_death_list_adds_up_per_player_and_survives_the_store()
    {
        using var store = new MeterStore(":memory:");
        var s = new MeterSession(store, ResetMode.OnTheHour, 3, At(0));
        s.Start(At(0));
        s.Handle(Msg(new PointMessage(41_000_000), At(0, 30)));
        s.Handle(Msg(new EnterWorldMessage(Me, "아라", 1019), At(1)));
        s.Handle(Msg(new IdentityMessage(KillerId, "저승사자", 60, 2, 2004), At(1, 1)));
        s.Handle(Msg(new IdentityMessage(HelperId, "조수", 60, 2, 2004), At(1, 2)));

        for (int death = 0; death < 2; death++)
        {
            int m = 2 + death * 3;
            s.Handle(Msg(Hit(Me, HelperId, 4_000, Earth), At(m, 1)));
            s.Handle(Msg(Hit(Me, KillerId, 9_000, Grace), At(m, 2)));
            s.Handle(Msg(Hit(Me, Monster, 100, Earth), At(m, 3)));
            s.Handle(Msg(new DeathMessage(Me, KillerId, 2004, "저승사자", 2, true), At(m, 4)));
            s.Handle(Msg(new PointMessage((ulong)(41_000_000 - 628 * (death + 1))), At(m, 4.02)));
        }

        var killer = Assert.Single(s.Kills.Killers());
        Assert.Equal(2, killer.Count);
        Assert.Equal(["저승사자", "조수", ""], killer.Attackers!.Select(a => a.Name));
        Assert.Equal([18_000L, 8_000L, 200L], killer.Attackers!.Select(a => a.Total));       // two deaths added up per player

        // what the store gives back after a restart
        var back = store.KillsSince("1019:아라", At(0)).Where(k => k.Kind == KillKind.Death).ToList();
        Assert.Equal(2, back.Count);
        Assert.Equal(["저승사자", "조수", ""], back[0].Attackers!.Select(a => a.Name));
        Assert.Equal(9_000L, back[0].Attackers![0].Total);
        Assert.Equal([new SkillDamage(Grace, 1, 9_000)], back[0].Attackers![0].Skills);
        Assert.True(back[0].Attackers![2].IsNpc);
    }

    [Fact]
    public void Records_without_attackers_and_damaged_values_are_fine()
    {
        using var store = new MeterStore(":memory:");
        store.AddKill("1019:아라", new KillRecord(KillKind.Death, "옛기록", 2004, 2, -1, At(1)));
        Assert.Null(store.KillsSince("1019:아라", At(0)).Single().Attackers);
        Assert.Empty(AttackerDamage.Merge(null, null));
    }
}

/// <summary>The kill list window redraws only when the list changed (hundreds of rows rebuilt every second made it lag).</summary>
public class KillListRevisionTests
{
    [Fact]
    public void The_revision_goes_up_with_every_change_and_stays_when_nothing_changed()
    {
        var now = new DateTimeOffset(2026, 10, 14, 21, 10, 0, Kst.Offset);
        var list = new HourlyKillList();
        Assert.True(list.Roll(now));
        int r0 = list.Revision;
        Assert.False(list.Roll(now.AddMinutes(5)));                    // same hour: no change
        Assert.Equal(r0, list.Revision);
        Assert.True(list.Add(new KillRecord(KillKind.Kill, "상대", 2004, 2, 100, now)));
        int r1 = list.Revision;
        Assert.True(r1 > r0);
        Assert.True(list.AddUnknown(new UnmatchedGain(10, now)));
        Assert.True(list.Revision > r1);
        Assert.False(list.Add(new KillRecord(KillKind.Kill, "옛날", 2004, 2, 100, now.AddHours(-3))));   // another hour: not counted
        int r2 = list.Revision;
        Assert.True(list.Roll(now.AddHours(1)));
        Assert.True(list.Revision > r2);
    }
}

/// <summary>The official character search: addresses and picking the right character out of its answer (shape of the answer of 2026-10-23).</summary>
public class NcCharacterTests
{
    private const string Answer = """
        {"list":[{"characterId":"oNdWpKefMilYJ0ZlXchC866M7ryJevB5esxqPshSxOs%3D","name":"<strong>아라</strong>","race":1,"pcId":30,"level":50,"serverId":1019,"serverName":"이슈타르"},
                 {"characterId":"abc%3D","name":"<strong>아라</strong>주","race":1,"pcId":22,"level":45,"serverId":1019,"serverName":"이슈타르"},
                 {"characterId":"zzz%3D","name":"<strong>아라</strong>","race":1,"pcId":18,"level":35,"serverId":1005,"serverName":"유스티엘"}],
         "pagination":{"page":1,"size":10,"total":3,"endPage":1}}
        """;

    [Fact]
    public void The_exact_name_on_the_right_server_is_picked()
    {
        Assert.Equal("oNdWpKefMilYJ0ZlXchC866M7ryJevB5esxqPshSxOs%3D", NcCharacter.FindCharacterId(Answer, "아라", 1019));
        Assert.Equal("zzz%3D", NcCharacter.FindCharacterId(Answer, "아라", 1005));      // same name, other server
        Assert.Null(NcCharacter.FindCharacterId(Answer, "아라", 1002));                // not on that server
        Assert.Null(NcCharacter.FindCharacterId(Answer, "호", 1019));                  // a part of a name is not the name
        Assert.Null(NcCharacter.FindCharacterId("not json", "아라", 1019));
        Assert.Null(NcCharacter.FindCharacterId("{\"list\":[{\"name\":1}]}", "아라", 1019));
    }

    [Fact]
    public void The_addresses_are_the_ones_the_site_uses()
    {
        Assert.Equal("https://aion2.plaync.com/api/search/character?keyword=%EC%95%84%EB%9D%BC&race=1&serverId=1019&page=1&size=10", NcCharacter.SearchApiUrl("아라", 1019));
        Assert.Equal("https://aion2.plaync.com/ko-kr/characters/1019/oNd%3D", NcCharacter.DetailUrl(1019, "oNd%3D"));
        Assert.Contains("race=2&serverId=2004&keyword=", NcCharacter.SearchPageUrl("살두부", 2004));
    }

    [Fact]
    public void The_two_newer_servers_and_the_spelling_are_those_of_the_official_list()
    {
        Assert.Equal(["포에타", "이스할겐", "루터스"], new[] { Servers.Label(1021), Servers.Label(2021), Servers.Label(1012) });
        Assert.True(Servers.IsValid(1021) && Servers.IsValid(2021) && !Servers.IsValid(1022) && !Servers.IsValid(2022));
        Assert.Equal((1, 2), (Servers.RaceOf(1021), Servers.RaceOf(2021)));
    }
}