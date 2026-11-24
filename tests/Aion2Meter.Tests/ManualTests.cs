using Aion2Meter.Core;

namespace Aion2Meter.Tests;

public class ManualTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Aion2Meter.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine([dir.FullName, .. parts]);
    }

    [Fact]
    public void Sections_bullets_sub_bullets_and_paragraphs_are_told_apart_and_the_head_is_ignored()
    {
        var s = Manual.Parse("# 제목\n안내문\n\n## 첫째\n문단\n- 항목 **굵게**\n  - 설명\n\n## 둘째\n- 하나\r\n");
        Assert.Equal(["첫째", "둘째"], s.Select(x => x.Title));
        Assert.Equal([(ManualLineKind.Paragraph, "문단"), (ManualLineKind.Bullet, "항목 **굵게**"), (ManualLineKind.SubBullet, "설명")],
            s[0].Lines.Select(l => (l.Kind, l.Text)));
        Assert.Equal([(ManualLineKind.Bullet, "하나")], s[1].Lines.Select(l => (l.Kind, l.Text)));
    }

    [Fact]
    public void The_real_manual_has_the_sections_a_user_looks_for()
    {
        var sections = Manual.Parse(File.ReadAllText(RepoFile("docs", "MANUAL.md")));
        Assert.True(sections.Count >= 10, $"only {sections.Count} sections");
        Assert.Equal(sections.Count, sections.Select(s => s.Title).Distinct().Count());
        Assert.All(sections, s => Assert.NotEmpty(s.Lines));
        foreach (var wanted in new[] { "처음 시작하기", "표시창", "처치 명단", "데이터 보내기", "구간 기록 보내기", "단축키", "문제 해결", "개인정보" })
            Assert.Contains(sections, s => s.Title.Contains(wanted));
    }

    [Fact]
    public void The_manual_names_the_default_shortcuts_the_app_really_uses()
    {
        var text = File.ReadAllText(RepoFile("docs", "MANUAL.md"));
        foreach (var gesture in new[] { "Ctrl+Alt+O", "Ctrl+Alt+P", "Ctrl+Alt+L" })
            Assert.Contains(gesture, text);
    }
}
