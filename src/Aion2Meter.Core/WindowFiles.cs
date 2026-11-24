using System.Globalization;
using System.Text;

namespace Aion2Meter.Core;

/// <summary>
/// The record files in the folder the user chose (off by default): every counted period ("구간 기록", see <see cref="WindowRecord"/>) adds a line
/// to the period file of its character and day, and its kills, deaths and duels to the second file. Both are CSV (UTF-8 with BOM so
/// spreadsheet programs read Korean correctly). The files are the user's own: the other players' names are in them.
/// </summary>
public static class WindowFiles
{
    /// <summary>Largest party size with a column of its own; bigger parties are summed in the last one.</summary>
    public const int PartyColumns = 5;

    public const string PeriodHeader = "날짜,시작,끝,구분,순증,획득,손실,횟수,시작 총 포인트,끝 총 포인트,처치,사망,결투 승,결투 패,몬스터,솔로 획득,2인 획득,3인 획득,4인 획득,5인 이상 획득,첫 변화,마지막 변화";
    public const string KillHeader = "날짜,시각,구분,포인트,상대,서버,구간 시작";

    private static string Who(WindowRecord r)
    {
        string who = r.Server != 0 ? $"{r.CharacterName}_{Protocol.Servers.Label(r.Server)}" : r.CharacterName;
        foreach (char c in Path.GetInvalidFileNameChars()) who = who.Replace(c, '_');
        return who;
    }

    /// <summary>One file per character and day (the day the period started, KST): the periods.</summary>
    public static string PeriodFileName(WindowRecord r) => $"랭킹포인트_{Who(r)}_{Kst.Of(r.Start):yyyy-MM-dd}.csv";

    /// <summary>The second file of the same character and day: the kills, deaths and duels.</summary>
    public static string KillFileName(WindowRecord r) => $"처치사망_{Who(r)}_{Kst.Of(r.Start):yyyy-MM-dd}.csv";

    private static string Cell(string text) => text.IndexOfAny([',', '"', '\n', '\r']) < 0 ? text : "\"" + text.Replace("\"", "\"\"") + "\"";

    /// <summary>A duel won counts as a kill and one lost as a death, as in the kill list.</summary>
    private static bool IsKill(WindowKill k) => k.Kind == KillKind.Kill || k is { Kind: KillKind.Duel, Delta: > 0 };
    private static bool IsDeath(WindowKill k) => k.Kind == KillKind.Death || k is { Kind: KillKind.Duel, Delta: <= 0 };

    /// <summary>Gain by party size, one cell per party column.</summary>
    private static IEnumerable<string> PartyGains(WindowRecord r)
    {
        for (int size = 1; size <= PartyColumns; size++)
        {
            long gain = r.Parties.Where(p => size == PartyColumns ? p.Size >= size : p.Size == size).Sum(p => p.Gain);
            yield return gain.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>One period as a row of the period file.</summary>
    public static string PeriodRow(WindowRecord r)
    {
        var start = Kst.Of(r.Start);
        var end = Kst.Of(r.End);
        string endText = Kst.Day(end) == Kst.Day(start) ? end.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : end.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        int kills = r.Kills.Count(IsKill), deaths = r.Kills.Count(IsDeath);
        int duelWins = r.Kills.Count(k => k is { Kind: KillKind.Duel, Delta: > 0 }), duelLosses = r.Kills.Count(k => k is { Kind: KillKind.Duel, Delta: <= 0 });
        return string.Join(',', [start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), start.ToString("HH:mm:ss", CultureInfo.InvariantCulture), endText,
            r.Complete ? "완전한 구간" : "일부 집계", r.Gain - r.Loss, r.Gain, r.Loss, r.Events, r.StartTotal, r.EndTotal,
            kills, deaths, duelWins, duelLosses, r.Kills.Count(k => k.Kind == KillKind.Monster), .. PartyGains(r),
            Kst.Of(r.First).ToString("HH:mm:ss", CultureInfo.InvariantCulture), Kst.Of(r.Last).ToString("HH:mm:ss", CultureInfo.InvariantCulture)]);
    }

    /// <summary>The kills, deaths and duels of a period as rows of the second file, in the order they happened.</summary>
    public static IEnumerable<string> KillRows(WindowRecord r)
    {
        var period = Kst.Of(r.Start).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        foreach (var k in r.Kills.OrderBy(k => k.Time))
        {
            var time = Kst.Of(DateTimeOffset.FromUnixTimeSeconds(k.Time));
            string kind = k.Kind switch
            {
                KillKind.Kill => "처치",
                KillKind.Death => "사망",
                KillKind.Duel => k.Delta > 0 ? "결투 승" : "결투 패",
                _ => "몬스터",
            };
            string points = k.Kind == KillKind.Duel ? "" : k.Delta.ToString(CultureInfo.InvariantCulture);
            yield return string.Join(',', [time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), time.ToString("HH:mm:ss", CultureInfo.InvariantCulture), kind, points,
                Cell(k.Name), k.Server > 0 ? Cell(Protocol.Servers.Label(k.Server)) : "", period]);
        }
    }

    /// <summary>Adds the period (and its kills) to the files of its character and day in <paramref name="folder"/>.</summary>
    public static void Append(string folder, WindowRecord r)
    {
        Directory.CreateDirectory(folder);
        AppendLines(Path.Combine(folder, PeriodFileName(r)), PeriodHeader, [PeriodRow(r)]);
        var rows = KillRows(r).ToList();
        if (rows.Count > 0) AppendLines(Path.Combine(folder, KillFileName(r)), KillHeader, rows);
    }

    private static void AppendLines(string path, string header, IEnumerable<string> lines)
    {
        bool isNew = !File.Exists(path);
        // A file with other columns (an older version, or a different layout) gets the new layout below its own header line.
        bool writeHeader = isNew || File.ReadLines(path).FirstOrDefault()?.TrimStart('﻿') != header && !File.ReadLines(path).Any(l => l.TrimStart('﻿') == header);
        using var w = new StreamWriter(path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: isNew));
        if (writeHeader) w.WriteLine(header);
        foreach (var line in lines) w.WriteLine(line);
    }
}
