using Aion2Meter.Core;
using Aion2Meter.Protocol;
using Xunit;

namespace Aion2Meter.Tests;

/// <summary>Messages from the recording of a two-person party formed and left twice (docs/PROTOCOL.md §3.7).</summary>
public class PartyTests
{
    private const string Formed = "009200a5e6120069a42390f3ba01000000ee030500000002010002010301892d0000fb03fb032442394543413646422d423741412d343636322d384536412d344243434330353541393544f3ba01000000ee0306ebb3b4eba6ac160000008aa2028aa202941c0000941c000000000000000000000000000000000000860300001600000086030000a8855f47b2f153470037c8470706ed9598eb829801000000";
    private const string Member = "0d920302b7090000fb03fb032444423045453930312d304146322d343030422d413841302d4332343141453535393835335cb701000000ee0306ec82aceb9e9112000000e5d703e5d7037f1c00007f1c0000000000000000000000000000000000008603000016000000860300001d785f470c4156470037c8470706ed9598eb82980100000032000000000000008b180000b9980d000000000001";
    private const string Left = "11925cb701000000ee03";

    private static GameMessage? Parse(string hex) => MessageParser.Parse(Convert.FromHexString(hex));

    [Fact]
    public void ParsesTheThreeMessages()
    {
        var formed = Assert.IsType<PartyFormedMessage>(Parse(Formed));
        Assert.Equal(113395UL, formed.CharacterId);
        Assert.Equal("보리", formed.Name);
        var member = Assert.IsType<PartyMemberMessage>(Parse(Member));
        Assert.Equal(112476UL, member.CharacterId);
        Assert.Equal("사랑", member.Name);
        Assert.Equal(112476UL, Assert.IsType<PartyLeftMessage>(Parse(Left)).CharacterId);
    }

    [Fact]
    public void TwoPersonPartyFormedAndLeftTwice()
    {
        var party = new PartyTracker();
        Assert.Equal(0, party.Size);
        for (int round = 0; round < 2; round++)
        {
            party.Handle((PartyMessage)Parse(Formed)!, "보리");
            party.Handle((PartyMessage)Parse(Member)!, "보리");
            Assert.Equal(2, party.Size);
            party.Handle((PartyMessage)Parse(Left)!, "보리");
            Assert.Equal(0, party.Size);
        }
    }

    [Fact]
    public void BiggerPartyShrinksOneByOneAndEndsWhenILeave()
    {
        var party = new PartyTracker();
        party.Handle(new PartyFormedMessage(1, "나", "G"), "나");
        party.Handle(new PartyMemberMessage(2, "가", "G"), "나");
        party.Handle(new PartyMemberMessage(3, "다", "G"), "나");
        party.Handle(new PartyMemberMessage(3, "다", "G"), "나"); // repeated info: still one member
        Assert.Equal(3, party.Size);
        party.Handle(new PartyLeftMessage(2), "나");
        Assert.Equal(2, party.Size);
        party.Handle(new PartyLeftMessage(1), "나");
        Assert.Equal(0, party.Size);
    }

    [Fact]
    public void ClearForgetsTheParty()
    {
        var party = new PartyTracker();
        party.Handle(new PartyFormedMessage(1, "a", "G"), "a");
        party.Handle(new PartyMemberMessage(2, "b", "G"), "a");
        Assert.True(party.Clear());
        Assert.Equal(0, party.Size);
    }
}

/// <summary>Three-person party recording (leader 보리): a joiner may announce itself with 08 92 only.</summary>
public class PartyJoinTests
{
    private const string JoinOnly = "0892fb032431443243363632362d463844362d344539382d413546382d4144324634374641443936321e8a01000000ee03af4c0e000000320000000000000006ec82aceb9e91d83d812fa1010000";
    private const string Joiner = "0892fb032444423045453930312d304146322d343030422d413841302d433234314145353539383533069c02000000ee030016000000300000000000000006eb8ba4ec869cfd05812fa1010000";

    [Fact]
    public void JoinMessageGivesIdAndName()
    {
        var m = Assert.IsType<PartyMemberMessage>(MessageParser.Parse(Convert.FromHexString(JoinOnly)));
        Assert.Equal(100894UL, m.CharacterId);
        Assert.Equal("사랑", m.Name);
        Assert.Equal("다솜", Assert.IsType<PartyMemberMessage>(MessageParser.Parse(Convert.FromHexString(Joiner))).Name);
    }

    [Fact]
    public void ThreePartyCountsJoinersAndShrinksWhenOneLeaves()
    {
        var party = new PartyTracker();
        party.Handle(new PartyFormedMessage(113395, "보리", "G"), "보리");
        party.Handle((PartyMessage)MessageParser.Parse(Convert.FromHexString(JoinOnly))!, "보리");
        party.Handle((PartyMessage)MessageParser.Parse(Convert.FromHexString(Joiner))!, "보리");
        party.Observe(new PartyStatusMessage(100894), DateTimeOffset.UnixEpoch);   // a joiner counts once their status messages come
        party.Handle(new PartyMemberMessage(171014, "다솜", "G"), "보리"); // the full info follows: still the same member
        Assert.Equal(3, party.Size);
        party.Handle(new PartyLeftMessage(171014), "보리");
        Assert.Equal(2, party.Size);
        Assert.Contains("다솜", party.Names.Concat(["다솜"]));
        Assert.DoesNotContain("다솜", party.Names);
    }
}

public class PartyRecordTests
{
    private static readonly DateTimeOffset H21 = new(2026, 10, 10, 21, 0, 0, Kst.Offset);

    [Fact]
    public void BucketSplitsPointsByPartySizeAndKeepsThemInTheStore()
    {
        var book = new BucketBook(0, [], H21);
        book.Resume(H21);
        book.Add(100, H21.AddMinutes(1));          // alone
        book.Add(300, H21.AddMinutes(2), 3);
        book.Add(-50, H21.AddMinutes(3), 3);
        book.Add(200, H21.AddMinutes(4), 6);       // more than the columns: summed in the last one
        var b = book.Current!;
        Assert.Equal(600, b.Gain);
        Assert.Equal(300, b.Parties[3].Gain);
        Assert.Equal(50, b.Parties[3].Loss);

        using var store = new MeterStore(":memory:");
        store.SaveBucket("1019:아라", b);
        var back = Assert.Single(store.Buckets("1019:아라", H21, H21.AddHours(1)));
        Assert.Equal(b.EncodeParties(), back.EncodeParties());
    }
}

public class EnterTotalTests
{
    private static readonly FlowKey Flow = new("10.0.0.1", 13328, "192.168.0.2", 50001);
    private static readonly StreamToken Token = new(Flow, 0);
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 18, 46, 0, Kst.Offset);

    [Fact]
    public void EnterMessageCarriesTheTotal()
    {
        var m = Assert.IsType<EnterWorldMessage>(MessageParser.Parse(Bytes.EnterWorldWithTotal(13313, "보리", 1019, 6_735_704)));
        Assert.Equal(6_735_704UL, m.Points);
        Assert.Null(Assert.IsType<EnterWorldMessage>(MessageParser.Parse(Bytes.EnterWorld(13313, "보리", 1019))).Points);
    }

    [Fact]
    public void FirstChangeAfterLoginIsCountedEvenWithoutARememberedTotal()
    {
        using var store = new MeterStore(":memory:");
        var s = new MeterSession(store, ResetMode.OnTheHour, 3, T0);
        s.Start(T0);
        s.Handle(new MessageEvent(Token, T0.AddSeconds(1), new EnterWorldMessage(13313, "보리", 1019, 6_735_704)));
        Assert.Equal(6_735_704UL, s.Points.Total);
        s.Handle(new MessageEvent(Token, T0.AddSeconds(25), new PointMessage(6_735_717)));
        Assert.Equal(13, s.Buckets.Current!.Net);   // without the enter total this first change would only have been a baseline
    }

    [Fact]
    public void EnterTotalReplacesAStaleRememberedTotal()
    {
        var tracker = new PointTracker();
        tracker.Start(remembered: 6_730_000);      // the meter was off while 5,704 points came in
        tracker.OnPoint(Token, 6_735_704, T0, exact: true);
        Assert.Equal(13, tracker.OnPoint(Token, 6_735_717, T0.AddSeconds(20))!.Value.Delta);
    }

    [Fact]
    public void ZoneMoveKeepsTheRunningTotal()
    {
        var tracker = new PointTracker();
        tracker.Start();
        tracker.OnPoint(Token, 6_735_704, T0);
        Assert.Equal(0, tracker.OnPoint(Token, 6_735_704, T0.AddSeconds(5), exact: true)!.Value.Delta);
    }
}

