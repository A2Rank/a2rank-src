namespace Aion2Meter.Protocol;

/// <summary>
/// Entity numbers tell players from monsters/NPCs: players are below 16384, monsters and NPCs from there up.
/// Found, not documented by the game: in 6 recordings about 550 players (identity / enter-world messages, PvP victims) were all
/// at most 16375 and about 220 monster deaths (PvE) were all at least 16563; no sample lies in between, so the exact edge
/// (16384 = 2^14) is a guess. Summons, pets and NPC allies count as NPCs. See docs/PROTOCOL.md §3.6.
/// </summary>
public static class Entities
{
    public const uint FirstNpc = 16384;

    public static bool IsPlayer(uint entity) => entity < FirstNpc;
}
