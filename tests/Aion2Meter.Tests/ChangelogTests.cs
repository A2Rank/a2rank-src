using Aion2Meter.Core;

namespace Aion2Meter.Tests;

public class ChangelogTests
{
    private const string Text = """
        # 변경 내역

        소개 문장은 무시한다.

        ## 0.1.10 (2026-10-12)
        - 처치 명단에 스킬별 피해
          - 줄을 누르면 펼쳐집니다
        - 사망 명단

        ## 0.1.9 (2026-10-11)
        - 녹화 폴더 선택

        ## 0.1.2
        - 업데이트 확인
        """;

    [Fact]
    public void Versions_dates_and_items_are_read_and_sub_points_keep_an_indent()
    {
        var entries = Changelog.Parse(Text);
        Assert.Equal(["0.1.10", "0.1.9", "0.1.2"], entries.Select(e => e.Version));
        Assert.Equal(["2026-10-12", "2026-10-11", ""], entries.Select(e => e.Date));
        Assert.Equal(["처치 명단에 스킬별 피해", "    줄을 누르면 펼쳐집니다", "사망 명단"], entries[0].Items);
    }

    [Fact]
    public void Windows_line_endings_are_fine()
    {
        Assert.Equal(3, Changelog.Parse(Text.Replace("\n", "\r\n")).Count);
    }

    [Fact]
    public void Since_picks_what_is_new_newest_first_and_compares_numbers_not_text()
    {
        var all = Changelog.Parse(Text);
        Assert.Equal(["0.1.10", "0.1.9"], Changelog.Since(all, "0.1.2", "0.1.10").Select(e => e.Version));
        Assert.Equal(["0.1.10"], Changelog.Since(all, "0.1.9", "0.1.10").Select(e => e.Version));   // 0.1.10 is newer than 0.1.9
        Assert.Empty(Changelog.Since(all, "0.1.10", "0.1.10"));
        Assert.Equal(["0.1.9"], Changelog.Since(all, "0.1.2", "0.1.9").Select(e => e.Version));      // not beyond what is installed
    }

    [Fact]
    public void Without_a_last_version_only_the_current_one_is_shown()
    {
        var all = Changelog.Parse(Text);
        Assert.Equal(["0.1.10"], Changelog.Since(all, null, "0.1.10").Select(e => e.Version));
        Assert.Empty(Changelog.Since(all, null, "9.9.9"));
    }
}