public class PartyListTests
{
    // 00 92 of a new connection (zone move) that lists the whole party: 아라 (leader) and 사랑.
    private const string Whole = "009200e7fd120069a42390513800000000ee0305000000020100020207010d220000fb03ca0b2442394543413646422d423741412d343636322d384536412d344243434330353541393544513800000000ee0306ec9584eb9dbc1e000000aac603b6dc03b4280000b42800000000000000000000000000000000000000000000000000000000000010740d47ada7644700b4ec463f06ed9598eb8298010000003200000000000000dd1800000100d8590901142b2b0a0000000000000302d11a0000fb03ca0b2444423045453930312d304146322d343030422d413841302d4332343141453535393835335cb701000000ee0306ec82aceb9e9112000000e5d703e5d7037f1c00007f1c0000000000000000000000000000000000001600000003000000a80300000bfbe94649ec5ec6478bde4506ed9598eb82980100000032000000000000008b180000b9980d00000000000105";

    [Fact]
    public void NewConnectionListsTheWholeParty()
    {
        var formed = Assert.IsType<PartyFormedMessage>(MessageParser.Parse(Convert.FromHexString(Whole)));
        Assert.Equal("아라", formed.Name);
        Assert.Equal([(112476UL, "사랑")], formed.Others);
        var party = new PartyTracker();
        party.Handle(formed, "아라");
        Assert.Equal(2, party.Size);
    }
}

public class SkillNameTests
{
    [Theory]
    [InlineData(0x660B, "쾌유의 광휘")]
    [InlineData(0x6DAC, "관통쇄")]
    [InlineData(0x6BE2, "암격쇄")]
    [InlineData(0x4DD8, "기습")]
    [InlineData(0x4E43, "문양 폭발")]
    public void NamesFromTheCombatLogAreKnown(uint key, string name)
    {
        Assert.True(Skills.IsKnown(key));
        Assert.Equal(name, Skills.Name(key));
    }
}

public class PartyObservedTests
{
    private const string Status = "1c925cb701000000ee0312000000860300001600000086030000d5bd5f47c54756476b38c84732000000000000008b18000006ed9598eb82980100000001b9980d000000000001";
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 19, 10, 0, Kst.Offset);

    [Fact]
    public void ParsesStatusMessage()
    {
        Assert.Equal(112476UL, Assert.IsType<PartyStatusMessage>(MessageParser.Parse(Convert.FromHexString(Status))).CharacterId);
    }

    [Fact]
    public void PartyJoinedBeforeTheMeterStartedIsSeenFromStatusMessages()
    {
        var party = new PartyTracker();
        Assert.Equal(0, party.Size);
        party.Observe(new PartyStatusMessage(112476), T0);
        Assert.Equal(2, party.Size);                                   // me and the member heard from
        party.Observe(new PartyStatusMessage(112476), T0.AddSeconds(1));
        Assert.Equal(2, party.Size);
        party.Observe(new PartyStatusMessage(2336), T0.AddSeconds(2));
        Assert.Equal(3, party.Size);
    }

    [Fact]
    public void SilentMembersOfAnObservedPartyExpire()
    {
        var party = new PartyTracker();
        party.Observe(new PartyStatusMessage(1), T0);
        party.Observe(new PartyStatusMessage(2), T0);
        party.Observe(new PartyStatusMessage(2), T0.AddSeconds(50));
        party.Expire(T0.AddSeconds(70));                               // 1 was silent for 70 s
        Assert.Equal(2, party.Size);
        party.Expire(T0.AddSeconds(130));
        Assert.Equal(0, party.Size);
    }

    [Fact]
    public void AFormedPartyDoesNotExpireAndLearnsAMissedMember()
    {
        var party = new PartyTracker();
        party.Handle(new PartyFormedMessage(1, "나", "G", [(2UL, "가")]), "나");
        party.Expire(T0.AddHours(1));
        Assert.Equal(2, party.Size);
        party.Observe(new PartyStatusMessage(3), T0);
        Assert.Equal(3, party.Size);
        party.Observe(new PartyStatusMessage(2), T0);                  // already known
        Assert.Equal(3, party.Size);
    }
}

public class LifeStealTailTests
{
    // Hits of 사랑's life-stealing skills (20:11 recording): a varint after the final 01 00 = what the attacker healed.
    private const string Blocked713 = "0438b0453604f640748dc60002038a00025b418f4d01000000cd9301f90602585801009503";
    private const string Plain641 = "0438b0451604f64094dbc6000803aa800102dbc5ad4d01000000b59b01810501009303";

    [Fact]
    public void DamageWithAHealTailIsParsed()
    {
        var a = Assert.IsType<DamageMessage>(MessageParser.Parse(Convert.FromHexString(Blocked713)));
        Assert.Equal(889UL, a.Total);
        Assert.Equal(713L, a.Main);
        Assert.Equal<uint>(0x4D8F, a.SkillKey);
        var b = Assert.IsType<DamageMessage>(MessageParser.Parse(Convert.FromHexString(Plain641)));
        Assert.Equal(641UL, b.Total);
        Assert.Equal<uint>(0x4DAD, b.SkillKey);
    }
}

public class SkillAliasTests
{
    [Fact]
    public void BothKeysOfCheonbeolAreOneSkill()
    {
        Assert.Equal("천벌", Skills.Name(0x65A0));
        Assert.Equal("천벌", Skills.Name(0x6591));
        Assert.True(Skills.IsKnown(0x6591));
        var merged = SkillDamage.Merge([new SkillDamage(0x65A0, 2, 2300), new SkillDamage(0x6591, 2, 2000)], [new SkillDamage(0x6563, 1, 100)]);
        Assert.Equal([new SkillDamage(0x65A0, 4, 4300), new SkillDamage(0x6563, 1, 100)], merged);
    }

    [Fact]
    public void NoTwoKeysShareAName()
    {
        // The only skill with two keys is the aliased one: another duplicate would be a naming mistake.
        var names = typeof(Skills).GetField("Names", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null) as IReadOnlyDictionary<uint, string>;
        Assert.NotNull(names);
        Assert.Empty(names!.GroupBy(n => n.Value).Where(g => g.Count() > 1));
    }
}

public class HitModsTests
{
    // Real hits of 사랑 on me (20:18 recording); the in-game statistics of the 35-hit fight they come from matched every count.
    private static DamageMessage Parse(string hex) => Assert.IsType<DamageMessage>(MessageParser.Parse(Convert.FromHexString(hex)));

    [Fact]
    public void CriticalFrontMultiRegen()
    {
        var d = Parse("0438b0453600f640ca91c6009303a0c40102f3f2904d01000000b59b01d807036262620100");
        Assert.Equal(690L, d.Main);
        Assert.Equal<HitMods>(HitMods.Critical | HitMods.Front | HitMods.HasDirection | HitMods.Multi | HitMods.Regen, d.Mods);
    }

    [Fact]
    public void BackSmashCriticalMultiHitNumberFour()
    {
        var d = Parse("0438b0453600f640fa06c7009d03880001e1feb94d04000000b59b01d20a0188010400");
        Assert.Equal(4u, d.HitIndex);
        Assert.Equal<HitMods>(HitMods.Critical | HitMods.Smash | HitMods.Multi | HitMods.HasDirection, d.Mods);   // from behind: no Front
    }

    [Fact]
    public void BlockedAndNotCritical()
    {
        var weapon = Parse("0438b0450600f640c859d100a902620001c711c75101000000904e010100");
        Assert.Equal<HitMods>(HitMods.Block | HitMods.Regen | HitMods.HasDirection, weapon.Mods);   // count 2: not critical, 62: weapon block + regen
        var front = Parse("0438b0450600f640c559d1009903020002c711c75101000000904e050100");
        Assert.Equal<HitMods>(HitMods.Critical | HitMods.Block | HitMods.Front | HitMods.HasDirection, front.Mods);
    }

