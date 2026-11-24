namespace Aion2Meter.Core;

/// <summary>The nine classes by the code the game's skill numbers start with (11790000: 11 = 검성): see <c>IdentityMessage.ClassCode</c>.</summary>
public static class PlayerClass
{
    public static readonly IReadOnlyList<(int Code, string Name)> All =
    [
        (11, "검성"), (12, "수호성"), (13, "살성"), (14, "궁성"), (15, "마도성"), (16, "정령성"), (17, "치유성"), (18, "호법성"), (19, "권성"),
    ];

    public static string Name(int code) => All.FirstOrDefault(c => c.Code == code).Name ?? "?";

    /// <summary>The class a class skill belongs to: the packet's skill number is the skill code * 100 >> 16 (<c>Skills</c>), and a code starts with the class (11790000: 11); 0 for anything else (items, effects, monsters).</summary>
    public static int FromSkillKey(uint skillKey)
    {
        if (skillKey < 0x4000) return 0;
        long code = (long)skillKey * 65536 / 100 / 1_000_000;
        return code is >= 11 and <= 19 ? (int)code : 0;
    }

    /// <summary>The number a target message and an identity message use (two plain bytes) for the entity a hit names as a varint: both are the same two bytes, 14 bits (128..16383 as a two-byte varint). 0 outside that range.</summary>
    public static ushort RawIdOf(uint entity) => entity is >= 128 and < 16384 ? (ushort)(((entity >> 7) << 8) | 0x80 | (entity & 0x7F)) : (ushort)0;
}

/// <summary>The player the target marker is on now, with the class that was learned when they came into view (0 = unknown).</summary>
public sealed record TargetedPlayer(int ClassCode, string Name, uint Id = 0);
