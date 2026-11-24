using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Aion2Meter.Protocol;

/// <summary>A decoded game message the meter uses. See docs/PROTOCOL.md §3.</summary>
public abstract record GameMessage;

/// <summary>73 E2: my total rank points.</summary>
public sealed record PointMessage(ulong Total) : GameMessage;

/// <summary>04 8D: a death seen nearby. Name/guild/race/server are the killer's.</summary>
public sealed record DeathMessage(uint Victim, uint Killer, ushort KillerServer, string KillerName, int KillerRace, bool Pvp) : GameMessage;

/// <summary>
/// 04 38 with a damage amount: <paramref name="Target"/> took <paramref name="Total"/> damage from <paramref name="Source"/>.
/// <paramref name="Total"/> is the main hit plus the extra damages listed after it (see <see cref="Main"/>).
/// Layout and the checks against the in-game combat log: docs/PROTOCOL.md §3.6.
/// <paramref name="Flags"/> is the first of the two flag bytes, kept raw: 0x04 = recovery (target = source) in the samples, 0x06/0x16/0x26/0x36 = damage; the rest of its meaning is not worked out yet.
/// </summary>
public sealed record DamageMessage(uint Target, uint Source, byte Flags, uint Skill, uint Total, IReadOnlyList<uint> Extras, uint Stamp = 0, uint HitIndex = 1, HitMods Mods = HitMods.None, uint Healed = 0, uint Regained = 0) : GameMessage
{
    /// <summary>
    /// The skill that did it, as the high 16 bits of <see cref="Stamp"/> (the 4 bytes before the u32 1): the same for the same skill on every
    /// character of a class and different between skills in all samples; names: <c>Skills.Name</c>. The low bits vary a little (levels/variants).
    /// </summary>
    public uint SkillKey => Stamp >> 16;

    /// <summary>The main hit as the combat log shows it (the log lists the extra damages separately).</summary>
    public long Main => (long)Total - Extras.Sum(e => (long)e);
}

/// <summary>How a damage hit landed (04 38, docs/PROTOCOL.md §3.6.1; checked against the in-game statistics of a 35-hit fight: every count equal).</summary>
[Flags]
public enum HitMods
{
    None = 0,
    Critical = 1,
    /// <summary>강타.</summary>
    Smash = 2,
    /// <summary>막기 (shield or weapon).</summary>
    Block = 4,
    /// <summary>The hit came from the front (전방); without it: from behind (후방).</summary>
    Front = 8,
    /// <summary>다단히트: the hit carries extra damages.</summary>
    Multi = 16,
    /// <summary>재생: the hit restored life (to the attacker).</summary>
    Regen = 32,
    /// <summary>철벽: <c>F & 0x10</c> together with a block bit (the in-game log says "[철벽]" where a plain block would say "[방패 막기]"; counted apart from 막기).</summary>
    Immune = 128,
    /// <summary>완벽 막기: <c>F & 0x40</c> together with a block bit and without 0x20/0x10 ("[완벽 방패 막기]"; with 0x20 the log shows no tag at all); counted apart from 막기.</summary>
    PerfectBlock = 256,
    /// <summary>The direction byte was 1 or 2 (otherwise unknown).</summary>
    HasDirection = 64,
    /// <summary>완벽: <c>F & 0x04</c>. With smash too (<c>8c</c>) the game log shows the smash tag only, but another meter reports 완벽 and 강타 both above 50% for the same hits, so they overlap. Every 완벽 tag seen had it; the one statistics table checked had none.</summary>
    Perfect = 128,
}

/// <summary>45 36: entity number ↔ character. Server is 0 when not found.</summary>
/// <summary>04 38 with the damage flag 0x02 but not 0x04 and no amount: an attack on <paramref name="Target"/> by <paramref name="Source"/> that did not land (회피, "…의 공격을 회피했습니다").</summary>
public sealed record DodgeMessage(uint Target, uint Source, uint Stamp = 0) : GameMessage
{
    /// <summary>The skill of the attack that was dodged: the high 16 bits of the stamp, as in a hit (<see cref="DamageMessage.SkillKey"/>).</summary>
    public uint SkillKey => Stamp >> 16;
}

/// <summary>
/// 45 36: who an entity is. <paramref name="RawId"/> is the two bytes after the opcode as a plain u16: the number a target message (35 38) uses (the varint <paramref name="Entity"/> is what hits use).
/// <paramref name="ClassCode"/>: the message lists the character's class skills as numbers 11790000 + 1 ...; their first two digits are the class (11 검성, 12 수호성, 13 살성, 14 궁성, 15 마도성, 16 정령성, 17 치유성, 18 호법성, 19 권성), 0 when none is found
/// (267 of 267 players whose class was known from their skills came out right, 2026-11-22).
/// </summary>
public sealed record IdentityMessage(uint Entity, string Name, int Level, int Race, ushort Server, ushort RawId = 0, int ClassCode = 0) : GameMessage;

/// <summary>
/// My target (docs/PROTOCOL.md): 34 38 with three zero bytes and the entity number (u16 for a player, 3-4 bytes for a monster) arrives 20-50 ms after my click, <paramref name="Select"/> = true;
/// 35 38 + u32 follows it, and is repeated every 0.5-1.3 s while I fight the target, or comes alone when the game aims for me (2026-11-23 recording, both directions).
/// <paramref name="RawId"/> is the entity as in <see cref="IdentityMessage.RawId"/>, 0 when the target was cleared (34 38 of six zero bytes).
/// </summary>
public sealed record TargetMessage(uint RawId, bool Select = false) : GameMessage;