    [Fact]
    public void ARegenValueWithLowBitsIsNoBlock()
    {
        // 20:11, 절혼 베기 259: F = a2 starts a longer value; the game showed no block tag.
        var d = Parse("0438b0453600f640a402c7006b03a8db010201d0b94d04000000b59b01cb08036d6d6d04009603");
        Assert.False(d.Mods.HasFlag(HitMods.Block));
    }
}

public class HitStatsTests
{
    private static DamageMessage Hit(HitMods mods, uint healed = 0) => new(1, 2, 0x36, 3, 100, [], 0x65A00000, 1, mods, healed);

    [Fact]
    public void CountsEachKindAndRoundTrips()
    {
        var h = HitStats.Empty;
        h = h.Add(Hit(HitMods.Critical | HitMods.Front | HitMods.HasDirection | HitMods.Multi));
        h = h.Add(Hit(HitMods.Smash | HitMods.HasDirection | HitMods.Regen, 405));
        h = h.Add(Hit(HitMods.Block | HitMods.Front | HitMods.HasDirection));
        Assert.Equal(new HitStats(3, 1, 1, 1, 2, 1, 1, 1, 405, 0, 3, 100), h);
        Assert.Equal(h, HitStats.Decode(h.Encode()));
        Assert.Equal(new HitStats(6, 2, 2, 2, 4, 2, 2, 2, 810, 0, 6, 100), HitStats.Merge(h, h));
        Assert.True(HitStats.Decode("garbage").IsEmpty);
    }

    [Fact]
    public void KillStoredWithItsHitStats()
    {
        using var store = new MeterStore(":memory:");
        var when = new DateTimeOffset(2026, 10, 12, 20, 0, 0, Kst.Offset);
        var how = new HitStats(35, 33, 15, 4, 7, 28, 25, 17, 0);
        store.AddKill("1019:아라", new KillRecord(KillKind.Kill, "사랑", 1019, 1, 600, when,
            Damage: new DamageStats(100, 38213, 1, 35, [], [], HitStats.Empty, how)));
        var back = Assert.Single(store.KillsSince("1019:아라", when.AddHours(-1)));
        Assert.Equal(how, back.Damage!.ReceivedHow);
    }
}

public class HitStatsConsistencyTests
{
    private const uint Me = 100, Other = 200;

    [Fact]
    public void NoKindOfHitCanExceedTheHitsOfItsSkill()
    {
        var ledger = new DamageLedger();
        var t = new DateTimeOffset(2026, 10, 12, 20, 0, 0, Kst.Offset);
        var all = new[] { HitMods.Critical, HitMods.Smash, HitMods.Block, HitMods.Front, HitMods.Multi, HitMods.Regen, HitMods.Perfect, HitMods.HasDirection };
        for (int i = 0; i < 40; i++)
        {
            var mods = HitMods.None;
            for (int k = 0; k < all.Length; k++) if ((i * 7 + k * 3) % 5 < 2) mods |= all[k];
            ledger.Add(new DamageMessage(Me, Other, 0x36, 1, (uint)(100 + i), [], (uint)(0x4D8F0000 + (i % 3 == 0 ? 0x10000 : 0)), (uint)(i % 4 + 1), mods, 0), Me, t);
        }
        var stats = ledger.Take(Other, t)!;
        foreach (var skill in stats.ReceivedBySkill!)
        {
            var h = skill.How!;
            Assert.Equal(skill.Hits, h.Hits);
            foreach (int n in new[] { h.Critical, h.Smash, h.Block, h.Front, h.Back, h.Multi, h.Regen, h.Perfect, h.Casts })
                Assert.InRange(n, 0, h.Hits);
            Assert.InRange(h.Front + h.Back, 0, h.Hits);   // a hit comes from the front or from behind, never both
        }
        Assert.Equal(stats.ReceivedHits, stats.ReceivedHow!.Hits);
        Assert.Equal(stats.ReceivedBySkill!.Sum(s => s.Hits), stats.ReceivedHow.Hits);
    }
}

public class DuelTests
{
    // 0B 94 of two duels of the 20:52 recording (아라 = me, entity 8880; 사랑 = 15407): first lost, second won.
    private const string Lost = "0b94b0220000022f3c0000b022000006ec82aceb9e9106ec9584eb9dbc";
    private const string Won = "0b94b022000002b02200002f3c000006ec9584eb9dbc06ec82aceb9e91";
    private static readonly FlowKey Flow = new("10.0.0.1", 13328, "192.168.0.2", 50001);
    private static readonly StreamToken Token = new(Flow, 0);
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 20, 50, 0, Kst.Offset);

    [Fact]
    public void ParsesWinnerAndLoser()
    {
        var lost = Assert.IsType<DuelMessage>(MessageParser.Parse(Convert.FromHexString(Lost)));
        Assert.Equal((15407u, 8880u, "사랑", "아라"), (lost.WinnerEntity, lost.LoserEntity, lost.WinnerName, lost.LoserName));
        var won = Assert.IsType<DuelMessage>(MessageParser.Parse(Convert.FromHexString(Won)));
        Assert.Equal((8880u, 15407u, "아라", "사랑"), (won.WinnerEntity, won.LoserEntity, won.WinnerName, won.LoserName));
    }

    [Fact]
    public void WinsGoToTheKillListAndLossesToTheDeathList()
    {
        using var store = new MeterStore(":memory:");
        var s = new MeterSession(store, ResetMode.OnTheHour, 3, T0);
        s.Start(T0);
        s.Handle(new MessageEvent(Token, T0.AddSeconds(1), new EnterWorldMessage(8880, "아라", 1019)));
        s.Handle(new MessageEvent(Token, T0.AddSeconds(2), new IdentityMessage(15407, "사랑", 55, 1, 1019)));
        foreach (var (hex, at) in new[] { (Lost, 10), (Won, 20), (Lost, 30), (Won, 40) })
            s.Handle(new MessageEvent(Token, T0.AddSeconds(at), (GameMessage)MessageParser.Parse(Convert.FromHexString(hex))!));

        Assert.Equal(2, s.Kills.KillCount);
        Assert.Equal(2, s.Kills.Deaths);
        var win = Assert.Single(s.Kills.Sorted());
        Assert.Equal(("사랑", 2, 0L), (win.Name, win.Count, win.Points));      // no points for a duel
        Assert.All(win.Entries!, e => Assert.True(e.Duel));
        var loss = Assert.Single(s.Kills.Killers());
        Assert.Equal(("사랑", 2), (loss.Name, loss.Count));
        // saved and loaded again
        var again = new MeterSession(store, ResetMode.OnTheHour, 3, T0.AddSeconds(50));
        Assert.Equal((2, 2), (again.Kills.KillCount, again.Kills.Deaths));
    }

    [Fact]
    public void ADuelOfOthersIsIgnored()
    {
        using var store = new MeterStore(":memory:");
        var s = new MeterSession(store, ResetMode.OnTheHour, 3, T0);
        s.Handle(new MessageEvent(Token, T0.AddSeconds(1), new EnterWorldMessage(8880, "아라", 1019)));
        s.Handle(new MessageEvent(Token, T0.AddSeconds(2), new DuelMessage(1, 2, "가", "나")));
        Assert.Equal((0, 0), (s.Kills.KillCount, s.Kills.Deaths));
    }
}

