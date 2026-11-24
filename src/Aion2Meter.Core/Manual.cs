namespace Aion2Meter.Core;

public enum ManualLineKind { Paragraph, Bullet, SubBullet }

public sealed record ManualLine(ManualLineKind Kind, string Text);

public sealed record ManualSection(string Title, IReadOnlyList<ManualLine> Lines);

/// <summary>
/// Reads the user manual (docs/MANUAL.md, embedded in the app). "## title" starts a section; "- text" is a bullet, "  - text" a sub-bullet,
/// any other non-blank line a paragraph. Everything before the first "## " (the title, notes for whoever edits the file) is ignored.
/// </summary>
public static class Manual
{
    public static IReadOnlyList<ManualSection> Parse(string markdown)
    {
        var sections = new List<ManualSection>();
        string? title = null;
        var lines = new List<ManualLine>();
        void Close()
        {
            if (title is not null) sections.Add(new ManualSection(title, [.. lines]));
            lines.Clear();
        }
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("## "))
            {
                Close();
                title = line[3..].Trim();
            }
            else if (title is null || line.Length == 0)
            {
                continue;
            }
            else if (line.TrimStart().StartsWith("- "))
            {
                var text = line.TrimStart()[2..].Trim();
                lines.Add(new ManualLine(line.StartsWith("  ") ? ManualLineKind.SubBullet : ManualLineKind.Bullet, text));
            }
            else
            {
                lines.Add(new ManualLine(ManualLineKind.Paragraph, line.Trim()));
            }
        }
        Close();
        return sections;
    }
}
