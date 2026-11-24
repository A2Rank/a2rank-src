using Aion2Meter.Core;

namespace Aion2Meter.Tests;

/// <summary>The real docs/CHANGELOG.md: it ships inside the app, so a release must have its entry.</summary>
public class ChangelogFileTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Aion2Meter.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine([dir.FullName, .. parts]);
    }

    [Fact]
    public void The_changelog_has_an_entry_with_changes_for_the_version_being_built()
    {
        var csproj = File.ReadAllText(RepoFile("src", "Aion2Meter.App", "Aion2Meter.App.csproj"));
        var version = System.Text.RegularExpressions.Regex.Match(csproj, "<Version>([^<]+)</Version>").Groups[1].Value;
        var entries = Changelog.Parse(File.ReadAllText(RepoFile("docs", "CHANGELOG.md")));
        var entry = Assert.Single(entries, e => e.Version == version);
        Assert.NotEmpty(entry.Items);
    }

    [Fact]
    public void All_versions_in_the_changelog_are_single_comparable_numbers_newest_first()
    {
        var versions = Changelog.Parse(File.ReadAllText(RepoFile("docs", "CHANGELOG.md"))).Select(e => e.Version).ToList();
        Assert.All(versions, v => Assert.True(Version.TryParse(v, out _), $"'{v}' is not a version number"));
        Assert.Equal(versions.OrderByDescending(Changelog.Number), versions);
    }
}