public class HealReceivedTests
{
    private const uint Me = 1320, Other = 200;
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 21, 4, 0, Kst.Offset);


    [Fact]
    public void A_party_seen_only_by_status_messages_counts_its_unnamed_members()
    {
        var party = new PartyTracker();
        party.Observe(new PartyStatusMessage(111), T0);
        party.Observe(new PartyStatusMessage(222), T0);
        Assert.Equal(3, party.Size);              // me and two others
        Assert.Empty(party.Names);                // no names yet: the tooltip must say so
        Assert.Equal(2, party.UnknownCount);      // I am not counted
        party.Handle(new PartyMemberMessage(111, "동료", "id"), "나");
        Assert.Equal(["동료"], party.Names);
        Assert.Equal(1, party.UnknownCount);
        Assert.Equal(0, new PartyTracker().UnknownCount);
    }


    [Fact]
    public void Only_the_rank_point_board_is_a_ranking()
    {
        var main = Assert.IsType<RankingMessage>(MessageParser.Parse(Convert.FromHexString(Real.MainBoardRank)));
        Assert.Equal((23u, 8, 3055u), (main.Entries[0].Rank, main.Filter, main.Total));
        Assert.Null(MessageParser.Parse(Convert.FromHexString(Real.BossBoardRank)));
    }
    [Fact]
    public void ParsesAnOverTimeTick()
    {
        var p = Assert.IsType<PeriodicMessage>(MessageParser.Parse(Convert.FromHexString("0538a80a0ba80a4713f5dd65987ab30992c70401")));
        Assert.Equal((1320u, 1320u, 0x65DDu, 1203u), (p.Target, p.Source, p.SkillKey, p.Amount));
        Assert.Equal("재생의 빛", Skills.Name(p.SkillKey));
        // the first tick has a longer counter
        Assert.Equal(1203u, Assert.IsType<PeriodicMessage>(MessageParser.Parse(Convert.FromHexString("0538a80a0ba80a4713f5dd65cb8301b30992c70401"))).Amount);
        // an effect that has no amount (type 09) is not a tick: it is the message that the effect landed
        var landed = Assert.IsType<EffectMessage>(MessageParser.Parse(Convert.FromHexString("0538a80a09a80a4713f5dd65fe8c0192c70401")));
        Assert.Equal((1320u, 1320u, 0x65DDu, false), (landed.Target, landed.Source, landed.SkillKey, landed.Ended));
    }

    [Fact]
    public void ParsesTicksWhoseSecondFieldIsALongerVarint()
    {
        // 00:35:53 and 00:36:00 of the 2026-10-14 recording: 면죄 (670e) 4,222 and 재생의 빛 (65dd) 1,195, as the chat log says
        var a = Assert.IsType<PeriodicMessage>(MessageParser.Parse(Convert.FromHexString(Real.TickMyeonjoe)));
        Assert.Equal((0x670Eu, 4222u, false), (a.SkillKey, a.Amount, a.Damage));
        Assert.Equal("면죄", Skills.Name(a.SkillKey));
        var b = Assert.IsType<PeriodicMessage>(MessageParser.Parse(Convert.FromHexString(Real.TickRegen)));
        Assert.Equal((0x65DDu, 1195u), (b.SkillKey, b.Amount));
        Assert.Equal("재생의 빛", Skills.Name(b.SkillKey));
    }

    [Fact]
    public void FightCarriesTheLifeIReceivedAfterItsFirstBlowButNoActionPoints()
    {
        var ledger = new DamageLedger();
        DamageMessage Recovery(uint skill, uint amount, uint effect = 0) => new(Me, Me, 0x04, 1, amount, [], skill << 16 | effect);
        ledger.Add(Recovery(0x660B, 9000), Me, T0);                                   // before the fight: not part of it
        ledger.Add(new DamageMessage(Me, Other, 0x16, 2, 500, [], 0x4D8F0000, 1), Me, T0.AddSeconds(1));
        ledger.Add(Recovery(0x660B, 9752), Me, T0.AddSeconds(2));
        ledger.Add(Recovery(0x6648, 2401, 0x2703), Me, T0.AddSeconds(3));
        ledger.Add(Recovery(0x6648, 20000, 0x1F97), Me, T0.AddSeconds(3));            // 200 action points
        ledger.Add(new PeriodicMessage(Me, Me, 0x65DD, 1203), Me, T0.AddSeconds(4));
        ledger.Add(new PeriodicMessage(Me, Me, 0x65DD, 1203), Me, T0.AddSeconds(6));
        ledger.Add(new PeriodicMessage(Other, Other, 0x65DD, 999), Me, T0.AddSeconds(6));   // someone else's tick
        var heal = ledger.Take(Other, T0.AddSeconds(7))!.HealBySkill!;
        Assert.Equal(new[] { (0x660Bu, 1, 9752L), (0x6648u, 1, 2401L), (0x65DDu, 2, 2406L) }.OrderByDescending(x => x.Item3), heal.Select(h => (h.Key, h.Hits, h.Total)));
    }


    [Fact]
    public void RecoveriesOfPointsAreToldFromLifeByTheirAmountsEvenForSkillsNotNamedYet()
    {
        var ledger = new DamageLedger();
        DamageMessage Recovery(uint skill, uint amount, uint effect) => new(Me, Me, 0x04, 1, amount, [], skill << 16 | effect);
        ledger.Add(new DamageMessage(Me, Other, 0x16, 2, 500, [], 0x4D8F0000, 1), Me, T0);
        // two effects of skills nobody has named: 250 and 1000 action points (recordings of 2026-10-13/14), each time a multiple of 100
        for (int i = 0; i < 3; i++) ledger.Add(Recovery(0x6C1F, 25000, 0x7448), Me, T0.AddSeconds(1 + i));
        for (int i = 0; i < 3; i++) ledger.Add(Recovery(0x6C4D, 100000, 0x47ED), Me, T0.AddSeconds(1 + i));
        // a life effect whose amount is round now and then: once it has shown a plain number it is life, round or not
        ledger.Add(Recovery(0x660B, 9752, 0x1F93), Me, T0.AddSeconds(4));
        ledger.Add(Recovery(0x660B, 9800, 0x1F93), Me, T0.AddSeconds(5));
        // the other effect of the same skill is judged by itself: the points of 생명의 권능 stay out, its life counts
        ledger.Add(Recovery(0x6648, 20000, 0x1F97), Me, T0.AddSeconds(6));
        ledger.Add(Recovery(0x6648, 2449, 0x2703), Me, T0.AddSeconds(6));
        var heal = ledger.Take(Other, T0.AddSeconds(7))!.HealBySkill!;
        Assert.Equal(new[] { (0x660Bu, 2, 19552L), (0x6648u, 1, 2449L) }.OrderByDescending(x => x.Item3), heal.Select(h => (h.Key, h.Hits, h.Total)));
    }
    [Fact]
    public void HealedSkillsAreStoredWithTheKill()
    {
        using var store = new MeterStore(":memory:");
        var when = new DateTimeOffset(2026, 10, 12, 21, 0, 0, Kst.Offset);
        SkillDamage[] heal = [new(0x660B, 2, 20000), new(0x65DD, 5, 6015)];
        store.AddKill("1019:아라", new KillRecord(KillKind.Kill, "사랑", 1019, 1, 600, when, Damage: new DamageStats(100, 200, 1, 1, [], [], null, null, heal)));
        var back = Assert.Single(store.KillsSince("1019:아라", when.AddHours(-1))).Damage!;
        Assert.Equal(heal.Select(h => (h.Key, h.Hits, h.Total)), back.HealBySkill!.Select(h => (h.Key, h.Hits, h.Total)));
    }
}

public class RegenStatTests
{
    private const uint Me = 8880, Other = 8310;
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 20, 18, 0, Kst.Offset);

    [Theory]
    [InlineData("0438b0453600f640ca91c6009303a0c40102f3f2904d01000000b59b01d807036262620100", 984, 196)]
    [InlineData("0438b0453604f64094dbc6002202a25602dbc5ad4d01000000b59b01af03042b2b2b2b0100a903", 431, 86)]
    [InlineData("0438b0453600f640eadfc6009703a8f101028dbcaa4d02000000b59b01b80904787878780200", 1208, 241)]
    public void RegenIsTwentyPercentOfTheHitAndReadFromTheMessage(string hex, int total, int regained)
    {
        var d = Assert.IsType<DamageMessage>(MessageParser.Parse(Convert.FromHexString(hex)));
        Assert.Equal((uint)total, d.Total);
        Assert.Equal((uint)regained, d.Regained);
        Assert.Equal(total / 5, regained);
    }

    [Fact]
    public void ItCountsAsLifeIReceivedInTheFight()
    {
        var ledger = new DamageLedger();
        var hit = new DamageMessage(Me, Other, 0x36, 1, 984, [98, 98, 98], 0x4D900000, 1, HitMods.Regen | HitMods.Critical, 0, 196);
        ledger.Add(hit, Me, T0);
        ledger.Add(hit, Me, T0.AddSeconds(1));
        var heal = Assert.Single(ledger.Take(Other, T0.AddSeconds(2))!.HealBySkill!);
        Assert.Equal((Skills.RegenStat, 2, 392L), (heal.Key, heal.Hits, heal.Total));
        Assert.Equal("재생 (스탯)", Skills.Name(heal.Key));
    }

    [Fact]
    public void ARegenOnAHitIDealtIsTheOpponentsAndNotMine()
    {
        var ledger = new DamageLedger();
        ledger.Add(new DamageMessage(Other, Me, 0x36, 1, 984, [], 0x4D900000, 1, HitMods.Regen, 0, 196), Me, T0);
        Assert.Empty(ledger.Take(Other, T0.AddSeconds(1))!.HealBySkill!);
    }
}

