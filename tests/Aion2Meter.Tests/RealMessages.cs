namespace Aion2Meter.Tests;

/// <summary>
/// Messages taken from real recordings (hex), used by more than one test. Each says where it comes from; a sample that only one test needs stays in that test.
/// A change of the protocol shows up here first: when a new field breaks several tests, fix the sample here (or add a builder to <see cref="Bytes"/>), not every test.
/// </summary>
internal static class Real
{
    // 2026-10-13 23:31 (game time): 8 slots; slot 0FD1 = 정령왕 아그로 at 2026-10-14 09:29:16.579, 0FD6..8 = 집행자 22:45:00, 0FD3..5 = 수호신장 나흐마 10-16 23:00:00.
    public const string BossList = "01910000140000000800d40f0000330345a101000000d10fa33dd037a101000000d20f000000000000000000d30f00330345a101000000d80f60bfa83aa101000000d50f00330345a101000000d60f60bfa83aa101000000d70f60bfa83aa1010000000000";

    // 2026-10-14 02:19:56.971 ("저격하는 크로카의 공격을 회피했습니다") and a shorter one of the same recording.
    public const string Dodged30 = "0438b93c0200d98d01f4c8100003012000025b7f8e0601000000904e0100";
    public const string Dodged29 = "04386f0200d38002ef7618001c0120000067758e0901000000904e0100";
    // 2026-10-14 02:46:30.968: MY attack dodged by the target ("살두부가 저격을 회피했습니다"): the same shape with me as the source (16 of them in 02:46, the chat log has 16 such lines).
    public const string DodgedByTarget = "04388a130200b33162efd50002011000022be2905301000000e489010100";
    // The same attacker's hit that landed (1 damage, flags 06) and a recovery (flags 04): neither is a dodge.
    public const string Landed = "0438b93c0600d3cb01f4c8100002022000015b7f8e0601000000904e010100";
    public const string Recovery = "0438e6590400e6596ed705013f020327486601000000ba940191130100";

    // 2026-10-14 00:58:41: my rank on the main board (class filter 8, rank 23 of 3,055) and, right after, on the boss board (03): that one is no ranking of rank points.
    public const string MainBoardRank = "578d0000010817000000513800000000ee0306ec9584eb9dbc06ed9598eb82980100320000000000000008c0cb7802000000003a7b226b696c6c5f636f756e74223a323733332c2264656174685f636f756e74223a313530302c226173736973745f636f756e74223a393537387d00000000040000000600000006000000ef0b0000d4b70a36a1010000";
    public const string BossBoardRank = "578d0000030093020000513800000000ee0306ec9584eb9dbc06ed9598eb829801003200000000000000080306180000000000677b22626f73735f67726f75705f6964223a333030342c22626573745f636c6561725f706f696e745f6c6576656c223a31302c22626573745f636c6561725f706f696e745f74696d65223a3134382c22626573745f636c6561725f706f696e74223a34373135327d00000000f3050000000000000800000010270000d4b70a36a1010000";

    // 00:35:53 and 00:36:00 of the 2026-10-14 recording: 면죄 (670e) 4,222 and 재생의 빛 (65dd) 1,195 over-time recoveries, as the chat log says.
    public const string TickMyeonjoe = "0538ca2b0bca2b8f06eb810e67f88301fe2038d30701";
    public const string TickRegen = "0538ca2b0bca2b9b06e37fdd658b70ab0966c60401";
}
