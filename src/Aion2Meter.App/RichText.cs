using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Aion2Meter.App;

/// <summary>A text block that shows **bold** and `code` (the two markers the change history uses) instead of the marker characters.</summary>
public sealed class RichText : TextBlock
{
    public static readonly DependencyProperty MarkupProperty = DependencyProperty.Register(
        nameof(Markup), typeof(string), typeof(RichText), new PropertyMetadata("", (d, _) => ((RichText)d).Rebuild()));

    public string Markup
    {
        get => (string)GetValue(MarkupProperty);
        set => SetValue(MarkupProperty, value);
    }

    private void Rebuild()
    {
        Inlines.Clear();
        bool bold = false, code = false;
        foreach (var piece in Split(Markup ?? ""))
        {
            if (piece == "**") { bold = !bold; continue; }
            if (piece == "`") { code = !code; continue; }
            var run = new Run(piece);
            if (bold) run.FontWeight = FontWeights.SemiBold;
            if (code) run.Foreground = TryFindResource("AccentBrush") as Brush ?? Foreground;
            Inlines.Add(run);
        }
    }

    /// <summary>The text cut at every marker; the markers are returned as pieces of their own.</summary>
    private static IEnumerable<string> Split(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            string? marker = text.AsSpan(i).StartsWith("**") ? "**" : text[i] == '`' ? "`" : null;
            if (marker is null) continue;
            if (i > start) yield return text[start..i];
            yield return marker;
            i += marker.Length - 1;
            start = i + 1;
        }
        if (start < text.Length) yield return text[start..];
    }
}