/// <summary>
/// 33 36: I entered the world (login, character change, zone move): my entity number, name and server.
/// <paramref name="Points"/> is my total rank points at that moment (null when not found), see docs/PROTOCOL.md §3.4.1.
/// </summary>
public sealed record EnterWorldMessage(uint Entity, string Name, ushort Server, ulong? Points = null) : GameMessage;

/// <summary>92 family: the party I am in. Formed (00 92) and Member (0D 92) carry a character; Left (11 92) names the one who left.</summary>
public abstract record PartyMessage(ulong CharacterId) : GameMessage;
/// <param name="Others">Further members listed in the same message (a new connection lists the whole party at once).</param>
public sealed record PartyFormedMessage(ulong CharacterId, string Name, string PartyId, IReadOnlyList<(ulong Id, string Name)>? Others = null) : PartyMessage(CharacterId);
public sealed record PartyMemberMessage(ulong CharacterId, string Name, string PartyId, bool Pending = false) : PartyMessage(CharacterId);
public sealed record PartyLeftMessage(ulong CharacterId) : PartyMessage(CharacterId);
/// <summary>1C 92: a periodic status of one party member (every 1-15 s while in a party): tells who is in my party even when the party was formed before the meter started.</summary>
/// <summary>
/// 05 38 of type 0B or 0A: one tick of an over-time effect on <paramref name="Target"/> from <paramref name="Source"/> (type 0A, <paramref name="Damage"/>: damage over time of a debuff; 0B: the same entity for a heal over time such as 재생의 빛:
/// 1,203 every 2 s, equal to the combat log). Other 05 38 types (state updates) are not parsed. docs/PROTOCOL.md §3.9.
/// <paramref name="NoAmount"/>: the effect is on the target but no amount is read from the tick: type 0C (the same effect while a shield takes the damage: 2026-11-24 고통의 연쇄 on me went from
/// 0A to 0C ticks the moment 유스티엘의 권능 was on me) and the 0A ticks of a tail shape that is not understood (most of them in the recordings of 2026-11-23/24). The ledger ignores these.
/// </summary>
public sealed record PeriodicMessage(uint Target, uint Source, uint SkillKey, uint Amount, bool Damage = false, bool NoAmount = false) : GameMessage;

/// <summary>
/// 05 38 of type 09 or 08: an effect of <paramref name="Source"/> lands on <paramref name="Target"/> (09) or ends (<paramref name="Ended"/>, 08): `[target][09][source][varint][u32 stamp, high half = skill key]...`.
/// Seen with shields, heals and buffs (on oneself and on others) and, by the same layout, assumed for the effects of enemies; what an effect does is not in the message (docs/PROTOCOL.md §3.9).
/// </summary>
public sealed record EffectMessage(uint Target, uint Source, uint SkillKey, bool Ended = false) : GameMessage;

/// <summary>
/// 2A 38: an effect with an id and a duration lands on <paramref name="Target"/> (2026-11-24 recordings): `[target][01][kind 11|13][handle varint][u32 effect id][u32 duration ms][u32 0][u64 time][source varint]...`.
/// Kind 13 is an effect with a time (stun 3000, 받는 치유량 감소 10000, a potion 10000 ...), kind 11 the states and passives of the skills themselves (duration 0 or 4294967295 = permanent). The effect id is the id of the skill that
/// gave it times 10 plus its number (183300301 = skill 18330030 [마르쿠탄의 분노], effect 1; the generic ones such as 100000311 = 기절 are not a skill of their own).
/// Checked with 마르쿠탄의 분노 on a monster: stun 3000 ms (100000311), 받는 치유량 감소 10000 ms (183300301), a third 5000 ms (187800011); the removal of each comes 3.0 s, 10.0 s and 5.1 s later as `2c 38` with the handle. docs/PROTOCOL.md §3.9.
/// </summary>
public sealed record EffectAppliedMessage(uint Target, uint Source, uint EffectId, uint DurationMs, byte Kind) : GameMessage;

/// <summary>
/// 41 36: something appeared near me (entity number); a summon names its owner in this message ("신성한 기운 [아라]"). <paramref name="Owner"/> is that name,
/// empty for the other kinds (monsters) and for summons that do not name one (the elemental spirits). <paramref name="Template"/> is what kind of thing it is
/// (an NPC number: 0x2C90CB 신성한 기운, 0x2C8EFE 고대의 정령 ...), 0 when the message does not have the usual layout. <paramref name="OwnerEntity"/> is the entity number of
/// the player it belongs to (`07 02 06 <u32> 34 08` in the message), 0 when not found. docs/PROTOCOL.md §3.10.
/// </summary>
public sealed record SpawnMessage(uint Entity, string Owner, uint Template = 0, uint OwnerEntity = 0) : GameMessage;

/// <summary>0B 94: a duel (결투) ended: who won and who lost, by entity number and name (docs/PROTOCOL.md §3.8). 4 of 4 results of a recording matched.</summary>
public sealed record DuelMessage(uint WinnerEntity, uint LoserEntity, string WinnerName, string LoserName) : GameMessage;

public sealed record PartyStatusMessage(ulong CharacterId) : PartyMessage(CharacterId);

