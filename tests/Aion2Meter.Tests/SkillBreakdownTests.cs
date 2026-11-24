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
        Assert.Equal([(Grace, 1, 9_000L), (Earth, 1, 3_000L)], list[0].Skills.Select(s => (s.Key, s.Hits, s.Total)));
        Assert.All(list[0].Skills, s => Assert.Equal(1, s.How!.Hits));                  // how the hits landed comes along (the detail window shows it per attacker)
        Assert.Equal(2, list[2].Hits);
        Assert.Empty(ledger.TakeAttackers(At(1, 11), Names));                // taken: the next death starts over
    }

    private static (string Name, int Server, int Class)? LiveNames(uint e) => e switch
    {
        KillerId => ("저승사자", 2004, 0),
        HelperId => ("조수", 2004, 0),
        _ => null,
    };

    [Fact]
    public void The_live_board_lists_opponents_by_damage_between_us_and_keeps_them_after_a_kill_or_death()
    {
        var ledger = new DamageLedger();
        ledger.Add(Hit(Me, KillerId, 9_000, Grace), Me, At(1, 1));                 // took 9,000 from the killer
        ledger.Add(Hit(KillerId, Me, 3_000, Earth), Me, At(1, 2));                 // did 3,000 to the killer
        ledger.Add(Hit(HelperId, Me, 20_000, Earth), Me, At(1, 3));                // did 20,000 to the helper
        ledger.Add(Hit(Monster, Me, 99_999, Earth), Me, At(1, 3));                 // monsters are not on the board
        var live = ledger.Live(At(1, 5), LiveNames);
        Assert.Equal(["조수", "저승사자"], live.Select(o => o.Name));                 // 20,000 before 12,000
        Assert.Equal((3_000L, 9_000L), (live[1].Dealt, live[1].Received));
        Assert.Single(live[1].DealtSkills);

        ledger.Take(KillerId, At(1, 6));                                           // the kill closes the fight record but not the board line
        Assert.Equal(2, ledger.Live(At(1, 7), LiveNames).Count);

        // a line with no hit for two minutes is gone, a new hit starts it over
        Assert.Empty(ledger.Live(At(3, 30), LiveNames));
        ledger.Add(Hit(Me, KillerId, 500, Grace), Me, At(4));
        Assert.Equal(500L, Assert.Single(ledger.Live(At(4, 1), LiveNames)).Received);

        ledger.ClearLive();
        Assert.Empty(ledger.Live(At(4, 2), LiveNames));
    }

    [Fact]
    public void An_attack_I_dodged_counts_in_how_the_attackers_hits_landed_but_is_not_a_hit()
    {
        var ledger = new DamageLedger();
        ledger.Add(Hit(Me, KillerId, 9_000, Grace), Me, At(1, 1));
        ledger.Add(new DodgeMessage(Me, KillerId, (uint)Grace << 16), Me, At(1, 2));
        ledger.Add(new DodgeMessage(Me, HelperId, (uint)Earth << 16), Me, At(1, 3));      // only dodged: still someone who attacked me
        var list = ledger.TakeAttackers(At(1, 10), Names);
        Assert.Equal(["저승사자", "조수"], list.Select(a => a.Name));
        Assert.Equal((9_000L, 1), (list[0].Total, list[0].Hits));                         // the dodge is not a hit
        Assert.Equal((1, 1), (list[0].Skills[0].How!.Hits, list[0].Skills[0].How!.Dodge));
        Assert.Equal((0L, 0, 1), (list[1].Total, list[1].Hits, list[1].Skills[0].How!.Dodge));
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
        Assert.Equal([(Grace, 1, 9_000L)], back[0].Attackers![0].Skills.Select(s => (s.Key, s.Hits, s.Total)));
        Assert.Equal(1, back[0].Attackers![0].Skills[0].How!.Hits);                       // and survives the store
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
/// <summary>A debuff of an enemy that lands on me, told by the name of its skill (docs/PROTOCOL.md §3.9).</summary>
public class DebuffWatcherTests
{
    private const uint Me = 2853, Enemy = 1221, Mate = 3160, Chain = 0x65BF;
    private static readonly DateTimeOffset T0 = new(2026, 11, 24, 21, 36, 20, Kst.Offset);
    private static bool Hostile(uint e) => e == Enemy;

    private static DamageMessage Hit(uint target, uint source, uint skillKey) => new(target, source, 0x36, 20993, 1000, [], skillKey << 16);

    [Fact]
    public void A_tick_a_shield_took_is_read_as_the_same_effect_without_an_amount()
    {
        // 21:36:25.000 recording: 고통의 연쇄 on me from #1221 while 유스티엘의 권능 (67d4) was on me
        var tick = Assert.IsType<PeriodicMessage>(MessageParser.Parse(Convert.FromHexString("0538a5160cc509af029370bf650177e3d46772790401")));
        Assert.Equal((Me, Enemy, 0x65BFu, 0u, true, true), (tick.Target, tick.Source, tick.SkillKey, tick.Amount, tick.Damage, tick.NoAmount));
        // a damage tick of the same effect before it is the plain 0A one
        var damage = Assert.IsType<PeriodicMessage>(MessageParser.Parse(Convert.FromHexString("0538a5160ac509af029370bf653b72790401")));
        Assert.True(damage.NoAmount);   // (the tail of this shape is not an amount: only that the effect is on me is read)
        Assert.Equal(0x65BFu, damage.SkillKey);
    }

    [Fact]
    public void An_effect_that_lands_and_ends_is_read_from_the_09_and_08_messages()
    {
        // 21:11:52.484 recording: 십자 방어 (6fa3) from #31903 on #30701
        var landed = Assert.IsType<EffectMessage>(MessageParser.Parse(Convert.FromHexString("0538ed77099f7cd00213b7a36f8c1512cc1d01")));
        Assert.Equal((0x6FA3u, false), (landed.SkillKey, landed.Ended));
        var ended = Assert.IsType<EffectMessage>(MessageParser.Parse(Convert.FromHexString("0538ed77089f7cd00213b7a36f8c1512cc1d01")));
        Assert.True(ended.Ended);
    }

    [Fact]
    public void A_shielded_tick_is_not_damage_in_the_ledger()
    {
        var ledger = new DamageLedger();
        ledger.Add(new PeriodicMessage(Me, Enemy, Chain, 0, true, NoAmount: true), Me, T0);
        Assert.Empty(ledger.Live(T0, _ => null));
    }

    [Fact]
    public void Tells_a_damage_debuff_once_per_application_and_again_after_it_ended()
    {
        var w = new DebuffWatcher();
        var tick = new PeriodicMessage(Me, Enemy, Chain, 59, true);
        Assert.Equal("고통의 연쇄", w.Observe(tick, Me, T0, Hostile));
        for (int i = 1; i <= 16; i++) Assert.Null(w.Observe(tick, Me, T0.AddSeconds(i * 0.5), Hostile));    // the ticks of the same debuff
        Assert.Null(w.Observe(tick with { Amount = 0, NoAmount = true }, Me, T0.AddSeconds(8.5), Hostile));   // also while a shield takes them
        Assert.Equal("고통의 연쇄", w.Observe(tick, Me, T0.AddSeconds(20), Hostile));                       // put on me anew
        Assert.Contains("고통의 연쇄", w.Seen.Keys);
    }

    [Fact]
    public void An_effect_is_named_by_the_skill_that_gave_it_and_one_skill_with_two_effects_is_one_alert()
    {
        var w = new DebuffWatcher();
        // 마르쿠탄의 분노 (6d41) hits me: a stun (100000311, named by hand) and the heal reduction of the skill (183300301); the heal reduction of the cast after is told again
        Assert.Null(w.Observe(Hit(Me, Enemy, 0x6D41), Me, T0, Hostile));
        Assert.Equal("기절", w.Observe(new EffectAppliedMessage(Me, Enemy, 100000311, 3000, 0x13), Me, T0.AddSeconds(0.01), Hostile));
        Assert.Equal("마르쿠탄의 분노", w.Observe(new EffectAppliedMessage(Me, Enemy, 183300301, 10000, 0x13), Me, T0.AddSeconds(0.05), Hostile));
        Assert.Null(w.Observe(new EffectAppliedMessage(Me, Enemy, 183300302, 10000, 0x13), Me, T0.AddSeconds(0.1), Hostile));   // same name: no second sound
        // later again (the effects of the next cast)
        Assert.Null(w.Observe(Hit(Me, Enemy, 0x6D41), Me, T0.AddSeconds(30), Hostile));
        Assert.Equal("마르쿠탄의 분노", w.Observe(new EffectAppliedMessage(Me, Enemy, 183300301, 10000, 0x13), Me, T0.AddSeconds(30.05), Hostile));
        Assert.Contains("마르쿠탄의 분노", w.Seen.Keys);
    }

    [Fact]
    public void An_effect_with_no_name_takes_the_skill_of_the_hit_before_it_else_its_id()
    {
        var w = new DebuffWatcher();
        Assert.Equal("효과 999999999", w.Observe(new EffectAppliedMessage(Me, Enemy, 999999999, 3000, 0x13), Me, T0, Hostile));
        Assert.Null(w.Observe(Hit(Me, Enemy, 0x6B59), Me, T0.AddSeconds(30), Hostile));
        Assert.Equal("격파쇄", w.Observe(new EffectAppliedMessage(Me, Enemy, 999999998, 3000, 0x13), Me, T0.AddSeconds(30.2), Hostile));   // named after the hit
        Assert.Null(w.Observe(Hit(Me, Mate, 0x6B68), Me, T0.AddSeconds(60), Hostile));                                                      // a hit of someone else does not name it
        Assert.Equal("효과 999999997", w.Observe(new EffectAppliedMessage(Me, Enemy, 999999997, 3000, 0x13), Me, T0.AddSeconds(60.2), Hostile));
    }

    [Fact]
    public void The_effects_of_a_분쇄격_and_a_대지의_약속_are_told_by_name()
    {
        // 23:00:35.825: 분쇄격 on a monster gives 방어력 감소 (181300002) and 허점 (100001301), 5 s each; a 호법 puts 대지의 약속 (187800011) on it with every hit
        var w = new DebuffWatcher();
        Assert.Equal("분쇄격", w.Observe(new EffectAppliedMessage(Me, Enemy, 181300002, 5000, 0x13), Me, T0, Hostile));
        Assert.Equal("허점", w.Observe(new EffectAppliedMessage(Me, Enemy, 100001301, 5000, 0x13), Me, T0, Hostile));
        Assert.Equal("대지의 약속", w.Observe(new EffectAppliedMessage(Me, Enemy, 187800011, 5000, 0x13), Me, T0.AddSeconds(0.1), Hostile));
    }

    [Fact]
    public void Not_told_the_09_message_a_friend_a_state_a_field_or_my_own()
    {
        var w = new DebuffWatcher();
        Assert.Null(w.Observe(new EffectMessage(Me, Enemy, 0x6C99), Me, T0, Hostile));                                          // sent for buffs and heals too
        Assert.Null(w.Observe(new EffectAppliedMessage(Me, Mate, 183300301, 10000, 0x13), Me, T0, Hostile));                    // a friend
        Assert.Null(w.Observe(new EffectAppliedMessage(Me, Enemy, 173500071, 2000, 0x11), Me, T0, Hostile));                    // a state of a skill, not a timed effect
        Assert.Null(w.Observe(new EffectAppliedMessage(Enemy, Me, 183300301, 10000, 0x13), Me, T0, Hostile));                   // mine on someone
        Assert.Null(w.Observe(new PeriodicMessage(Enemy, Me, Chain, 59, true), Me, T0, Hostile));                               // my tick on someone
        Assert.Null(w.Observe(new PeriodicMessage(Me, Me, Chain, 59, true), Me, T0, Hostile));                                  // my own
        Assert.Null(w.Observe(new PeriodicMessage(Me, Mate, 0x65DD, 1203, false), Me, T0, Hostile));                            // a heal
        Assert.Null(w.Observe(new PeriodicMessage(Me, Mate, 0x67D4, 5000, true), Me, T0, Hostile));                             // a shield flagged as damage
        Assert.Null(w.Observe(new PeriodicMessage(Me, Enemy, 0x5BBB, 100, true), Me, T0, Hostile));                             // a ground field (불의 장벽)
        Assert.Null(w.Observe(new PeriodicMessage(Me, Enemy, 0x5A99, 0, true, NoAmount: true), Me, T0, Hostile));              // 냉기 폭풍
        Assert.Null(w.Observe(new PeriodicMessage(Me, Enemy, Chain, 59, true), null, T0, Hostile));                             // my entity not known
        Assert.Empty(w.Seen.Where(x => x.Key is "불의 장벽" or "냉기 폭풍"));
    }

    [Fact]
    public void A_debuff_on_the_one_I_aim_at_is_told_when_it_comes_from_the_other_side()
    {
        const uint Mob = 50000, Other = 1222;          // a monster I aim at, another enemy
        bool Hostile3(uint e) => e is Enemy or Mob or Other;
        var w = new DebuffWatcher();
        // my hit on it, then a stun with no skill of its own: named after my hit
        Assert.Null(w.Observe(Hit(Mob, Me, 0x6D41), Me, T0, Hostile3, aimed: Mob));
        Assert.Equal("기절", w.Observe(new EffectAppliedMessage(Mob, Me, 100000311, 3000, 0x13), Me, T0.AddSeconds(0.01), Hostile3, aimed: Mob));
        Assert.Equal("마르쿠탄의 분노", w.Observe(new EffectAppliedMessage(Mob, Me, 183300301, 10000, 0x13), Me, T0.AddSeconds(0.05), Hostile3, aimed: Mob));
        // a friend's debuff on it counts too; an enemy's effect on it (their own side) does not; nor does its own
        Assert.Equal("허점", w.Observe(new EffectAppliedMessage(Mob, Mate, 100001301, 5000, 0x13), Me, T0.AddSeconds(10), Hostile3, aimed: Mob));
        Assert.Null(w.Observe(new EffectAppliedMessage(Mob, Other, 100000201, 5000, 0x13), Me, T0.AddSeconds(20), Hostile3, aimed: Mob));
        Assert.Null(w.Observe(new EffectAppliedMessage(Mob, Mob, 100000201, 5000, 0x13), Me, T0.AddSeconds(30), Hostile3, aimed: Mob));
        // not aiming at it (or the option off: no entity given): nothing
        Assert.Null(w.Observe(new EffectAppliedMessage(Mob, Me, 100000311, 3000, 0x13), Me, T0.AddSeconds(40), Hostile3));
        Assert.Null(w.Observe(new EffectAppliedMessage(Mob, Me, 100000311, 3000, 0x13), Me, T0.AddSeconds(41), Hostile3, aimed: 777));
        // aiming at a friend: an enemy\x27s debuff on them counts, mine does not
        Assert.Equal("기절", w.Observe(new EffectAppliedMessage(Mate, Enemy, 100000311, 3000, 0x13), Me, T0.AddSeconds(50), Hostile3, aimed: Mate));
        Assert.Null(w.Observe(new EffectAppliedMessage(Mate, Me, 100000311, 3000, 0x13), Me, T0.AddSeconds(60), Hostile3, aimed: Mate));
        // damage over time on the one I aim at: once per application, per target
        var tick = new PeriodicMessage(Mob, Me, Chain, 500, true);
        Assert.Equal("고통의 연쇄", w.Observe(tick, Me, T0.AddSeconds(70), Hostile3, aimed: Mob));
        Assert.Null(w.Observe(tick, Me, T0.AddSeconds(70.5), Hostile3, aimed: Mob));
    }

    [Fact]
    public void It_says_whether_the_debuff_landed_on_me_or_on_the_one_I_aim_at()
    {
        const uint Mob = 50000;
        bool Hostile3(uint e) => e is Enemy or Mob;
        var w = new DebuffWatcher();
        Assert.Equal("기절", w.Observe(new EffectAppliedMessage(Me, Enemy, 100000311, 3000, 0x13), Me, T0, Hostile3, aimed: Mob));
        Assert.True(w.LastOnMe);
        Assert.Equal("기절", w.Observe(new EffectAppliedMessage(Mob, Me, 100000311, 3000, 0x13), Me, T0.AddSeconds(10), Hostile3, aimed: Mob));
        Assert.False(w.LastOnMe);
    }

    [Fact]
    public void Hostile_means_a_monster_or_a_player_of_the_other_race_and_never_a_guess()
    {
        var matcher = new KillMatcher();
        matcher.EnterWorld(Me, "나", 1, 1019);
        matcher.OnIdentity(new IdentityMessage(Enemy, "적", 60, 2, 2004));
        matcher.OnIdentity(new IdentityMessage(Mate, "아군", 60, 1, 1019));
        Assert.True(matcher.IsEnemy(Enemy));
        Assert.False(matcher.IsEnemy(Mate));
        Assert.False(matcher.IsEnemy(4242));        // a player whose race is not known: not taken for an enemy
        Assert.True(matcher.IsEnemy(27094));        // a monster
        // a summon is as hostile as its owner (my 정령 gave effects to me and were taken for enemies)
        matcher.NoteSpawn(new SpawnMessage(24719, "나", 0x2C8EFE));
        matcher.NoteSpawn(new SpawnMessage(24720, "", 0x2C8EFE, Me));
        matcher.NoteSpawn(new SpawnMessage(24721, "", 0x2C8EFE, Enemy));
        matcher.NoteSpawn(new SpawnMessage(24722, "", 0x2C8EFE, Mate));
        Assert.False(matcher.IsEnemy(24719));       // mine, by name
        Assert.False(matcher.IsEnemy(24720));       // mine, by entity
        Assert.True(matcher.IsEnemy(24721));        // an enemy's
        Assert.False(matcher.IsEnemy(24722));       // a friend's
        Assert.True(matcher.IsEnemy(24723));        // no owner seen: a monster
    }

    [Theory]
    // the three effects of one 마르쿠탄의 분노 hit on monster #43620 (22:45:21.210 and .260): stun 3 s, 받는 치유량 감소 10 s, the third 5 s
    [InlineData("2a38e4d40201130137e2f505b80b00000000000082dacd0da2010000f74401b8b1170100b8b995468017f44700702f45", 100000311u, 3000u)]
    [InlineData("2a38e4d402011302cdf0ec0a1027000000000000daf5cd0da2010000f74401b8b1170100b8b995468017f44700702f45", 183300301u, 10000u)]
    [InlineData("2a38e4d402011303cb99310b881300000000000084e2cd0da2010000f74410618f1e0100b8b995468017f44700702f45", 187800011u, 5000u)]
    public void Reads_the_effect_id_and_how_long_it_lasts(string hex, uint effect, uint duration)
    {
        var m = Assert.IsType<EffectAppliedMessage>(MessageParser.Parse(Convert.FromHexString(hex)));
        Assert.Equal((43620u, effect, duration, (byte)0x13), (m.Target, m.EffectId, m.DurationMs, m.Kind));
    }

    [Fact]
    public void An_effect_id_is_named_by_the_skill_that_gave_it()
    {
        Assert.Equal("기절", EffectNames.Name(100000311));
        Assert.Equal("허점", EffectNames.Name(100001301));
        Assert.Equal("분쇄격", EffectNames.Name(181300002));
        Assert.Equal("송곳 화살", EffectNames.Name(140500201));     // 출혈 8 s
        Assert.Equal("봉인 화살", EffectNames.Name(141600201));     // 최대 행동력 감소 5 s
        Assert.Equal("그리폰 화살", EffectNames.Name(140600401));   // 홍염 20 s
        Assert.Equal("실명", EffectNames.Name(100000701));
        Assert.Equal("봉인", EffectNames.Name(100000801));
        Assert.Equal("회복 차단", EffectNames.Name(177600011));
        Assert.Equal("전투 속도 감소", EffectNames.Name(100000111));
        Assert.Equal("전투 속도 감소", EffectNames.Name(100000121));
        Assert.Equal("공포", EffectNames.Name(162100001));
        Assert.Equal("마르쿠탄의 분노", EffectNames.Name(183300301));
        Assert.Equal("효과 999", EffectNames.Name(999));
        Assert.Null(EffectNames.OfSkill(100000311));
    }
}