public class DuelSkillNameTests
{
    [Theory]
    [InlineData(0x4DE7, "암습")]
    [InlineData(0x4EF9, "연막탄")]
    [InlineData(0x4D9A, "암검 투척")]
    [InlineData(0x4E05, "암검 추적")]
    [InlineData(0x4F96, "심장 찌르기")]
    [InlineData(0x4DC8, "섬광 베기")]
    [InlineData(0x4D8C, "빠른 베기")]   // alias of 4d8f
    public void NamesFromTheDuelLog(uint key, string name) => Assert.Equal(name, Skills.Name(key));
}

public class ShieldHitTests
{
    // 21:29 duel: hits that met my shield (유스티엘의 권능) carry an effect stamp after the damage (flags 0x40).
    [Fact]
    public void HitsThatMetAShieldAreParsed()
    {
        var withExtras = Assert.IsType<DamageMessage>(MessageParser.Parse(Convert.FromHexString("0438c0637600e10a03becb001603880002d338964f01000000edb201a71204ea01ea01ea01ea0101afdeea690100")));
        Assert.Equal((2343u, 1407L, 4), (withExtras.Total, withExtras.Main, withExtras.Extras.Count));   // 심장 찌르기 1,407 + 4 x 234
        Assert.Equal<uint>(0x4DC8 == 0 ? 0 : 0x4F96, withExtras.SkillKey);
        var single = Assert.IsType<DamageMessage>(MessageParser.Parse(Convert.FromHexString("0438c0635610e10a8021c7001f0388000155b8c84d02000000edb201d0150177e3d467020001")));
        Assert.Equal((2768u, 2u), (single.Total, single.HitIndex));
        Assert.Equal<uint>(0x4DC8, single.SkillKey);                                                      // 섬광 베기 2,768 [후방 강타 치명타]
        Assert.True(single.Mods.HasFlag(HitMods.Critical) && single.Mods.HasFlag(HitMods.Smash) && !single.Mods.HasFlag(HitMods.Front));
    }
}

public class ChainKeyTests
{
    [Theory]
    [InlineData(0x4D8C, 0x4D8F)] [InlineData(0x4DAA, 0x4DAD)] [InlineData(0x4DB9, 0x4DBD)]
    [InlineData(0x4D90, 0x4D8F)] [InlineData(0x4DAF, 0x4DAD)] [InlineData(0x4DBE, 0x4DBD)]
    public void FollowUpKeysCountAsTheirSkill(uint key, uint first)
    {
        Assert.Equal(first, Skills.Canonical(key));
        Assert.Equal(Skills.Name(first), Skills.Name(key));
    }

    [Theory]
    [InlineData(0x4F27, "나선 베기")] [InlineData(0x6D41, "마르쿠탄의 분노")] [InlineData(0x6155, "공간 지배")] [InlineData(0x5FF6, "소환: 불의 정령")]
    [InlineData(0x6024, "소환: 땅의 정령")] [InlineData(0x5FB9, "흡인")] [InlineData(0x612B, "원소 융합")] [InlineData(0x55B6, "지원 사격")] [InlineData(0x53FC, "표적 화살")]
    public void NamesFromTheOlderLogs(uint key, string name) => Assert.Equal(name, Skills.Name(key));
}

public class Key6591Tests
{
    private const uint Me = 8880, Dummy = 3000, Spirit = 3001;   // players: the ledger follows no monsters
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 21, 46, 0, Kst.Offset);

    private static DamageMessage Hit(uint target, uint key, uint total) => new(target, Me, 0x16, 1, total, [], key << 16);

    [Fact]
    public void KeyOf6591DependsOnWhetherACheonbeolHitCameJustBefore()
    {
        // 21:46 log: 심판의 번개 300,481 (6591, alone), 천벌 695,651 (65a0) + 663,572 (6591, 50 ms later), 심판의 번개 654,022 (6591, 400 ms later)
        var ledger = new DamageLedger();
        ledger.Add(Hit(Dummy, 0x6591, 300_481), Me, T0);
        ledger.Add(Hit(Dummy, 0x65A0, 695_651), Me, T0.AddMilliseconds(550));
        ledger.Add(Hit(Dummy, 0x6591, 663_572), Me, T0.AddMilliseconds(600));
        ledger.Add(Hit(Spirit, 0x6591, 585), Me, T0.AddMilliseconds(600));            // the same cast on a summon: no 65a0 on that target
        ledger.Add(Hit(Dummy, 0x6591, 654_022), Me, T0.AddMilliseconds(1000));
        var by = ledger.Take(Dummy, T0.AddSeconds(2))!.DealtBySkill!.ToDictionary(s => s.Key, s => s.Total);
        Assert.Equal(695_651 + 663_572, by[0x65A0]);
        Assert.Equal(300_481 + 654_022, by[0x6590]);
        Assert.Equal("천벌", Skills.Name(0x65A0));
        Assert.Equal("심판의 번개", Skills.Name(0x6590));
    }

    [Fact]
    public void NamesOfTheClericSkillsFromTheDummyLog()
    {
        Assert.Equal("단죄", Skills.Name(0x676D));
        Assert.Equal("고결한 기운", Skills.Name(0x67F3));
        Assert.Equal("대지의 축복", Skills.Name(0x67B6));
        Assert.Equal("방전", Skills.Name(0x6582));
        Assert.Equal("벽력 MAX", Skills.Name(0x65B0));
        Assert.Equal("벽력", Skills.Name(0x65AF));
    }
}

public class SummonTests
{
    // 21:46: my 신성한 기운 appears; its spawn message names me (아라) as the owner.
    private const string Spawn = "4136fe85035f0000000106ec9584eb9dbccb902c004002009d644700a552470040c847a066ae4309f801a68c03a68c03292c0000292c0000000000000000000000000000f0c6020064000000f04902000100000000000000a08601000000000078fd1100010101110181969800ffffffffffffffff8075d52abb030000fe85030102009d644700a552470040c847070206c0310000340800000000fb0306ed9598eb829801000100000000000000000000000000000003cd00cc160000d00054030000d600fa0000003200000000";
    private const uint Me = 12736, Summon = 49918, Enemy = 3000;
    private static readonly DateTimeOffset T0 = new(2026, 10, 12, 21, 46, 0, Kst.Offset);

    [Fact]
    public void TheSpawnMessageNamesTheOwner()
    {
        var s = Assert.IsType<SpawnMessage>(MessageParser.Parse(Convert.FromHexString(Spawn)));
        Assert.Equal((Summon, "아라"), (s.Entity, s.Owner));
    }

    // The elemental spirits do not name an owner: `5f 10 00 00`, a single 00, then their kind (fe 8e 2c 00 = 고대의 정령). A synthetic message of that layout.
    private const string SpiritSpawn = "4136bca1025f10000000fe8e2c004002000d6347009f53470050c8472ee4aa430cf301f5d503f5d503941c0000941c0000";

    [Fact]
    public void TheSpawnMessageSaysWhatKindItIs()
    {
        var summon = Assert.IsType<SpawnMessage>(MessageParser.Parse(Convert.FromHexString(Spawn)));
        Assert.Equal(0x2C90CBu, summon.Template);   // 신성한 기운
        var spirit = Assert.IsType<SpawnMessage>(MessageParser.Parse(Convert.FromHexString(SpiritSpawn)));
        Assert.Equal((37052u, "", 0x2C8EFEu), (spirit.Entity, spirit.Owner, spirit.Template));
        Assert.Equal(1, Skills.SpiritKind(spirit.Template));
        Assert.Equal(0, Skills.SpiritKind(summon.Template));
    }