/// <summary>01 91 (while the map's boss list is open, about every second): the next spawn of every field boss slot, as epoch milliseconds (0 = no time sent). docs/PROTOCOL.md.</summary>
public sealed record FieldBossMessage(IReadOnlyList<(ushort Id, long Millis)> Entries) : GameMessage;

public sealed record RankingEntry(uint Rank, ushort Server, ulong CharacterId, string Name, string Guild, int Race, int Level, int ClassId, ulong Score);

/// <summary>55 8D (list) / 57 8D (personal). Filter 0 = ALL. Deadline = next ranking reset.</summary>
public sealed record RankingMessage(bool IsList, int Filter, IReadOnlyList<RankingEntry> Entries, DateTimeOffset Deadline, uint Total) : GameMessage;

public static class MessageParser
{
    /// <summary>Second opcode bytes seen on this connection; used to validate stream synchronization.</summary>
    public static readonly IReadOnlySet<byte> KnownSecondBytes =
        new HashSet<byte> { 0x36, 0x37, 0x38, 0x40, 0x43, 0x56, 0x61, 0x8A, 0x8D, 0x90, 0xE2, 0xE3, 0xFF };

    /// <summary>Parses a message this project uses; null for other or malformed messages.</summary>
    public static GameMessage? Parse(ReadOnlySpan<byte> m)
    {
        if (m.Length < 2) return null;
        try
        {
            return (m[0], m[1]) switch
            {
                (0x73, 0xE2) => ParsePoint(m),
                (0x04, 0x8D) => ParseDeath(m),
                (0x04, 0x38) => (GameMessage?)ParseDamage(m) ?? ParseDodge(m),
                (0x45, 0x36) => ParseIdentity(m),
                (0x35, 0x38) or (0x34, 0x38) => ParseTarget(m),
                (0x33, 0x36) => ParseEnterWorld(m),
                (0x55, 0x8D) or (0x57, 0x8D) => ParseRanking(m),
                (0x00, 0x92) => ParseParty(m, formed: true),
                (0x0D, 0x92) => ParseParty(m, formed: false),
                (0x11, 0x92) => ParsePartyLeft(m),
                (0x0C, 0x92) => ParsePartyInviteEnded(m),
                (0x1C, 0x92) => ParsePartyStatus(m),
                (0x0B, 0x94) => ParseDuel(m),
                (0x41, 0x36) => ParseSpawn(m),
                (0x05, 0x38) => ParsePeriodic(m),
                (0x2A, 0x38) => ParseEffectApplied(m),
                (0x08, 0x92) => ParsePartyJoin(m),
                (0x01, 0x91) => ParseFieldBoss(m),
                _ => null,
            };
        }
        catch (Exception e) when (e is ProtocolException or DecoderFallbackException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// 01 91, 00 00, u32 20, u16 count, then count x (u16 slot id, u64 epoch ms, 1 byte); the first slot has one more byte between id and time
    /// (101 bytes with 8 slots, ids 0FD1..0FD8 in the recording, so the slots go from offset 10 on).
    /// </summary>
    private static FieldBossMessage? ParseFieldBoss(ReadOnlySpan<byte> m)
    {
        if (m.Length < 10 || m[2] != 0 || m[3] != 0) return null;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(m[8..]);
        if (count is 0 or > 64 || 10 + 1 + count * 11 > m.Length) return null;
        var entries = new List<(ushort, long)>(count);
        int pos = 10;
        for (int i = 0; i < count; i++)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(m[pos..]);
            pos += 2 + (i == 0 ? 1 : 0);
            long ms = BinaryPrimitives.ReadInt64LittleEndian(m[pos..]);
            if (ms < 0 || ms > 4_102_444_800_000) return null;   // not a time before the year 2100: this is not the message we know
            entries.Add((id, ms));
            pos += 8 + 1;
        }
        return new FieldBossMessage(entries);
    }

    private static PointMessage? ParsePoint(ReadOnlySpan<byte> m) =>
        m.Length == 11 && m[2] == 0 ? new PointMessage(BinaryPrimitives.ReadUInt64LittleEndian(m[3..])) : null;

    /// <summary>
    /// target varint, 2 flag bytes, source varint, ..., u32 1, skill varint, total varint, [n varint, n extra varints], 01 00.
    /// Other 04 38 messages (about 97% in the first recording: short 26-28 byte ones without an amount) do not fit and give null.
    /// </summary>
    private static DamageMessage? ParseDamage(ReadOnlySpan<byte> m)
    {
        var r = new ByteReader(m, 2);
        uint target = r.Varint();
        byte flags = r.Byte();
        r.Skip(1);
        uint source = r.Varint();
        int srcEnd = r.Position;
        // The u32 before the skill is the number of the hit within a multi-hit skill (1 for the first, 2, 3 ... for the following
        // ones; 20:18 recording: those were dropped before, so the damage of every multi-hit skill was too small). The message ends
        // with the same number as a u16, then (life-steal skills) the amount the attacker healed.
        for (int i = r.Position; i + 4 <= m.Length - 2; i++)
        {
            byte index = m[i];
            if (index is < 1 or > 8 || m[i + 1] != 0 || m[i + 2] != 0 || m[i + 3] != 0) continue;
            if (ParseBody(m, i, index, target, source, flags, srcEnd) is { } d) return d;
        }
        return null;
    }

    /// <summary>
    /// A dodge: the layout of a hit (target, flags 0x02 without 0x04, 00, source, ..., hit number u32, a varint, hit number, 00) without the amount.
    /// Checked on 2026-10-14 against the chat log ("저격하는 크로카의 공격을 회피했습니다" 02:19:56 and 02:20:58, each between the hit and the effect lines in the log's order).
    /// </summary>
    private static DodgeMessage? ParseDodge(ReadOnlySpan<byte> m)
    {
        try
        {
            var r = new ByteReader(m, 2);
            uint target = r.Varint();
            byte flags = r.Byte();
            r.Skip(1);
            uint source = r.Varint();
            if ((flags & 0x06) != 0x02) return null;
            for (int i = r.Position; i + 4 <= m.Length - 2; i++)
            {
                byte index = m[i];
                if (index is < 1 or > 8 || m[i + 1] != 0 || m[i + 2] != 0 || m[i + 3] != 0) continue;
                var body = new ByteReader(m, i + 4);
                body.Varint();   // the same varint a hit has before its amount
                if (m.Length - body.Position == 2 && m[^2] == index && m[^1] == 0) return new DodgeMessage(target, source, i >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(m[(i - 4)..]) : 0);
            }
        }
        catch (ProtocolException) { }
        return null;
    }

    private static DamageMessage? ParseBody(ReadOnlySpan<byte> m, int anchor, byte index, uint target, uint source, byte flags, int srcEnd)
    {
        uint stamp = anchor >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(m[(anchor - 4)..]) : 0;
        try
        {
            var r = new ByteReader(m, anchor + 4);
            uint skill = r.Varint();
            uint total = r.Varint();
            int body = r.Position;
            // Flag 0x0C: a fixed extra amount some skills add to a hit (390 with 흡인, 강습 낙인 and 강습 공포: every one of 5 seen is the same shape, a message of its
            // own in the same millisecond as the hit): after the amount `64 00 00 00` and the hit number. Only that exact shape is read.
            if (flags == 0x0C)
            {
                return m.Length == body + 6 && m[body] == 0x64 && m[body + 1] == 0 && m[body + 2] == 0 && m[body + 3] == 0 && m[body + 4] == index && m[body + 5] == 0
                    ? new DamageMessage(target, source, flags, skill, total, [], stamp, index) : null;
            }
            // The end of the message: with or without the healed-amount varint after "index 00".
            foreach (int end in Ends(m, index, body))
            {
                var rr = new ByteReader(m, body);
                var extras = new List<uint>();
                if ((flags & 0x20) != 0 && body < end - 2)   // extra damages are listed only when the 0x20 flag says so
                {
                    uint n = rr.Varint();
                    if (n > 32) continue;
                    for (uint k = 0; k < n; k++) extras.Add(rr.Varint());
                }
                // Flag 0x40: the hit met an effect of mine (a shield, e.g. 유스티엘의 권능): a count and that many u32 stamps of it follow.
                if ((flags & 0x40) != 0 && rr.Position < end - 2)
                {
                    uint effects = rr.Varint();
                    if (effects > 8) continue;
                    rr.Skip(4 * (int)effects);
                }
                if (rr.Position != end - 2) continue;
                // Other 04 38 layouts (flags 0x46/0x54/0x56 seen) can look like this by accident: their "extras" are 4-byte values
                // larger than the total. Real extra damages are part of the total.
                if (extras.Sum(e => (long)e) > total) continue;
                uint healed = 0;
                if (end < m.Length) Varint.TryRead(m[end..], out healed, out _);
                return new DamageMessage(target, source, flags, skill, total, extras, stamp, index, Modifiers(m, srcEnd, anchor, extras.Count), healed, RegenAmount(m, srcEnd, anchor));
            }
        }
        catch (ProtocolException) { }
        return null;
    }

    /// <summary>
    /// The modifier bytes between the source and the stamp: 4 bytes, a sequence byte, then <c>[count][F ...][direction]</c>.
    /// count 3 = critical; F: 0x08 smash, 0x03 block (1 shield, 2 weapon), 0x20 regen; the byte before the stamp is the direction (2 front, 1 back).
    /// </summary>
    private static HitMods Modifiers(ReadOnlySpan<byte> m, int srcEnd, int anchor, int extras)
    {
        int count = srcEnd + 5, stamp = anchor - 4;
        if (stamp - count < 3) return HitMods.None;
        byte x = m[count], f = m[count + 1], dir = m[stamp - 1];
        var mods = HitMods.None;
        if (x == 3) mods |= HitMods.Critical;
        if ((f & 0x08) != 0) mods |= HitMods.Smash;
        // 0x80 + 0x20 without 0x40 starts a longer value (a2 56 02 was an unblocked hit): its low bits are not a block.
        // The kind of block, checked against the log of two 수호성 recordings (31 hits, all equal): F=0x01 "[방패 막기]", 0x02 "[무기 막기]", 0x11 and 0x31 "[철벽]",
        // 0x41 "[완벽 방패 막기]", 0x21 and 0x61 no tag (the 0x20 bit hides the block tag; they are blocks all the same: 비호의 방패 healed after them), 0x00 only front/back.
        if ((f & 0x03) != 0 && (f & 0xE0) != 0xA0)
        {
            if ((f & 0x10) != 0) mods |= HitMods.Immune;
            else if ((f & 0x40) != 0 && (f & 0x20) == 0) mods |= HitMods.PerfectBlock;
            else mods |= HitMods.Block;
        }
        if ((f & 0x20) != 0) mods |= HitMods.Regen;
        if ((f & 0x04) != 0 && (f & 0xE0) != 0xA0) mods |= HitMods.Perfect;
        if (extras > 0) mods |= HitMods.Multi;
        if (dir == 2) mods |= HitMods.Front | HitMods.HasDirection;
        else if (dir == 1) mods |= HitMods.HasDirection;
        return mods;
    }

    /// <summary>
    /// The life the target's 재생 stat restored with this hit (0.2 x the hit's total, as the stat says): the varint after the F byte when F has 0x20
    /// (5 of 5 checked: 984 -> 196, 1208 -> 241, 1646 -> 329, 431 -> 86, 23 -> 4). 0 when 재생 did not trigger.
    /// </summary>
    private static uint RegenAmount(ReadOnlySpan<byte> m, int srcEnd, int anchor)
    {
        int count = srcEnd + 5, stamp = anchor - 4;
        if (stamp - count < 3 || (m[count + 1] & 0x20) == 0) return 0;
        return Varint.TryRead(m[(count + 2)..(stamp - 1)], out uint v, out _) ? v : 0;
    }

    /// <summary>Possible ends of a damage message: "index 00" at the very end, or followed by one trailing varint of 1-3 bytes.</summary>
    private static IEnumerable<int> Ends(ReadOnlySpan<byte> m, byte index, int from)
    {
        var ends = new List<int>();
        if (m.Length >= 2 && m[^2] == index && m[^1] == 0) ends.Add(m.Length);
        for (int len = 1; len <= 3; len++)
        {
            int at = m.Length - len;
            if (at - 2 < from) break;
            if (m[at - 2] == index && m[at - 1] == 0 && Varint.TryRead(m[at..], out _, out int used) && used == len) ends.Add(at);
        }
        return ends;
    }

    // 00 92 / 0D 92: ..., 0x24 + 36-char party GUID, u32 character id, 2 zero bytes, u16 server, name (docs/PROTOCOL.md §3.7).
    private static PartyMessage? ParseParty(ReadOnlySpan<byte> m, bool formed)
    {
        // Each member is such an entry; a 00 92 on a new connection lists the whole party (2 entries seen for a 2-person party).
        var entries = new List<(ulong Id, string Name, string Guid)>();
        for (int i = 2; i + 37 <= m.Length; i++)
        {
            if (m[i] != 0x24 || !IsGuid(m.Slice(i + 1, 36))) continue;
            try
            {
                var r = new ByteReader(m, i + 37);
                uint id = r.UInt32();
                r.Skip(2);
                ushort server = r.UInt16();
                string name = r.String(64);
                if (id == 0 || name.Length == 0 || !Servers.IsValid(server)) continue;
                entries.Add((id, name, Encoding.ASCII.GetString(m.Slice(i + 1, 36))));
                i += 36;
            }
            catch (Exception e) when (e is ProtocolException or DecoderFallbackException) { }
        }
        if (entries.Count == 0) return null;
        var first = entries[0];
        if (!formed) return new PartyMemberMessage(first.Id, first.Name, first.Guid);
        return new PartyFormedMessage(first.Id, first.Name, first.Guid, entries.Skip(1).Select(e => (e.Id, e.Name)).ToList());
    }

    // 08 92: someone joined: ..., 0x24 + GUID, u32 character id, 2 zero bytes, u16 server, ..., name (length-prefixed), 8 bytes time.
    // Sometimes the only message about a joiner (no 0D 92 follows).
    private static PartyMemberMessage? ParsePartyJoin(ReadOnlySpan<byte> m)
    {
        if (m.Length < 3 + 37 + 8 || m[4] != 0x24 || !IsGuid(m.Slice(5, 36))) return null;
        var r = new ByteReader(m, 41);
        uint id = r.UInt32();
        r.Skip(2);
        if (id == 0 || !Servers.IsValid(r.UInt16())) return null;
        string name = "";
        for (int len = 1; len <= 48 && m.Length - 9 - len >= 41; len++)
        {
            int at = m.Length - 9 - len;
            if (m[at] != len) continue;
            try { name = new UTF8Encoding(false, true).GetString(m.Slice(at + 1, len)); break; }
            catch (DecoderFallbackException) { }
        }
        return new PartyMemberMessage(id, name, Encoding.ASCII.GetString(m.Slice(5, 36)), Pending: true);   // it also arrives when I only invite someone (2026-11-22): the person counts once their status messages come
    }

    // 05 38: target varint, 0B (a heal over time: source varint, a varint (47 in the first recordings, 8f 06 or 9b 06 in later ones), u32 stamp (high half = skill key),
    // varint counter, varint amount, then one byte and a varint (or just a varint), 01)
    // or 0A (damage over time on a target: source varint, varint, u32 stamp, varint amount, 48, varint, 01).
    private static GameMessage? ParsePeriodic(ReadOnlySpan<byte> m)
    {
        var r = new ByteReader(m, 2);
        uint target = r.Varint();
        byte type = r.Byte();
        if (type is 0x09 or 0x08)
        {
            // `<source> <varint> <u32 stamp>` and more: an effect lands on the target (09) or ends (08), the same layout as the ticks; the rest (amounts of shields and heals) is not read here
            uint from = r.Varint();
            r.Varint();
            uint applied = r.UInt32();
            return m.Length - r.Position >= 1 ? new EffectMessage(target, from, applied >> 16, Ended: type == 0x08) : null;
        }
        if (type != 0x0B && type != 0x0A && type != 0x0C) return null;
        uint source = r.Varint();
        if (type == 0x0C)
        {
            // `<source> <varint> <u32 stamp> 01 <u32 of the shield> <a few bytes> 01`: the tick of a damage effect that a shield took in full (no amount)
            r.Varint();
            uint effect = r.UInt32();
            if (r.Position + 5 > m.Length || m[r.Position] != 0x01 || m.Length - 1 - (r.Position + 5) is < 0 or > 6 || m[^1] != 1) return null;
            return new PeriodicMessage(target, source, effect >> 16, 0, true, NoAmount: true);
        }
        uint stamp, amount;
        if (type == 0x0B)
        {
            r.Varint();
            stamp = r.UInt32();
            r.Varint();
            amount = r.Varint();
            // what follows differs (a varint, or a byte and a varint): at most a few bytes, then the closing 01
            if (m.Length - 1 - r.Position is < 1 or > 6 || m[^1] != 1) return null;
            return amount > 0 ? new PeriodicMessage(target, source, stamp >> 16, amount, false) : null;
        }
        r.Varint();
        stamp = r.UInt32();
        int afterStamp = r.Position;
        amount = r.Varint();
        r.Varint();
        r.Varint();
        if (r.Position == m.Length - 1 && m[^1] == 1 && amount > 0) return new PeriodicMessage(target, source, stamp >> 16, amount, true);
        // A tail of another shape (most ticks in the recordings of 2026-11-23/24: `<byte> <2 bytes> 01` or `... 00`, the same bytes tick after tick, so not an amount):
        // the effect is on the target, but no number is read from it.
        return m.Length - afterStamp is >= 2 and <= 12 ? new PeriodicMessage(target, source, stamp >> 16, 0, true, NoAmount: true) : null;
    }

    // 2a 38 (first entry only: the messages seen have one): target varint, count, kind, handle varint, u32 effect id, u32 duration, u32, u64 time, source varint
    private static EffectAppliedMessage? ParseEffectApplied(ReadOnlySpan<byte> m)
    {
        var r = new ByteReader(m, 2);
        uint target = r.Varint();
        if (r.Byte() == 0) return null;
        byte kind = r.Byte();
        if (kind is not (0x11 or 0x13)) return null;
        r.Varint();
        uint effect = r.UInt32(), duration = r.UInt32();
        r.UInt32();
        r.UInt64();
        uint source = r.Varint();
        return effect == 0 ? null : new EffectAppliedMessage(target, source, effect, duration, kind);
    }

    // 41 36: entity varint, `5f` and three more bytes, then either `01 <length> <owner name>` (a summon of a player: 5f 00 00 00 01 06 ec9584eb9dbc = 아라) or a single `00`
    // (the elemental spirits: 5f 10 00 00 00), then what it is as a u32 (cb 90 2c 00 = 신성한 기운, fe 8e 2c 00 = 고대의 정령).
    private static SpawnMessage? ParseSpawn(ReadOnlySpan<byte> m)
    {
        var r = new ByteReader(m, 2);
        uint entity = r.Varint();
        int from = r.Position;
        uint template = 0;
        if (from + 9 <= m.Length && m[from] == 0x5F)
        {
            int q = from + 4;
            if (m[q] == 0x00) q += 1;
            else if (m[q] == 0x01 && q + 1 < m.Length) q += 2 + m[q + 1];
            else q = -1;
            if (q > 0 && q + 4 <= m.Length && m[q + 3] == 0) template = BinaryPrimitives.ReadUInt32LittleEndian(m.Slice(q, 4));
        }
        // `07 02 06 <u32> 34 08`: the player this belongs to (an entity number), also for the spirits whose message has no owner name.
        uint ownerEntity = 0;
        for (int i = from; i + 9 <= m.Length; i++)
            if (m[i] == 0x07 && m[i + 1] == 0x02 && m[i + 2] == 0x06 && m[i + 7] == 0x34 && m[i + 8] == 0x08
                && BinaryPrimitives.ReadUInt32LittleEndian(m.Slice(i + 3, 4)) is var owner && owner != 0 && Entities.IsPlayer(owner)) { ownerEntity = owner; break; }
        for (int i = from; i < from + 12 && i + 2 < m.Length; i++)
        {
            int length = m[i + 1];
            if (m[i] != 0x01 || length is < 2 or > 24 || i + 2 + length > m.Length) continue;
            try
            {
                string name = new UTF8Encoding(false, true).GetString(m.Slice(i + 2, length));
                if (name.All(ch => char.IsLetterOrDigit(ch))) return new SpawnMessage(entity, name, template, ownerEntity);
            }
            catch (DecoderFallbackException) { }
        }
        return new SpawnMessage(entity, "", template, ownerEntity);
    }

    // 0B 94: u32 my entity, 02, u32 winner entity, u32 loser entity, winner name, loser name (length-prefixed).
    private static DuelMessage? ParseDuel(ReadOnlySpan<byte> m)
    {
        var r = new ByteReader(m, 2);
        r.UInt32();
        if (r.Byte() != 2) return null;
        uint winner = r.UInt32(), loser = r.UInt32();
        string winnerName = r.String(64), loserName = r.String(64);
        return winner != 0 && loser != 0 && winnerName.Length > 0 && loserName.Length > 0 ? new DuelMessage(winner, loser, winnerName, loserName) : null;
    }

    // 1C 92: u32 character id, 2 zero bytes, u16 server, ... (71 bytes in all samples: 2 characters, 3 parties, 650 messages).
    private static PartyStatusMessage? ParsePartyStatus(ReadOnlySpan<byte> m)
    {
        if (m.Length != 71) return null;
        var r = new ByteReader(m, 2);
        uint id = r.UInt32();
        r.Skip(2);
        return id != 0 && Servers.IsValid(r.UInt16()) ? new PartyStatusMessage(id) : null;
    }

    // 11 92: u32 character id of the one who left, 2 zero bytes, u16 server.
    private static PartyLeftMessage? ParsePartyLeft(ReadOnlySpan<byte> m)
    {
        if (m.Length != 10) return null;
        var r = new ByteReader(m, 2);
        uint id = r.UInt32();
        r.Skip(2);
        return id != 0 && Servers.IsValid(r.UInt16()) ? new PartyLeftMessage(id) : null;
    }

    /// <summary>0C 92: an invitation I sent to this character ended (declined or timed out; 2026-11-22: 08 92 arrived when I invited, this 20 s later and the person never joined): same start as 11 92 (id, 00 00, server) but longer (26 bytes with the name). The invited one was counted as a member from 08 92 on.</summary>
    private static PartyLeftMessage? ParsePartyInviteEnded(ReadOnlySpan<byte> m)
    {
        if (m.Length < 10) return null;
        var r = new ByteReader(m, 2);
        uint id = r.UInt32();
        r.Skip(2);
        return id != 0 && Servers.IsValid(r.UInt16()) ? new PartyLeftMessage(id) : null;
    }

    private static bool IsGuid(ReadOnlySpan<byte> s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            bool dash = i is 8 or 13 or 18 or 23;
            if (dash ? s[i] != (byte)'-' : !Uri.IsHexDigit((char)s[i])) return false;
        }
        return true;
    }

