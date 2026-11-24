using Aion2Meter.Core;

namespace Aion2Meter.Tests;

/// <summary>Reading the character answers of the official site (the shapes of 2026-10-23) and adding stats up.</summary>
public class NcCharacterDataTests
{
    [Fact]
    public void The_profile_and_the_equipment_list_are_read()
    {
        var p = NcCharacterData.ParseProfile("""{"profile":{"characterName":"아라","className":"치유성","characterLevel":50,"combatPower":1036340,"raceName":"천족","serverName":"이슈타르","titleName":"어비스[시즌3] 대장군"}}""")!;
        Assert.Equal(("아라", "치유성", 50, 1036340L), (p.Name, p.ClassName, p.Level, p.CombatPower));
        var slots = NcCharacterData.ParseEquipment("""{"equipment":{"equipmentList":[{"enchantLevel":22,"exceedLevel":0,"grade":"Epic","icon":"i","id":5,"name":"흉갑","slotPos":5,"slotPosName":"Torso"},{"enchantLevel":20,"exceedLevel":5,"grade":"Epic","icon":"j","id":1,"name":"전곤","slotPos":1,"slotPosName":"MainHand"}],"skinList":[]}}""");
        Assert.Equal([1, 5], slots.Select(s => s.Slot));                     // by slot
        Assert.Equal(25, slots[0].Enchant + slots[0].Exceed);
        Assert.Contains("enchantLevel=25&", NcCharacterData.ItemUrl(1019, "abc%3D", slots[0]));
        Assert.True(NcCharacterData.HasDetails(slots[0]) && !NcCharacterData.HasDetails(new NcSlot(41, "Arcana1", 1, "x", "Epic", 0, 0, "")));
        Assert.Null(NcCharacterData.ParseProfile("nope"));
        Assert.Empty(NcCharacterData.ParseEquipment("{}"));
    }

    [Fact]
    public void An_item_gives_its_soul_engraving_stats_skills_and_magic_stones()
    {
        var slot = new NcSlot(5, "Torso", 7, "용암 심장의 흉갑", "Epic", 22, 0, "");
        var item = NcCharacterData.ParseItem(slot, """
            {"name":"용암 심장의 흉갑","soulBindRate":"100","magicStoneSlotCount":5,
             "mainStats":[{"id":"ArmorDefense","name":"방어력","value":"900"}],
             "subStats":[{"id":"AmplifyAllDamage","name":"피해 증폭","value":"15.9%"},{"id":"Critical","name":"치명타","value":"36"}],
             "subSkills":[{"id":1,"level":1,"name":"대지의 은총"},{"id":2,"level":1,"name":"주신의 은총"}],
             "magicStoneStat":[{"id":"HPMax","value":"+160","name":"생명력","grade":"Unique","slotPos":0},{"id":"MPMax","value":"+30","name":"정신력","grade":"Rare","slotPos":1}]}
            """);
        Assert.Equal(["피해 증폭 15.9%", "치명타 36"], item.SoulStats.Select(s => s.Name + " " + s.Value));
        Assert.Equal(["대지의 은총 +1", "주신의 은총 +1"], item.SoulSkills.Select(s => s.Name + " " + s.Value));
        Assert.Equal(["생명력 +160 Unique", "정신력 +30 Rare"], item.Stones.Select(s => $"{s.Name} {s.Value} {s.Grade}"));
        Assert.Equal((5, "100"), (item.StoneSlots, item.SoulRate));
        Assert.Empty(NcCharacterData.ParseItem(slot, "[]").Stones);
    }

    [Fact]
    public void Stats_are_added_up_by_name_and_percent_apart()
    {
        var sum = NcCharacterData.Sum([new("공격력", "+10"), new("생명력", "+160"), new("공격력", "+3"), new("공격력", "23"), new("강타", "2.4%"), new("강타", "1.1%"), new("공격력", "2%")]);
        Assert.Equal(["공격력 +36", "생명력 +160", "강타 +3.5%", "공격력 +2%"], sum.Select(s => s.Name + " " + s.Value));
        Assert.Empty(NcCharacterData.Sum([new("x", "abc")]));
    }
}

public class NcGodStatTests
{
    [Fact]
    public void A_name_that_ends_with_the_god_in_brackets_is_a_god_stat()
    {
        Assert.True(NcCharacterData.IsGodStat(new("자유[바이젤]", "+40")));
        Assert.True(NcCharacterData.IsGodStat(new("지혜[루미엘]", "+40")));
        Assert.False(NcCharacterData.IsGodStat(new("공격력", "23")));
        Assert.False(NcCharacterData.IsGodStat(new("받는 치유량", "25.9%")));
    }
}

public class NcStatTableTests
{
    [Fact]
    public void The_stat_table_gives_the_main_stats_and_the_god_stats_as_the_site_shows_them()
    {
        var all = NcCharacterData.ParseStats("""
            {"stat":{"statList":[
              {"name":"위력","type":"STR","value":319,"statSecondList":["공격력 증가 +31.9%"]},
              {"name":"체력","type":"CON","value":183,"statSecondList":["생명력 증가 +18.3%"]},
              {"name":"정확","type":"AGI","value":303,"statSecondList":["명중 증가 +30.3%","치명타 증가 +30.3%"]},
              {"name":"자유[바이젤]","type":"Freedom","value":113,"statSecondList":["명중 증가 +22.6%","회피 증가 +22.6%"]},
              {"name":"파괴[지켈]","type":"Destruction","value":170,"statSecondList":["공격력 증가 +34%"]},
              {"name":"아이템레벨","type":"ItemLevel","value":6734,"statSecondList":null}]}}
            """);
        Assert.Equal(6, all.Count);
        Assert.Equal(["위력 319", "정확 303", "체력 183"], NcCharacterData.MainStats(all).Select(s => $"{s.Name} {s.Value}"));
        Assert.Equal(["자유[바이젤] 113", "파괴[지켈] 170"], NcCharacterData.GodStats(all).Select(s => $"{s.Name} {s.Value}"));
        Assert.Equal(["명중 증가 +22.6%", "회피 증가 +22.6%"], all[3].Effects);
        Assert.Empty(all[5].Effects);
        Assert.Empty(NcCharacterData.ParseStats("{}"));
    }
}
