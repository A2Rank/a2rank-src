using Aion2Meter.Core;
using Aion2Meter.Protocol;

namespace Aion2Meter.Tests;

/// <summary>The target marker (35 38) and the class an identity message (45 36) carries in the numbers of its class skills (2026-11-22 recordings).</summary>
public class TargetTests
{
    // The first 640 bytes of a real identity message of a 치유성 (name changed to ABCDE): the class skills 17800001, 17720001 ... come after the name.
    private const string ClericIdentity = "4536CD6A0300A601170541424344451E00000001020100D2D7A662C77A0487C400DA9E4600000000000001BEF10395D503EA270000EA2700000000000000000000289A010060DE020026000000F049020001000000A0860100A08601003E5F18003E5F180001404DCB01A872D4012C42B700FB038E0000000000FB0306eb8bacebb99b0100020000000000000000000000000000000D1110D3CBC901FFFFFFFFFFFFFFFF8075D52ABB030000CD6A01009D4EECC7F4E3D6C7006AC34610171590600AFFFFFFFFFFFFFFFF8075D52ABB03000019FFFF7F7FFFFF7F7FFFFF7F7F1019844E0000D52F8104020000003206D407A401000009FFFF7F7FFFFF7F7FFFFF7F7F113581109C0AFFFFFFFFFFFFFFFF8075D52ABB030000CD6A109D4EECC7F4E3D6C7006AC346113681DB8F0AFFFFFFFFFFFFFFFF8075D52ABB030000CD6A099D4EECC7F4E3D6C7006AC3461137E1548E0AFFFFFFFFFFFFFFFF8075D52ABB030000CD6A0F9D4EECC7F4E3D6C7006AC34611382162910AFFFFFFFFFFFFFFFF8075D52ABB030000CD6A169D4EECC7F4E3D6C7006AC3461139C1E8920AFFFFFFFFFFFFFFFF8075D52ABB030000CD6A119D4EECC7F4E3D6C7006AC346113A616F940AFFFFFFFFFFFFFFFF8075D52ABB030000CD6A13C09D4EECC7F4E3D6C7006AC346113BE1899A0AFFFFFFFFFFFFFFFF8075D52ABB030000CD6A109D4EECC7F4E3D6C7006AC346113C01F6950AFFFFFFFFFFFFFFFF8075D52ABB030000CD6A059D4EECC7F4E3D6C7006AC346113DA17C970AFFFFFFFFFFFFFFFF8075D52ABB030000CD6A109D4EECC7F4E3D6C7006AC346113E4103990AFFFFFFFFFFFFFFFF8075D52ABB030000CD6A139D4EECC7F4E3D6C7006AC3461029749906190100000000";

    [Fact]
    public void AnIdentityGivesItsNumberAndTheClassOfItsSkills()
    {
        var m = Assert.IsType<IdentityMessage>(MessageParser.Parse(Convert.FromHexString(ClericIdentity)));
        Assert.Equal(("ABCDE", 30, 1), (m.Name, m.Level, m.Race));
        Assert.Equal((ushort)27341, m.RawId);     // the two bytes after the opcode: what a target message names
        Assert.Equal(17, m.ClassCode);
        Assert.Equal("치유성", PlayerClass.Name(m.ClassCode));
    }

    [Fact]
    public void TheTargetMessagesOfARealClick()
    {
        // 2026-11-23 01:21:33 (both directions recorded): my click, then 34 38 + entity at once and 35 38 + u32 31 ms later
        var select = Assert.IsType<TargetMessage>(MessageParser.Parse(Convert.FromHexString("3438000000AB2D")));
        Assert.Equal((11691u, true), (select.RawId, select.Select));
        var echo = Assert.IsType<TargetMessage>(MessageParser.Parse(Convert.FromHexString("3538AB2D0000")));
        Assert.Equal((11691u, false), (echo.RawId, echo.Select));
        Assert.Equal((113866u, true), Assert.IsType<TargetMessage>(MessageParser.Parse(Convert.FromHexString("3438000000CABC01"))) is var mob ? (mob.RawId, mob.Select) : default);   // a monster: a 3-byte number
        Assert.Equal(0u, Assert.IsType<TargetMessage>(MessageParser.Parse(Convert.FromHexString("343800000000"))).RawId);   // cleared
        Assert.Null(MessageParser.Parse(Convert.FromHexString("3538A21500E167")));        // the longer 35 38 forms are not used
        Assert.Null(MessageParser.Parse(Convert.FromHexString("353800000000")));          // 0 in a 35 38 is not a target
    }