    private static DeathMessage? ParseDeath(ReadOnlySpan<byte> m)
    {
        var r = new ByteReader(m, 2);
        uint victim = r.Varint();
        r.UInt32(); // skill
        uint killer = r.Varint();
        ushort server = r.UInt16();
        string name = r.String(256);
        r.String(256); // guild
        int race = r.UInt16();
        if (killer == 0 || name.Length == 0 || !Servers.IsValid(server)) return null;
        if (race is not (1 or 2)) race = Servers.RaceOf(server);
        return new DeathMessage(victim, killer, server, name, race, m[^1] == 1);
    }

    // Layout seen at every login / character change / zone move (one recording, 3 of 3):
    // varint entity, 5 unknown bytes, name, u16 server, then several KB of character data.
    private static EnterWorldMessage? ParseEnterWorld(ReadOnlySpan<byte> m)
    {
        if (m.Length < 256) return null;
        var r = new ByteReader(m, 2);
        uint entity = r.Varint();
        r.Skip(5);
        string name = r.String(64);
        ushort server = r.UInt16();
        return entity != 0 && name.Length > 0 && Servers.IsValid(server) ? new EnterWorldMessage(entity, name, server, FindTotal(m)) : null;
    }

    /// <summary>
    /// My total rank points inside the enter message: a u64 right after a tag ending in <c>f4 cb 1f</c> (21 f4 cb 1f and 24 f4 cb 1f seen,
    /// 14 enter messages of two characters, always exactly one such place and equal to the total the points message showed before).
    /// Null unless exactly one place fits.
    /// </summary>
    private static ulong? FindTotal(ReadOnlySpan<byte> m)
    {
        ulong? found = null;
        for (int i = 4; i + 11 <= m.Length; i++)
        {
            if (m[i] != 0xF4 || m[i + 1] != 0xCB || m[i + 2] != 0x1F) continue;
            ulong v = BinaryPrimitives.ReadUInt64LittleEndian(m[(i + 3)..]);
            if (v is < 1_000 or >= 100_000_000_000) continue;
            if (found is not null) return null;
            found = v;
        }
        return found;
    }