    [Fact]
    public void SpiritsOfDifferentKindsKeepTheirSkillsApart()
    {
        const uint Fire = 20001, Wind = 20002, Ancient = 20003, Other = 20004;
        var ledger = new DamageLedger();
        foreach (var (entity, template) in new[] { (Fire, 0x2C8EB3u), (Wind, 0x2C8ED3u), (Ancient, 0x2C8EFEu), (Other, 0x2C90CBu) })
        {
            ledger.AddSummon(entity);
            ledger.NoteSpawn(entity, template);
        }
        foreach (var (source, key, total) in new[] { (Fire, 0x98u, 100L), (Wind, 0x98u, 200L), (Ancient, 0x98u, 400L), (Ancient, 0x5F5Fu, 800L), (Fire, 0x5F5Fu, 1600L), (Wind, 0x5F5Fu, 3200L), (Other, 0x98u, 6400L) })
            ledger.Add(new DamageMessage(Enemy, source, 0x06, 1, (uint)total, [], key << 16), Me, T0);
        var names = ledger.Take(Enemy, T0.AddSeconds(1))!.DealtBySkill!.ToDictionary(s => Skills.Name(s.Key), s => s.Total);
        Assert.Equal(100, names["불의 정령: 기본공격"]);
        Assert.Equal(200, names["바람의 정령: 기본 공격"]);
        Assert.Equal(400, names["고대의 정령: 기본 공격"]);
        Assert.Equal(800, names["고대의 정령: 파괴"]);
        Assert.Equal(1600, names["불의 정령: 화염 폭발"]);
        Assert.Equal(3200, names["바람의 정령: 회오리"]);
        Assert.Equal(6400, names["정령 기본 공격 (소환수)"]);   // a summon of another kind keeps the plain name
    }

    [Fact]
    public void TheSpiritsSpawnMessageNamesItsOwnerByEntityNumber()
    {
        // synthetic: the usual start, then `07 02 06 <owner u32> 34 08` further in (owner 11939)
        var spirit = Assert.IsType<SpawnMessage>(MessageParser.Parse(Convert.FromHexString(SpiritSpawn + "000000000000070206a32e000034080000000000")));
        Assert.Equal(11939u, spirit.OwnerEntity);
        Assert.Equal(0u, Assert.IsType<SpawnMessage>(MessageParser.Parse(Convert.FromHexString(SpiritSpawn))).OwnerEntity);
    }

    [Fact]
    public void ASpiritOfMineCountsAsMineEvenWithoutAnOwnerName()
    {
        using var store = new MeterStore(":memory:");
        var flow = new FlowKey("10.0.0.1", 13328, "192.168.0.2", 50001);
        var token = new StreamToken(flow, 0);
        var s = new MeterSession(store, ResetMode.OnTheHour, 3, T0);
        s.Handle(new MessageEvent(token, T0, new EnterWorldMessage(Me, "아라", 1019)));
        s.Handle(new MessageEvent(token, T0.AddSeconds(1), new SpawnMessage(20001, "", 0x2C8EB3, Me)));        // my fire spirit
        s.Handle(new MessageEvent(token, T0.AddSeconds(1), new SpawnMessage(20002, "", 0x2C8EB3, Me + 1)));    // someone else's
        s.Handle(new MessageEvent(token, T0.AddSeconds(2), new DamageMessage(Enemy, 20001, 0x06, 1, 5_000, [], 0x00980000)));
        s.Handle(new MessageEvent(token, T0.AddSeconds(3), new DamageMessage(Enemy, 20002, 0x06, 1, 999, [], 0x00980000)));
        var fight = s.Matcher.Damage.Take(Enemy, T0.AddSeconds(4))!;
        Assert.Equal(5_000, fight.Dealt);
        Assert.Equal("불의 정령: 기본공격", Skills.Name(Assert.Single(fight.DealtBySkill!).Key));
    }

    [Fact]
    public void WhatMySummonDoesCountsAsMine()
    {
        using var store = new MeterStore(":memory:");
        var flow = new FlowKey("10.0.0.1", 13328, "192.168.0.2", 50001);
        var token = new StreamToken(flow, 0);
        var s = new MeterSession(store, ResetMode.OnTheHour, 3, T0);
        s.Handle(new MessageEvent(token, T0, new EnterWorldMessage(Me, "아라", 1019)));
        s.Handle(new MessageEvent(token, T0.AddSeconds(1), MessageParser.Parse(Convert.FromHexString(Spawn))!));
        s.Handle(new MessageEvent(token, T0.AddSeconds(2), new DamageMessage(Enemy, Summon, 0x06, 1, 383_458, [], 0x66380000)));
        s.Handle(new MessageEvent(token, T0.AddSeconds(3), new DamageMessage(Enemy, 77777, 0x06, 1, 999, [], 0x66380000)));   // someone else's summon
        var fight = s.Matcher.Damage.Take(Enemy, T0.AddSeconds(4))!;
        Assert.Equal(383_458, fight.Dealt);
        Assert.Equal("신성한 기운 (소환수)", Skills.Name(Assert.Single(fight.DealtBySkill!).Key));
    }
}

