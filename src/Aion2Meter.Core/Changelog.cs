namespace Aion2Meter.Core;

/// <summary>One released version in docs/CHANGELOG.md: its number, date and the list of changes.</summary>
public sealed record ChangelogEntry(string Version, string Date, IReadOnlyList<string> Items);

/// <summary>
/// Reads the change history (docs/CHANGELOG.md, embedded in the app) and picks what is new since a version.
/// Format: "## 0.1.10 (2026-10-12)" starts a version; lines "- text" are its changes; "  - text" is a sub-point of the one before;
/// anything else (title, blank lines) is ignored.
/// </summary>
public static class Changelog
{
    public static IReadOnlyList<ChangelogEntry> Parse(string markdown)
    {
        var entries = new List<ChangelogEntry>();
        string? version = null, date = "";
        var items = new List<string>();
        void Close()
        {
            if (version is not null) entries.Add(new ChangelogEntry(version, date, [.. items]));
            items.Clear();
        }
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("## "))
            {
                Close();
                var head = line[3..].Trim();
                int paren = head.IndexOf('(');
                version = (paren < 0 ? head : head[..paren]).Trim();
                date = paren < 0 ? "" : head[(paren + 1)..].TrimEnd(')').Trim();
            }
            else if (version is not null && line.TrimStart().StartsWith("- "))
            {
                // sub-points keep an indent so the window can show them under their parent
                items.Add((line.StartsWith("  ") ? "    " : "") + line.TrimStart()[2..].Trim());
            }
        }
        Close();
        return entries;
    }

    /// <summary>Entries newer than <paramref name="lastSeen"/> up to and including <paramref name="current"/>, newest first.
    /// With no last version only <paramref name="current"/> itself.</summary>
    public static IReadOnlyList<ChangelogEntry> Since(IReadOnlyList<ChangelogEntry> all, string? lastSeen, string current)
    {
        var cur = Number(current);
        if (lastSeen is null) return [.. all.Where(e => Number(e.Version) == cur)];
        var last = Number(lastSeen);
        return [.. all.Where(e => Number(e.Version) > last && Number(e.Version) <= cur).OrderByDescending(e => Number(e.Version))];
    }

    /// <summary>"0.1.10" compares higher than "0.1.9" (numbers, not text). Anything unreadable counts as 0.</summary>
    public static Version Number(string version)
    {
        var core = version.Split('-', '+')[0];
        return Version.TryParse(core, out var v) ? v : new Version(0, 0);
    }
}