    /// <summary>The class of the character an identity message describes: the most common first two digits of the class skill numbers in it (11790000 + 1: 9 digits, ending in 0001).</summary>
    private static int ClassOf(ReadOnlySpan<byte> m)
    {
        Span<int> votes = stackalloc int[10];
        for (int p = 0; p + 4 <= m.Length; p++)
        {
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(m[p..]);
            if (v is >= 110_000_000 and <= 199_999_999 && v % 10_000 == 1) votes[(int)(v / 10_000_000) - 10]++;
        }
        int best = 0;
        for (int i = 1; i < 10; i++) if (votes[i] > votes[best]) best = i;
        return votes[best] == 0 ? 0 : best + 10;
    }

    /// <summary>35 38 (6 bytes: a target as u32), 34 38 (7+ bytes: three zero bytes, then the entity number: my click) and 34 38 of six zero bytes (cleared).</summary>
    private static TargetMessage? ParseTarget(ReadOnlySpan<byte> m)
    {
        if (m[0] == 0x35 && m.Length == 6)
        {
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(m[2..]);
            return id != 0 ? new TargetMessage(id) : null;
        }
        if (m[0] != 0x34) return null;
        if (m.Length == 6 && m[2..].IndexOfAnyExcept((byte)0) < 0) return new TargetMessage(0);
        if (m.Length is >= 7 and <= 9 && m[2] == 0 && m[3] == 0 && m[4] == 0)
        {
            uint id = 0;
            for (int i = m.Length - 1; i >= 5; i--) id = (id << 8) | m[i];
            return id != 0 ? new TargetMessage(id, Select: true) : null;
        }
        return null;
    }