public class DamageOverTimeTests
{
    private const uint Me = 8880, Enemy = 3000;
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 18, 0, 0, Kst.Offset);

    [Fact]
    public void ADamageTickIsParsed()
    {
        // 18:03 recording: 파멸의 목소리 (671d) on the training dummy 38750 from entity 2858, 6,635 per tick
        var p = Assert.IsType<PeriodicMessage>(MessageParser.Parse(Convert.FromHexString("0538deae020aaa16a92e2cc41d67eb3348fa0701")));
        Assert.Equal((38750u, 2858u, 0x671Du, 6635u, true), (p.Target, p.Source, p.SkillKey, p.Amount, p.Damage));
        // the heal over time (0B) is still a heal
        var heal = Assert.IsType<PeriodicMessage>(MessageParser.Parse(Convert.FromHexString("05388e8e010b8e8e0147" + "dd650000" + "01" + "a3" + "09" + "01" + "01")));
        Assert.False(heal.Damage);
    }

    [Fact]
    public void ATickCountsInTheTotalAndTheSkillButNotAsAHit()
    {
        var ledger = new DamageLedger();
        ledger.Add(new DamageMessage(Enemy, Me, 0x06, 1, 10_000, [], 0x671D0000), Me, T0);
        ledger.Add(new PeriodicMessage(Enemy, Me, 0x671D, 600, true), Me, T0.AddSeconds(1));
        ledger.Add(new PeriodicMessage(Enemy, Me, 0x671D, 500, true), Me, T0.AddSeconds(2));
        ledger.Add(new PeriodicMessage(77777, Me, 0x671D, 999, true), Me, T0.AddSeconds(2));       // on a monster: not tracked
        var fight = ledger.Take(Enemy, T0.AddSeconds(3))!;
        Assert.Equal((11_100L, 1), (fight.Dealt, fight.DealtHits));
        var skill = Assert.Single(fight.DealtBySkill!);
        Assert.Equal((1, 11_100L), (skill.Hits, skill.Total));
    }

    [Fact]
    public void ATickOfADebuffKeyJoinsTheHitsOfItsSkill()
    {
        var ledger = new DamageLedger();
        ledger.Add(new DamageMessage(Enemy, Me, 0x06, 1, 5_000, [], 0x60340000), Me, T0);      // 협공: 저주 hit key 6034
        ledger.Add(new PeriodicMessage(Enemy, Me, 0x6033, 100, true), Me, T0.AddSeconds(1));   // its tick key is one lower
        var skill = Assert.Single(ledger.Take(Enemy, T0.AddSeconds(2))!.DealtBySkill!);
        Assert.Equal((1, 5_100L, "협공: 저주"), (skill.Hits, skill.Total, Skills.Name(skill.Key)));
    }


    [Fact]
    public void AFixedExtraAmountIsReadOnlyInItsExactShape()
    {
        // 흡인 (5fb9): 390 in a message of its own, flags 0c, `64 00 00 00` after the amount (17:04:41.490)
        var extra = Assert.IsType<DamageMessage>(MessageParser.Parse(Convert.FromHexString("0438deae020c00a35d880ef500d8022cadb95f01000000b092018603640000000100")));
        Assert.Equal((0x0Cu, 0x5FB9u, 390u), ((uint)extra.Flags, extra.SkillKey, extra.Total));
        // 강습 낙인 (6980): the same shape with the second hit number
        Assert.Equal(0x6980u, Assert.IsType<DamageMessage>(MessageParser.Parse(Convert.FromHexString("0438deae020c00aa16c8140e0173023b1e806902000000904e8603640000000200"))).SkillKey);
        // anything else with flag 0c is not read
        Assert.Null(MessageParser.Parse(Convert.FromHexString("0438deae020c00a35d880ef500d8022cadb95f01000000b092018603650000000100")));
    }

    [Fact]
    public void TheFixedExtraAmountCountsInTheTotalAndTheSkillButNotAsAHit()
    {
        var ledger = new DamageLedger();
        ledger.Add(new DamageMessage(Enemy, Me, 0x26, 1, 122_073, [3130, 3130], 0x5FB90000), Me, T0);
        ledger.Add(new DamageMessage(Enemy, Me, 0x0C, 1, 390, [], 0x5FB90000), Me, T0);
        var fight = ledger.Take(Enemy, T0.AddSeconds(1))!;
        Assert.Equal((122_463L, 1), (fight.Dealt, fight.DealtHits));
        Assert.Equal(122_463, Assert.Single(fight.DealtBySkill!).Total);
        Assert.Equal(0, fight.Received);
    }

    [Fact]
    public void ASecondHitUnderTheSameKeyIsTheOtherSkill()
    {
        // 18:29 recording: 루미엘의 공간 then, 0.1 s later, 나락 - both under key 5b31; the next cast is far later
        var ledger = new DamageLedger();
        ledger.Add(new DamageMessage(Enemy, Me, 0x06, 1, 181_973, [], 0x5B310000), Me, T0);
        ledger.Add(new DamageMessage(Enemy, Me, 0x06, 1, 98_794, [], 0x5B310000), Me, T0.AddMilliseconds(100));
        ledger.Add(new DamageMessage(Enemy, Me, 0x06, 1, 175_047, [], 0x5B310000), Me, T0.AddSeconds(44));
        ledger.Add(new DamageMessage(Enemy, Me, 0x06, 1, 101_865, [], 0x5B310000), Me, T0.AddSeconds(44.2));
        var skills = ledger.Take(Enemy, T0.AddSeconds(45))!.DealtBySkill!.ToDictionary(s => Skills.Name(s.Key), s => (s.Hits, s.Total));
        Assert.Equal((2, 357_020L), skills["루미엘의 공간"]);
        Assert.Equal((2, 200_659L), skills["나락"]);
    }

    [Fact]
    public void TheEarthProcAndTheEarthPunishmentShareAKeyButNotTheMultiHitFlag()
    {
        // 02:38 recording: 67b6 hits with flags 06 are 대지의 축복, the ones with flags 36 (multi-hit) are 대지의 징벌
        var ledger = new DamageLedger();
        ledger.Add(new DamageMessage(Enemy, Me, 0x06, 1, 60_000, [], 0x67B661B3), Me, T0);
        ledger.Add(new DamageMessage(Enemy, Me, 0x36, 1, 676_936, [100, 100, 100, 100], 0x67B65E93), Me, T0.AddSeconds(1));
        var skills = ledger.Take(Enemy, T0.AddSeconds(2))!.DealtBySkill!.ToDictionary(s => Skills.Name(s.Key), s => (s.Hits, s.Total));
        Assert.Equal((1, 60_000L), skills["대지의 축복"]);
        Assert.Equal((1, 676_936L), skills["대지의 징벌"]);
    }

    [Fact]
    public void AHitISufferedIsNotCountedTwiceAsAFollowUp()
    {
        var ledger = new DamageLedger();
        ledger.Add(new DamageMessage(Me, Enemy, 0x06, 1, 5_000, [], 0x5B310000), Me, T0);   // one hit on me: the first, not a follow-up
        var skill = Assert.Single(ledger.Take(Enemy, T0.AddSeconds(1))!.ReceivedBySkill!);
        Assert.Equal("루미엘의 공간", Skills.Name(skill.Key));
    }
    [Fact]
    public void AnEnemyDebuffOnMeCountsAsReceived()
    {
        var ledger = new DamageLedger();
        ledger.Add(new DamageMessage(Me, Enemy, 0x06, 1, 4_000, [], 0x60430000), Me, T0);
        ledger.Add(new PeriodicMessage(Me, Enemy, 0x6042, 700, true), Me, T0.AddSeconds(1));
        var fight = ledger.Take(Enemy, T0.AddSeconds(2))!;
        Assert.Equal((4_700L, 1), (fight.Received, fight.ReceivedHits));
    }
}

public class ImmuneHitTests
{
    // 18:41-18:43 수호성 recording: the dummy hit me for 1 and the game log said, in this order, 철벽, 철벽, 전방, 후방, 후방, 무기 막기, 무기 막기, 완벽 방패 막기, (nothing), 방패 막기
    private static HitMods Mods(string hex) => Assert.IsType<DamageMessage>(MessageParser.Parse(Convert.FromHexString(hex))).Mods;

    [Fact]
    public void ImmuneIsToldApartFromABlock()
    {
        var immune = Mods("0438f12f0600fbad02b20813005c02110002f556be0001000000904e010100");        // F=11: [철벽]
        Assert.True(immune.HasFlag(HitMods.Immune));
        Assert.False(immune.HasFlag(HitMods.Block));
        var shield = Mods("0438f12f0600fbad02b20813006802010002f556be0001000000904e010100");        // F=01: [방패 막기]
        Assert.True(shield.HasFlag(HitMods.Block));
        Assert.False(shield.HasFlag(HitMods.Immune));
        var weapon = Mods("0438f12f0600fbad02b20813006402020002f556be0001000000904e010100");        // F=02: [무기 막기]
        Assert.True(weapon.HasFlag(HitMods.Block));
        Assert.False(Mods("0438f12f0600fbad02b20813006102000002f556be0001000000904e010100").HasFlag(HitMods.Block));   // F=00: [전방]
    }

    [Fact]
    public void TheDetailStatsCountImmuneApartAndKeepOldStrings()
    {
        var stats = HitStats.Empty.Add(new DamageMessage(1, 2, 0x06, 1, 1, [], 0, 1, HitMods.Immune)).Add(new DamageMessage(1, 2, 0x06, 1, 1, [], 0, 1, HitMods.Block));
        Assert.Equal((2, 1, 1), (stats.Hits, stats.Immune, stats.Block));
        Assert.Equal(stats, HitStats.Decode(stats.Encode()));
        // no 철벽: the string is the 12-field one as before
        Assert.Equal(12, HitStats.Empty.Add(new DamageMessage(1, 2, 0x06, 1, 1, [], 0, 1, HitMods.Block)).Encode().Split(',').Length);
        Assert.Equal(13, stats.Encode().Split(',').Length);
    }
}

public class BlockKindTests
{
    // The F byte of a hit the dummy made on a 수호성 (message of 18:41:27 with F replaced) and what the in-game log called it: two recordings, 31 hits, all agree.
    private const string Template = "0438f12f0600fbad02b20813005c02FF0002f556be0001000000904e010100";
    private static HitMods Mods(byte f, byte dir = 2) => Assert.IsType<DamageMessage>(MessageParser.Parse(Convert.FromHexString(Template.Replace("FF0002", f.ToString("x2") + "00" + dir.ToString("x2"))))).Mods;

    [Theory]
    [InlineData(0x01, "막기")] [InlineData(0x02, "막기")] [InlineData(0x21, "막기")] [InlineData(0x61, "막기")]   // 0x21 and 0x61: no tag in the log, but 비호의 방패 healed after them: blocks
    [InlineData(0x11, "철벽")] [InlineData(0x31, "철벽")] [InlineData(0x32, "철벽")]   // 0x32: [철벽] too (18:58:22), whatever the block kind
    [InlineData(0x41, "완벽 막기")] [InlineData(0x42, "완벽 막기")]   // 0x42 = [완벽 무기 막기]: seen once (18:58:42, 아라 recording; the only 0x42 hit of the recording)
    [InlineData(0x00, "")] [InlineData(0x20, "")]   // 0x20 (no block bit): a plain hit, no heal followed (18:57:52)
    public void TheKindOfBlock(byte f, string kind)
    {
        var mods = Mods(f);
        Assert.Equal(kind == "막기", mods.HasFlag(HitMods.Block));
        Assert.Equal(kind == "철벽", mods.HasFlag(HitMods.Immune));
        Assert.Equal(kind == "완벽 막기", mods.HasFlag(HitMods.PerfectBlock));
    }

