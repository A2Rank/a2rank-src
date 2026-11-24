using Aion2Meter.Core;
using Aion2Meter.Protocol;
using Xunit;

namespace Aion2Meter.Tests;

/// <summary>회피: an attack on me that did not land (04 38 with flags 02 and no amount; docs/PROTOCOL.md).</summary>
public class DodgeTests
{
    private const uint Me = 13313, Enemy = 3068, Monster = 27094;
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 2, 19, 0, Kst.Offset);


    [Fact]
    public void A_dodge_is_read_from_the_two_shapes_seen()
    {
        Assert.Equal(new DodgeMessage(7737, 18137), Assert.IsType<DodgeMessage>(MessageParser.Parse(Convert.FromHexString(Real.Dodged30))) with { Stamp = 0 });
        Assert.Equal(new DodgeMessage(111, 32851), Assert.IsType<DodgeMessage>(MessageParser.Parse(Convert.FromHexString(Real.Dodged29))) with { Stamp = 0 });
        // the attack's skill is in it too (the high half of the stamp before the hit number, as in a hit)
        Assert.NotEqual(0u, Assert.IsType<DodgeMessage>(MessageParser.Parse(Convert.FromHexString(Real.Dodged30))).SkillKey);
    }


    [Fact]
    public void A_dodge_of_my_own_attack_is_read_the_same_way()
    {
        Assert.Equal(new DodgeMessage(2442, 6323), Assert.IsType<DodgeMessage>(MessageParser.Parse(Convert.FromHexString(Real.DodgedByTarget))) with { Stamp = 0 });
    }
    [Fact]
    public void A_landed_hit_and_a_recovery_are_not_dodges()
    {
        var hit = Assert.IsType<DamageMessage>(MessageParser.Parse(Convert.FromHexString(Real.Landed)));
        Assert.Equal(1u, hit.Total);
        Assert.IsType<DamageMessage>(MessageParser.Parse(Convert.FromHexString(Real.Recovery)));
        Assert.Null(MessageParser.Parse(Convert.FromHexString(Real.Dodged30)[..^2]));   // cut: not a message we know
    }

    [Fact]
    public void Dodges_of_a_players_attacks_count_with_the_hits_that_landed()
    {
        var ledger = new DamageLedger();
        ledger.Add(new DamageMessage(Me, Enemy, 0x16, 20993, 300, [], 0x6563_0000), Me, T0);
        ledger.Add(new DodgeMessage(Me, Enemy), Me, T0.AddSeconds(1));
        ledger.Add(new DodgeMessage(Me, Enemy), Me, T0.AddSeconds(2));
        ledger.Add(new DodgeMessage(Me, Monster), Me, T0.AddSeconds(2));       // a mob: not part of the details
        ledger.Add(new DodgeMessage(Monster, Me), Me, T0.AddSeconds(2));       // a mob dodging me: neither
        ledger.Add(new DodgeMessage(Enemy, Me), Me, T0.AddSeconds(2));         // my attack dodged by them
        ledger.Add(new DodgeMessage(5555, Enemy), Me, T0.AddSeconds(2));       // someone else's
        var stats = ledger.Take(Enemy, T0.AddSeconds(3))!;
        Assert.Equal((1, 2), (stats.ReceivedHow!.Hits, stats.ReceivedHow.Dodge));
        Assert.Equal(1, stats.DealtHow!.Dodge);
        Assert.Null(ledger.Take(Monster, T0.AddSeconds(3)));
    }

    [Fact]
    public void A_dodge_counts_under_the_skill_of_the_attack_that_was_dodged()
    {
        var ledger = new DamageLedger();
        ledger.Add(new DamageMessage(Me, Enemy, 0x16, 20993, 300, [], 0x6563_0000), Me, T0);
        ledger.Add(new DodgeMessage(Me, Enemy, 0x6563_0001), Me, T0.AddSeconds(1));   // the same skill: 6563
        ledger.Add(new DodgeMessage(Me, Enemy, 0x5390_0001), Me, T0.AddSeconds(2));   // another one, only dodged
        var stats = ledger.Take(Enemy, T0.AddSeconds(3))!;
        var same = stats.ReceivedBySkill!.Single(s => s.Key == 0x6563);
        Assert.Equal((1, 1), (same.How!.Hits, same.How.Dodge));
        var only = stats.ReceivedBySkill!.Single(s => s.Key == 0x5390);
        Assert.Equal((0, 1, 0L), (only.How!.Hits, only.How.Dodge, only.Total));
    }

    [Fact]
    public void Only_dodges_still_make_a_fight()
    {
        var ledger = new DamageLedger();
        ledger.Add(new DodgeMessage(Me, Enemy), Me, T0);
        var stats = ledger.Take(Enemy, T0.AddSeconds(1))!;
        Assert.Equal((0, 1, false), (stats.ReceivedHow!.Hits, stats.ReceivedHow.Dodge, stats.ReceivedHow.IsEmpty));
    }

    [Fact]
    public void The_dodge_count_is_stored_as_a_fifteenth_field_and_old_strings_still_read()
    {
        var how = new HitStats(10, 5, 1, 2, 3, 4, 1, 0, 0, 1, 8, 500, 1, 2, 3);
        Assert.Equal(15, how.Encode().Split(',').Length);
        Assert.Equal(how, HitStats.Decode(how.Encode()));
        var old = new HitStats(10, 5, 1, 2, 3, 4, 1, 0, 0, 1, 8, 500, 1, 2);
        Assert.Equal(14, old.Encode().Split(',').Length);
        Assert.Equal(0, HitStats.Decode(old.Encode()).Dodge);
        var onlyDodges = HitStats.Empty.AddDodge().AddDodge();
        Assert.Equal(onlyDodges, HitStats.Decode(onlyDodges.Encode()));
        Assert.Equal(7, HitStats.Merge(how with { Dodge = 3 }, how with { Dodge = 4 }).Dodge);
    }
}