    private static IdentityMessage? ParseIdentity(ReadOnlySpan<byte> m)
    {
        var r = new ByteReader(m, 2);
        uint entity = r.Varint();
        r.Skip(5);
        string name = r.String(256);
        int level = (int)r.UInt32();
        int race = r.Byte();
        if (name.Length == 0 || race is not (1 or 2) || level is < 1 or > 100) return null;
        ushort rawId = m.Length >= 4 ? BinaryPrimitives.ReadUInt16LittleEndian(m[2..]) : (ushort)0;
        return new IdentityMessage(entity, name, level, race, FindServer(m, r.Position, race), rawId, ClassOf(m));
    }

    // The server code has no fixed offset: u16 code, the same u16 8 bytes later, then guild string + u16 race.
    private static ushort FindServer(ReadOnlySpan<byte> m, int start, int race)
    {
        for (int pos = start; pos < Math.Min(200, m.Length - 12); pos++)
        {
            ushort code = BinaryPrimitives.ReadUInt16LittleEndian(m[pos..]);
            if (!Servers.IsValid(code) || BinaryPrimitives.ReadUInt16LittleEndian(m[(pos + 8)..]) != code) continue;
            try
            {
                var g = new ByteReader(m, pos + 10);
                g.String(256);
                if (g.UInt16() == race && Servers.RaceOf(code) == race) return code;
            }
            catch (Exception e) when (e is ProtocolException or DecoderFallbackException) { }
        }
        return FindServerAfterAnchor(m, start, race);
    }

