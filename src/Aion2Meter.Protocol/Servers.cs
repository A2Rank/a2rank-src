namespace Aion2Meter.Protocol;

/// <summary>Server codes: 1001-1021 Elyos (race 1), 2001-2021 Asmodian (race 2); the names are those of the official character search (aion2.plaync.com/api/gameinfo/servers). See docs/PROTOCOL.md §3.5.</summary>
public static class Servers
{
    private static readonly string[] Elyos =
        ["시엘", "네자칸", "바이젤", "카이시넬", "유스티엘", "아리엘", "프레기온", "메스람타에다", "히타니에", "나니아",
         "타하바타", "루터스", "페르노스", "다미누", "카사카", "바카르마", "챈가룽", "코치룽", "이슈타르", "티아마트", "포에타"];
    private static readonly string[] Asmodian =
        ["이스라펠", "지켈", "트리니엘", "루미엘", "마르쿠탄", "아스펠", "에레슈키갈", "브리트라", "네몬", "하달",
         "루드라", "울고른", "무닌", "오다르", "젠카카", "크로메데", "콰이링", "바바룽", "파프니르", "인드나흐", "이스할겐"];

    public static bool IsValid(int code) => code is >= 1001 and <= 1021 or >= 2001 and <= 2021;

    public static int RaceOf(int code) => code is >= 1001 and <= 1021 ? 1 : code is >= 2001 and <= 2021 ? 2 : 0;

    public static string Label(int code) => code switch
    {
        >= 1001 and <= 1021 => Elyos[code - 1001],
        >= 2001 and <= 2021 => Asmodian[code - 2001],
        0 => "서버 확인 필요",
        _ => $"서버 확인 필요 ({code})",
    };
}