    [Fact]
    public void StatsCountThemApartAndRoundTrip()
    {
        var stats = HitStats.Empty.Add(new DamageMessage(1, 2, 0x06, 1, 1, [], 0, 1, HitMods.PerfectBlock)).Add(new DamageMessage(1, 2, 0x06, 1, 1, [], 0, 1, HitMods.Immune));
        Assert.Equal((1, 1, 0), (stats.PerfectBlock, stats.Immune, stats.Block));
        Assert.Equal(stats, HitStats.Decode(stats.Encode()));
        Assert.Equal(14, stats.Encode().Split(',').Length);
        Assert.Equal(13, HitStats.Empty.Add(new DamageMessage(1, 2, 0x06, 1, 1, [], 0, 1, HitMods.Immune)).Encode().Split(',').Length);
    }
}

public class LifeRecoveryTests
{
    private const uint Me = 8880, Enemy = 3000;
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 18, 0, 0, Kst.Offset);

    [Fact]
    public void ManaAndActionPointRecoveriesAreNotLife()
    {
        // 18:41:37: 비호의 방패 767 life, 보호의 방패 260 정신력 and 26,000 (= 260) 행동력, all as recoveries to me
        var ledger = new DamageLedger();
        ledger.Add(new DamageMessage(Enemy, Me, 0x06, 1, 5_000, [], 0x66ff0000), Me, T0);
        ledger.Add(new DamageMessage(Me, Me, 0x04, 1, 767, [], 0x4BD12E6F), Me, T0.AddSeconds(1));
        ledger.Add(new DamageMessage(Me, Me, 0x04, 1, 260, [], 0x482E67EB), Me, T0.AddSeconds(1));
        ledger.Add(new DamageMessage(Me, Me, 0x04, 1, 26_000, [], 0x482E67EC), Me, T0.AddSeconds(1));
        var heals = ledger.Take(Enemy, T0.AddSeconds(2))!.HealBySkill!;
        var life = Assert.Single(heals);
        Assert.Equal(("비호의 방패", 767L), (Skills.Name(life.Key), life.Total));
        Assert.False(Skills.IsLifeRecovery(0x482E));
        Assert.True(Skills.IsLifeRecovery(0x699E));
    }

    [Fact]
    public void TheLifeHealsOfTheBlockSkillsHaveNames()
    {
        Assert.Equal("비호의 방패", Skills.Name(0x4BD1));
        Assert.Equal("고결의 갑주", Skills.Name(0x48E5));
        Assert.Equal("주신의 가호", Skills.Name(0x699E));
    }
}

public class DatabaseCopyTests
{
    [Fact]
    public void ADatabaseThatIsStillOpenIsCopiedWhole()
    {
        string dir = Path.Combine(Path.GetTempPath(), "a2rank-copy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string from = Path.Combine(dir, "old.db"), to = Path.Combine(dir, "new.db");
            var time = new DateTimeOffset(2026, 10, 13, 21, 0, 0, TimeSpan.FromHours(9));
            using (var store = new MeterStore(from))
            {
                store.AddKill("1019:아라", new KillRecord(KillKind.Kill, "예시가", 2004, 2, 640, time));
                Assert.True(MeterStore.CopyDatabase(from, to));   // the store is still open: its -wal file holds the newest rows
            }
            using var copy = new MeterStore(to);
            var kill = Assert.Single(copy.KillsSince("1019:아라", time.AddHours(-1)));
            Assert.Equal(("예시가", 640L), (kill.Name, kill.Delta));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(dir, true); }
    }

    [Fact]
    public void ABrokenFileGivesFalseAndLeavesNothing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "a2rank-copy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string from = Path.Combine(dir, "broken.db"), to = Path.Combine(dir, "new.db");
            File.WriteAllBytes(from, [.. Enumerable.Repeat((byte)0x41, 5000)]);
            Assert.False(MeterStore.CopyDatabase(from, to));
            Assert.False(File.Exists(to));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(dir, true); }
    }
}

public class ShieldTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AShieldTickIsNeitherDamageNorLife()
    {
        // 01:33 recording: 유스티엘의 권능 of a party member arrived as a 05 38 tick on me (and as a "heal" on myself)
        const uint Me = 8880, Mate = 8310, Enemy = 3000;
        var ledger = new DamageLedger();
        ledger.Add(new PeriodicMessage(Me, Mate, 0x67D4, 5_135, true), Me, T0);
        ledger.Add(new PeriodicMessage(Me, Me, 0x67D4, 19_070, false), Me, T0.AddSeconds(1));
        ledger.Add(new DamageMessage(Me, Enemy, 0x06, 1, 1_000, [], 0x1234_0000), Me, T0.AddSeconds(2));
        Assert.False(Skills.IsLifeRecovery(0x67D4));
        Assert.True(Skills.IsShield(0x67D4));
        var fight = ledger.Take(Mate, T0.AddSeconds(3));
        Assert.True(fight is null || fight.Received == 0);
        ledger.Add(new PeriodicMessage(Me, Mate, 0x65BF, 4_000, true), Me, T0.AddSeconds(4));   // a real damage tick of that player still counts
        Assert.Equal(4_000, ledger.Take(Mate, T0.AddSeconds(5))!.Received);
    }
}

public class HelpfulTickTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ATickOfAHealingOrShieldingSkillFlaggedAsDamageIsNotDamage()
    {
        const uint Me = 8880, Mate = 8310;
        var ledger = new DamageLedger();
        foreach (var key in new uint[] { 0x670E, 0x6FA3, 0x5588, 0x65DF, 0x67D4 })
            ledger.Add(new PeriodicMessage(Me, Mate, key, 4_000, true), Me, T0);
        ledger.Add(new PeriodicMessage(Me, Mate, 0x65BF, 4_000, true), Me, T0);   // 고통의 연쇄: a real damage-over-time tick still counts
        var fight = ledger.Take(Mate, T0.AddSeconds(1))!;
        Assert.Equal(4_000, fight.Received);
    }
}

public class PartyInviteTests
{
    // 2026-11-22 recording: a party of two; I invited 푸른바다 (id 7014): 08 92 at once, 0C 92 20 s later (declined) and no join
    private const string Invite = "0892FB032430313035413634462D414338432D344235452D383733412D453232344434363431303843661B00000000EE03E8610900000037000000000000000Ced91b8eba5b8ebb094eb8ba4CFB36603A2010000";
    private const string InviteEnded = "0C92661B00000000EE03FB030Ced91b8eba5b8ebb094eb8ba404";

    [Fact]
    public void AnInviteIsNotCountedUntilTheInvitedOneIsHeardFrom()
    {
        var tracker = new PartyTracker();
        tracker.Handle(new PartyFormedMessage(1, "나", "G", [(2, "친구")]), "나");
        var invite = Assert.IsType<PartyMemberMessage>(MessageParser.Parse(Convert.FromHexString(Invite)));
        Assert.True(invite.Pending);
        Assert.Equal((7014UL, "푸른바다"), (invite.CharacterId, invite.Name));
        tracker.Handle(invite, "나");
        Assert.Equal(2, tracker.Size);                       // not yet a member
        tracker.Observe(new PartyStatusMessage(7014), DateTimeOffset.UnixEpoch);
        Assert.Equal(3, tracker.Size);                       // accepted: their status messages come
        Assert.Contains("푸른바다", tracker.Names);
    }

    [Fact]
    public void ADeclinedInviteLeavesThePartyAsItWas()
    {
        var tracker = new PartyTracker();
        tracker.Handle(new PartyFormedMessage(1, "나", "G", [(2, "친구")]), "나");
        tracker.Handle((PartyMessage)MessageParser.Parse(Convert.FromHexString(Invite))!, "나");
        var ended = Assert.IsType<PartyLeftMessage>(MessageParser.Parse(Convert.FromHexString(InviteEnded)));
        Assert.Equal(7014UL, ended.CharacterId);
        tracker.Handle(ended, "나");
        Assert.Equal(2, tracker.Size);
    }
}