    // Other layout (about a third of identities, no repeated code): the server code follows the marker
    // "100000, 100000" (u32 twice) after a flag and a few varints. First code of the right race within 80 bytes.
    private static ushort FindServerAfterAnchor(ReadOnlySpan<byte> m, int start, int race)
    {
        int anchor = m[start..].IndexOf(new byte[] { 0xA0, 0x86, 0x01, 0x00, 0xA0, 0x86, 0x01, 0x00 });
        if (anchor < 0) return 0;
        for (int pos = start + anchor + 8; pos < Math.Min(start + anchor + 80, m.Length - 1); pos++)
        {
            ushort code = BinaryPrimitives.ReadUInt16LittleEndian(m[pos..]);
            if (Servers.IsValid(code) && Servers.RaceOf(code) == race) return code;
        }
        return 0;
    }

    private static RankingMessage? ParseRanking(ReadOnlySpan<byte> m)
    {
        if (m.Length < 6) return null;
        if (m[4] != 0x01) return null;   // the game sends the "my rank" of other boards the same way (03 boss, 04 dungeon, 05/06, 15): only board 01 is the ranking of rank points
        bool isList = m[0] == 0x55;
        int filter = m[5];
        var r = new ByteReader(m, 6);
        var entries = new List<RankingEntry>();
        ulong deadlineMs;
        uint total;
        if (isList)
        {
            uint count = r.Varint();
            if (count > 10000) return null;
            for (int i = 0; i < count; i++) entries.Add(Entry(ref r));
            deadlineMs = r.UInt64();
            total = r.UInt32();
        }
        else
        {
            entries.Add(Entry(ref r));
            total = r.UInt32();
            deadlineMs = r.UInt64();
        }
        double seconds = deadlineMs / 1000.0;
        if (r.Position != m.Length || seconds is < 1.5e9 or > 2.2e9) return null;
        return new RankingMessage(isList, filter, entries, DateTimeOffset.FromUnixTimeMilliseconds((long)deadlineMs), total);
    }

