using System.Globalization;
using System.Text.Json;

namespace Aion2Meter.Core;

/// <summary>One stat line of an item: "공격력" "+160", "받는 치유량" "25.9%".</summary>
public sealed record NcStat(string Name, string Value);

/// <summary>A magic stone (마석) in an item: what it gives and its grade.</summary>
public sealed record NcStone(string Name, string Value, string Grade);

/// <summary>The character at the top of the page: who it is and its combat power.</summary>
public sealed record NcProfile(string Name, string ClassName, int Level, long CombatPower, string RaceName, string ServerName, string Title);

/// <summary>One total of the stat table of the character ("위력" 319, "자유[바이젤]" 113) with the effects it gives ("명중 증가 +22.6%"), as the official page shows it.</summary>
public sealed record NcStatTotal(string Name, string Type, int Value, IReadOnlyList<string> Effects);

/// <summary>A worn piece as the equipment list gives it (before its details are asked).</summary>
public sealed record NcSlot(int Slot, string SlotName, int ItemId, string Name, string Grade, int Enchant, int Exceed, string Icon);

/// <summary>A worn piece with what its detail page says: the soul engraving (영혼 각인: stats and skill levels) and the magic stones (마석).</summary>
public sealed record NcItem(NcSlot Slot, IReadOnlyList<NcStat> MainStats, IReadOnlyList<NcStat> SoulStats, IReadOnlyList<NcStat> SoulSkills,
    IReadOnlyList<NcStone> Stones, int StoneSlots, string SoulRate);

/// <summary>
/// The character data of the official character search (aion2.plaync.com/api/character/...): reading its answers and adding stats up.
/// The shapes are those of 2026-10-23; anything missing or different gives an empty value, never an exception.
/// </summary>
public static class NcCharacterData
{
    public static string InfoUrl(int server, string characterId) => $"{NcCharacter.Site}/api/character/info?lang=ko&characterId={characterId}&serverId={server}";
    public static string EquipmentUrl(int server, string characterId) => $"{NcCharacter.Site}/api/character/equipment?lang=ko&characterId={characterId}&serverId={server}";
    public static string ItemUrl(int server, string characterId, NcSlot s) =>
        $"{NcCharacter.Site}/api/character/equipment/item?id={s.ItemId}&enchantLevel={s.Enchant + s.Exceed}&characterId={characterId}&serverId={server}&slotPos={s.Slot}&lang=ko";

    /// <summary>A 주신 stat (the god stats: "자유[바이젤]", "파괴[지켈]", "죽음[트리니엘]", "환상[카이시넬]", "시간[시엘]", "지혜[루미엘]"): the name ends with the god in brackets.</summary>
    public static bool IsGodStat(NcStat s) => s.Name.EndsWith(']') && s.Name.Contains('[');

    /// <summary>The slots whose details are asked: weapons, armor and accessories (1-20). Runes, seals, the amulet and the arcana have no stones or engraving.</summary>
    public static bool HasDetails(NcSlot s) => s.Slot is >= 1 and <= 20 and not 9;   // 9 = the pendant: seen without stones or engraving

    public static NcProfile? ParseProfile(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var p = doc.RootElement.GetProperty("profile");
            return new NcProfile(Str(p, "characterName"), Str(p, "className"), Int(p, "characterLevel"), Long(p, "combatPower"), Str(p, "raceName"), Str(p, "serverName"), Str(p, "titleName"));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    /// <summary>The stat table of the character (info answer): the six main stats, the ten god stats ("정의[네자칸]" ...) and the item level; same order as the site.</summary>
    public static List<NcStatTotal> ParseStats(string json)
    {
        var list = new List<NcStatTotal>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.GetProperty("stat").GetProperty("statList").EnumerateArray())
                list.Add(new NcStatTotal(Str(e, "name"), Str(e, "type"), Int(e, "value"), Array(e, "statSecondList").Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").ToList()));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { }
        return list;
    }

    /// <summary>The six main stats come first (위력, 민첩, 정확, 의지, 지식, 체력 in the order of the site's page); the god stats are the ones named with a god in brackets.</summary>
    public static IEnumerable<NcStatTotal> MainStats(IEnumerable<NcStatTotal> all)
    {
        string[] order = ["STR", "DEX", "AGI", "WIS", "INT", "CON"];
        return order.Select(t => all.FirstOrDefault(s => s.Type == t)).OfType<NcStatTotal>();
    }

    public static IEnumerable<NcStatTotal> GodStats(IEnumerable<NcStatTotal> all) => all.Where(s => IsGodStat(new NcStat(s.Name, "")));

    public static List<NcSlot> ParseEquipment(string json)
    {
        var list = new List<NcSlot>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.GetProperty("equipment").GetProperty("equipmentList").EnumerateArray())
                list.Add(new NcSlot(Int(e, "slotPos"), Str(e, "slotPosName"), Int(e, "id"), Str(e, "name"), Str(e, "grade"), Int(e, "enchantLevel"), Int(e, "exceedLevel"), Str(e, "icon")));
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { }
        return list.OrderBy(s => s.Slot).ToList();
    }

    public static NcItem ParseItem(NcSlot slot, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return new NcItem(slot, [], [], [], [], 0, "");
            return new NcItem(slot, Stats(r, "mainStats"), Stats(r, "subStats"),
                Array(r, "subSkills").Select(s => new NcStat(Str(s, "name"), "+" + Int(s, "level"))).ToList(),
                Array(r, "magicStoneStat").Select(s => new NcStone(Str(s, "name"), Str(s, "value"), Str(s, "grade"))).ToList(),
                Int(r, "magicStoneSlotCount"), Str(r, "soulBindRate"));
        }
        catch (JsonException) { return new NcItem(slot, [], [], [], [], 0, ""); }
    }

    /// <summary>Adds the stats up by name (a number with % and a plain number of the same name are kept apart); in the order of the first appearance.</summary>
    public static List<NcStat> Sum(IEnumerable<NcStat> stats)
    {
        var order = new List<(string Name, bool Percent)>();
        var sums = new Dictionary<(string, bool), double>();
        foreach (var s in stats)
        {
            string v = s.Value.Trim().TrimStart('+');
            bool percent = v.EndsWith('%');
            if (!double.TryParse(v.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) continue;
            var key = (s.Name, percent);
            if (!sums.ContainsKey(key)) order.Add(key);
            sums[key] = sums.GetValueOrDefault(key) + n;
        }
        return order.Select(k => new NcStat(k.Name, (sums[k] >= 0 ? "+" : "") + Math.Round(sums[k], 1).ToString("0.#", CultureInfo.InvariantCulture) + (k.Percent ? "%" : ""))).ToList();
    }

    private static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : [];

    private static List<NcStat> Stats(JsonElement e, string name) => Array(e, name).Select(s => new NcStat(Str(s, "name"), Str(s, "value"))).ToList();

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;

    private static long Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : 0;
}
