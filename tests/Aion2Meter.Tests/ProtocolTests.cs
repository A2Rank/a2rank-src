using Aion2Meter.Protocol;

namespace Aion2Meter.Tests;

public class ProtocolTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeSeconds(1_791_630_000);

    [Theory]
    [InlineData(new byte[] { 0x05 }, 5u, 1)]
    [InlineData(new byte[] { 0xAC, 0x02 }, 300u, 2)]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F }, uint.MaxValue, 5)]
    public void Varint_reads_value_and_length(byte[] data, uint value, int length)
    {
        Assert.True(Varint.TryRead(data, out uint v, out int n));
        Assert.Equal(value, v);
        Assert.Equal(length, n);
    }

    [Fact]
    public void Varint_needs_more_bytes_or_rejects_overlong()
    {
        Assert.False(Varint.TryRead(new byte[] { 0x80 }, out _, out _));
        Assert.Throws<ProtocolException>(() => Varint.TryRead(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 }, out _, out _));
    }

    [Fact]
    public void Lz4_decodes_literals_and_overlapping_match()
    {
        // "abcd" + match(offset 4, length 8) + final literal "x" -> "abcdabcdabcdx"
        byte[] block = [0x44, (byte)'a', (byte)'b', (byte)'c', (byte)'d', 0x04, 0x00, 0x10, (byte)'x'];
        Assert.Equal("abcdabcdabcdx"u8.ToArray(), Lz4Block.Decode(block, 13));
        Assert.Throws<ProtocolException>(() => Lz4Block.Decode(block, 12));                       // size mismatch
        Assert.Throws<ProtocolException>(() => Lz4Block.Decode([0x40, 0x61, 0x62, 0x63, 0x64, 0x09, 0x00], 12)); // offset beyond output
    }

    [Fact]
    public void Lz4_long_literal_run()
    {
        var data = Enumerable.Range(0, 600).Select(i => (byte)i).ToArray();
        Assert.Equal(data, Lz4Block.Decode(Bytes.Lz4Literals(data), 600));
    }

    [Fact]
    public void Frame_header_and_compressed_bundle()
    {
        byte[] point = Bytes.Point(40_075_861), death = Bytes.Death(15415, 6374, 1019, "사랑", 1, true);
        Assert.True(Frames.TryReadHeader(Bytes.Frame(point), out int size, out int header));
        Assert.Equal((1 + point.Length, 1), (size, header));
        var inner = Frames.UnpackPayload(Bytes.Bundle(death, point));
        Assert.Equal(2, inner.Count);
        Assert.Equal(death, inner[0]);
        Assert.Equal(point, inner[1]);
    }

    [Fact]
    public void Parses_point_death_identity()
    {
        Assert.Equal(new PointMessage(40_075_861), MessageParser.Parse(Bytes.Point(40_075_861)));
        Assert.Null(MessageParser.Parse([0x73, 0xE2, 0x00, 1, 2])); // wrong length

        var death = Assert.IsType<DeathMessage>(MessageParser.Parse(Bytes.Death(15415, 6374, 1019, "사랑", 1, true)));
        Assert.Equal((15415u, 6374u, (ushort)1019, "사랑", 1, true), (death.Victim, death.Killer, death.KillerServer, death.KillerName, death.KillerRace, death.Pvp));
        Assert.False(Assert.IsType<DeathMessage>(MessageParser.Parse(Bytes.Death(1, 2, 2004, "몹", 2, false))).Pvp);
        Assert.Null(MessageParser.Parse(Bytes.Death(1, 2, 999, "x", 1, true))); // invalid server code

        Assert.Equal(new EnterWorldMessage(13313, "아라", 1019), MessageParser.Parse(Bytes.EnterWorld(13313, "아라", 1019)));
        Assert.Null(MessageParser.Parse(Bytes.EnterWorld(13313, "아라", 999)));

        var id = Assert.IsType<IdentityMessage>(MessageParser.Parse(Bytes.Identity(3068, "료이치", 55, 2, 2004)));
        Assert.Equal((3068u, "료이치", 55, 2, (ushort)2004), (id.Entity, id.Name, id.Level, id.Race, id.Server));
        var noServer = Assert.IsType<IdentityMessage>(MessageParser.Parse(Bytes.Identity(15416, "일장", 55, 2, 1019))); // race mismatch
        Assert.Equal((ushort)0, noServer.Server);

        var other = Assert.IsType<IdentityMessage>(MessageParser.Parse(Bytes.IdentityWithoutRepeat(167, "이쮸링", 6, 1, 1019)));
        Assert.Equal((167u, "이쮸링", (ushort)1019), (other.Entity, other.Name, other.Server));
        var otherMismatch = Assert.IsType<IdentityMessage>(MessageParser.Parse(Bytes.IdentityWithoutRepeat(167, "이쮸링", 6, 2, 1019)));
        Assert.Equal((ushort)0, otherMismatch.Server);
    }

    [Fact]
    public void Parses_ranking_personal_and_list_with_deadline()
    {
        var deadline = DateTimeOffset.Parse("2026-10-11T23:13:54+09:00");
        ulong ms = (ulong)deadline.ToUnixTimeMilliseconds();
        byte[] personal = Bytes.PersonalRanking(Bytes.RankingEntry(12, 1019, 777, "아라", 40_060_461), 500, ms);
        var p = Assert.IsType<RankingMessage>(MessageParser.Parse(personal));
        Assert.False(p.IsList);
        Assert.Equal(0, p.Filter);
        Assert.Equal(deadline, p.Deadline);
        Assert.Equal(("아라", (ushort)1019, 777UL, 40_060_461UL), (p.Entries[0].Name, p.Entries[0].Server, p.Entries[0].CharacterId, p.Entries[0].Score));

        byte[] list = Bytes.ListRanking([Bytes.RankingEntry(1, 2004, 1, "가", 100), Bytes.RankingEntry(2, 2004, 2, "나", 90)], 500, ms, filter: 8);
        var l = Assert.IsType<RankingMessage>(MessageParser.Parse(list));
        Assert.True(l.IsList);
        Assert.Equal((8, 2), (l.Filter, l.Entries.Count));
        Assert.Null(MessageParser.Parse([.. list, 0])); // trailing byte -> layout not understood
    }

    [Fact]
    public void Stream_decoder_locks_after_three_frames_when_joining_mid_stream()
    {
        byte[] frames = [.. Bytes.Frame(Bytes.Point(1)), .. Bytes.Frame(Bytes.Point(2)), .. Bytes.Frame(Bytes.Point(3))];
        byte[] stream = [0x13, 0x37, 0x99, .. frames]; // starts inside an earlier frame
        var decoder = new StreamDecoder();
        var first = decoder.Feed(stream.AsSpan(0, stream.Length - 2));
        Assert.Empty(first); // only two complete frames: not locked yet
        var rest = decoder.Feed(stream.AsSpan(stream.Length - 2));
        Assert.True(decoder.Locked);
        Assert.Equal([1UL, 2UL, 3UL], rest.Select(m => ((PointMessage)MessageParser.Parse(m)!).Total));
    }

    [Fact]
    public void Pipeline_emits_deaths_before_points_from_the_same_segment()
    {
        var pipeline = new Pipeline();
        byte[] sync = [.. Bytes.Frame(Bytes.Point(100)), .. Bytes.Frame(Bytes.Point(100)), .. Bytes.Frame(Bytes.Point(100))];
        pipeline.Feed(Bytes.Ethernet(1000, sync), 1, T0);
        // Point first, death second inside one bundle: the matcher must still see the death first.
        byte[] bundle = Bytes.Frame(Bytes.Bundle(Bytes.Point(640), Bytes.Death(15415, 6374, 1019, "사랑", 1, true)));
        var events = pipeline.Feed(Bytes.Ethernet(1000 + (uint)sync.Length, bundle), 1, T0.AddSeconds(1));
        var messages = events.OfType<MessageEvent>().Select(e => e.Message).ToList();
        Assert.IsType<DeathMessage>(messages[0]);
        Assert.Equal(new PointMessage(640), messages[1]);
    }

    [Fact]
    public void Tcp_reorders_and_reports_gaps()
    {
        var pipeline = new Pipeline();
        byte[] a = [.. Bytes.Frame(Bytes.Point(1)), .. Bytes.Frame(Bytes.Point(2)), .. Bytes.Frame(Bytes.Point(3))];
        byte[] b = Bytes.Frame(Bytes.Point(4)), c = Bytes.Frame(Bytes.Point(5));
        uint s = 5000;
        pipeline.Feed(Bytes.Ethernet(s, a), 1, T0);
        Assert.Empty(pipeline.Feed(Bytes.Ethernet(s + (uint)(a.Length + b.Length), c), 1, T0.AddSeconds(0.1))); // out of order: wait
        var late = pipeline.Feed(Bytes.Ethernet(s + (uint)a.Length, b), 1, T0.AddSeconds(0.2));
        Assert.Equal([4UL, 5UL], late.OfType<MessageEvent>().Select(e => ((PointMessage)e.Message).Total));

        // A hole that never fills for 2 s becomes a gap with a new stream token.
        var before = late[0].Token;
        uint next = s + (uint)(a.Length + b.Length + c.Length);
        pipeline.Feed(Bytes.Ethernet(next + 50, a), 1, T0.AddSeconds(1));
        var afterGap = pipeline.Feed(Bytes.Ethernet(next + 50 + (uint)a.Length, b), 1, T0.AddSeconds(3.5));
        Assert.Contains(afterGap, e => e is GapEvent);
        Assert.NotEqual(before, afterGap[0].Token);
        Assert.Contains(pipeline.Feed(Bytes.Ethernet(next + 999, [], 0x11), 1, T0.AddSeconds(4)), e => e is ClosedEvent);
    }

    // Real 04 38 messages from a training-dummy fight (entity ids only). Target 38750 = the dummy, source 3218 = me;
    // the totals and extra damages are the numbers the game's combat log printed for the same hits (docs/PROTOCOL.md §3.6).
    [Theory]
    [InlineData("0438deae02160092194a8e03017a03840002d346636501000000d28c01dde9010100", 0x16, 29917, 29917, new uint[0])]
    [InlineData("0438deae0226009219d1890e017f030c0002afd5ad6901000000d28c01e1d20304fb0bfb0bfb0bfb0b0100", 0x26, 59745, 53621, new uint[] { 1531, 1531, 1531, 1531 })]
    [InlineData("04 38deae023600921982a004017b03880002d3b2ce6501000000d28c0181ce0802a01ca01c0100", 0x36, 141057, 133825, new uint[] { 3616, 3616 })]
    [InlineData("0438deae0236009219c4be080183038800029b846a6701000000d28c01cab91003fe35fe35fe350100", 0x36, 269514, 248784, new uint[] { 6910, 6910, 6910 })]
    public void Damage_message_total_is_the_main_hit_plus_the_extra_damages_the_combat_log_lists_separately(
        string hex, int flags, int total, int main, uint[] extras)
    {
        var d = Assert.IsType<DamageMessage>(MessageParser.Parse(Convert.FromHexString(hex.Replace(" ", ""))));
        Assert.Equal((38750u, 3218u, (byte)flags, 18002u, (uint)total, (long)main), (d.Target, d.Source, d.Flags, d.Skill, d.Total, d.Main));
        Assert.Equal(extras, d.Extras);
    }

    // The skill key (high 16 bits of the 4 bytes before the u32 1) against the skill named on the game's combat-log line for the same hit.
    [Theory]
    [InlineData("0438deae02160092194a8e03017a03840002d346636501000000d28c01dde9010100", "대지의 응보")]                   // 29,917
    [InlineData("0438deae0226009219d1890e017f030c0002afd5ad6901000000d28c01e1d20304fb0bfb0bfb0bfb0b0100", "주신의 은총")]   // 53,621 + 4 x 1,531
    [InlineData("0438deae023600921982a004017b03880002d3b2ce6501000000d28c0181ce0802a01ca01c0100", "약화의 낙인")]            // 133,825 + 2 x 3,616
    [InlineData("0438deae0236009219c4be080183038800029b846a6701000000d28c01cab91003fe35fe35fe350100", "단죄")]               // 248,784 + 3 x 6,910
    // damage I took from another player (목우 and POK), the same skill names as in the log
    [InlineData("0438fd090600a64cd1890e01c103000001afd5ad69010000009a9e01dd080100", "주신의 은총")]                          // 1,117 from 목우
    [InlineData("0438fd093600d01ba49cd70017038400024bab385401000000fbaf01fd920104d90ed90ed90ed90e0100", "올가미 화살")]       // 11,289 + 4 x 1,881 from POK
    public void The_skill_of_a_damage_message_is_told_by_its_key(string hex, string skill)
    {
        var d = Assert.IsType<DamageMessage>(MessageParser.Parse(Convert.FromHexString(hex)));
        Assert.Equal(skill, Skills.Name(d.SkillKey));
    }

    [Fact]
    public void An_unknown_skill_shows_its_key()
    {
        Assert.Equal("스킬 6ff1", Skills.Name(0x6FF1));
        Assert.False(Skills.IsKnown(0x6FF1));
    }

    [Fact]
    public void A_hit_that_met_a_shield_is_damage_with_an_effect_stamp_not_extra_damages()
    {
        // flags 0x46 in the 54-minute recording: after the total comes "01" and a 4-byte value (221,949,743 = stamp 0x69eadeaf of an effect), not an extra damage.
        // It was dropped before; the same stamp ends the 1,407 hit of the 21:29 duel (whose log amount matched).
        var d = Assert.IsType<DamageMessage>(MessageParser.Parse(Convert.FromHexString("0438c4304600996af9c3e900d8032cf10c024f8d505b01000000fdaf01d84c01afdeea690100")));
        Assert.Equal(9816u, d.Total);
        Assert.Empty(d.Extras);
    }

    [Fact]
    public void Short_04_38_messages_without_an_amount_are_not_damage()
    {
        Assert.Null(MessageParser.Parse(Convert.FromHexString("0438b2080000b20837b92100df0287592c0d01000000904e0100")));
        Assert.Null(MessageParser.Parse(Convert.FromHexString("0438b208")));               // truncated
    }

    [Fact]
    public void Ignores_other_ports_and_non_tcp()
    {
        Assert.Null(PacketParser.Parse(Bytes.Ethernet(1, [1, 2, 3], sport: 443), 1, T0));
        Assert.Null(PacketParser.Parse(new byte[10], 1, T0));
    }
}