    private static RankingEntry Entry(ref ByteReader r)
    {
        uint rank = r.UInt32();
        ulong id = r.UInt64();
        string name = r.String(256);
        string guild = r.String(256);
        int race = r.UInt16();
        int level = (int)r.UInt32();
        r.UInt32();
        int cls = r.Byte();
        ulong score = r.UInt64();
        using (var stats = JsonDocument.Parse(r.String(4096)))
            if (stats.RootElement.ValueKind != JsonValueKind.Object) throw new ProtocolException("stats is not an object");
        r.Skip(16);
        if (name.Length == 0 || score >= 1UL << 63) throw new ProtocolException("invalid ranking row");
        return new RankingEntry(rank, (ushort)(id >> 48), id & ((1UL << 48) - 1), name, guild, race, level, cls, score);
    }
}

/// <summary>Bounds-checked little-endian reader. Throws <see cref="ProtocolException"/> when out of data.</summary>
public ref struct ByteReader(ReadOnlySpan<byte> data, int position = 0)
{
    private readonly ReadOnlySpan<byte> _data = data;
    public int Position { get; private set; } = position;

    private ReadOnlySpan<byte> Take(int n)
    {
        if (n < 0 || Position + n > _data.Length) throw new ProtocolException("truncated message");
        var s = _data.Slice(Position, n);
        Position += n;
        return s;
    }

    public void Skip(int n) => Take(n);
    public byte Byte() => Take(1)[0];
    public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

    public uint Varint()
    {
        if (!Protocol.Varint.TryRead(_data[Position..], out uint v, out int n)) throw new ProtocolException("truncated varint");
        Position += n;
        return v;
    }

    public string String(int maxLength)
    {
        uint length = Varint();
        if (length > maxLength) throw new ProtocolException("oversized string");
        return Strict.GetString(Take((int)length));
    }

    private static readonly UTF8Encoding Strict = new(false, true);
}