    private static IdentityMessage Cleric() => Assert.IsType<IdentityMessage>(MessageParser.Parse(Convert.FromHexString(ClericIdentity)));

    private static readonly DateTimeOffset T0 = new(2026, 11, 23, 1, 21, 0, TimeSpan.Zero);
    private static TargetedPlayer? Obs(TargetWatcher w, GameMessage m, double seconds) => w.Observe(m, T0.AddSeconds(seconds));
    private static TargetMessage Select(uint id) => new(id, true);
    private static TargetMessage Echo(uint id) => new(id);

    [Fact]
    public void AClickRingsOnceAndItsEchoAndTheRepeatsDoNot()
    {
        var w = new TargetWatcher();
        Obs(w, Cleric(), 0);
        Assert.Equal((17, "ABCDE"), Seen(Obs(w, Select(27341), 1)));      // 34 38 at the click
        Assert.Null(Obs(w, Echo(27341), 1.05));                           // 35 38 50 ms later
        Assert.Null(Obs(w, Echo(27341), 2.3));                            // repeated while I fight it
        Assert.Equal((17, "ABCDE"), Seen(Obs(w, Select(27341), 5)));      // clicked again: rings again
        Assert.Equal((0, "", 999u), Seen3(Obs(w, Select(999), 6)));       // someone never seen
    }

    [Fact]
    public void TheGameAimingForMeRingsOnceForANewEntity()
    {
        var w = new TargetWatcher();
        Obs(w, Cleric(), 0);
        Assert.Equal((17, "ABCDE"), Seen(Obs(w, Echo(27341), 1)));        // 35 38 alone: auto aim
        Assert.Null(Obs(w, Echo(27341), 1.6));
        Assert.Null(Obs(w, new TargetMessage(0), 3));                     // cleared
        Assert.Null(Obs(w, Echo(27341), 3));                              // the echo that comes with the clear (01:21:39.772)
        Assert.Equal((17, "ABCDE"), Seen(Obs(w, Echo(27341), 6)));        // a second later: aimed again
    }

    [Fact]
    public void AnIdentityThatComesAfterTheTargetStillRings()
    {
        var w = new TargetWatcher();
        Assert.Equal((0, ""), Seen(Obs(w, Select(27341), 1)));            // aimed first: unknown for now
        Assert.Equal((17, "ABCDE"), Seen(Obs(w, Cleric(), 1.2)));         // the identity arrives: ring then, once
        Assert.Null(Obs(w, Cleric(), 2));
    }

    [Fact]
    public void ThePlayersClassIsLearnedFromWhatTheyCastWhenTheirIdentityWasNeverSeen()
    {
        // 주교 = entity 7320 in a target message and in the identity (bytes 98 1C); a hit names the same two bytes as the varint 3608
        Assert.Equal((ushort)7320, PlayerClass.RawIdOf(3608));
        Assert.Equal(17, PlayerClass.FromSkillKey(0x65A0));      // 천벌: a 치유성 skill
        Assert.Equal(0, PlayerClass.FromSkillKey(0x11E1));       // a stigma stone: not a class
        var w = new TargetWatcher();
        Assert.Null(Obs(w, new DamageMessage(40, 3608, 0x06, 1, 5_000, [], 0x65A00000), 0));
        Assert.Equal((17, "", 7320u), Seen3(Obs(w, Select(7320), 1)));          // never seen coming into view: known by the skill
    }

    private static (int, string) Seen(TargetedPlayer? p) => (p!.ClassCode, p.Name);
    private static (int, string, uint) Seen3(TargetedPlayer? p) => (p!.ClassCode, p.Name, p.Id);
}
