using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Aion2Meter.Core;

namespace Aion2Meter.App;

/// <summary>A change line ready for display: bullet, indent and text colour (sub-points are fainter).</summary>
public sealed record ChangeItem(string Text, string Bullet, Thickness Indent, Brush Color);

public sealed record ChangeSection(string Header, string Date, IReadOnlyList<ChangeItem> Items);

/// <summary>What changed: shown after an update, before applying a downloaded one, and from the settings window.</summary>
public partial class ChangelogWindow : Window
{
    /// <param name="activate">False for the pop-up after an update, so it does not take the keyboard away from the game.</param>
    public ChangelogWindow(string title, string? subtitle, IReadOnlyList<ChangelogEntry> entries, bool activate)
    {
        InitializeComponent();
        ShowActivated = activate;
        Topmost = !activate;
        TitleText.Text = title;
        Subtitle.Text = subtitle ?? "";
        Subtitle.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;
        Brush text = (Brush)FindResource("TextBrush"), faint = (Brush)FindResource("FaintBrush");
        Sections.ItemsSource = entries.Select(e => new ChangeSection("v" + e.Version, e.Date,
            [.. e.Items.Select(i => i.StartsWith("    ")
                ? new ChangeItem(i.Trim(), "–", new Thickness(16, 1, 0, 1), faint)
                : new ChangeItem(i, "•", new Thickness(0, 2, 0, 2), text))])).ToList();
        if (entries.Count == 0)
            Sections.ItemsSource = new[] { new ChangeSection("", "", [new ChangeItem("표시할 변경 내용이 없습니다.", "", new Thickness(0), faint)]) };
    }

    private void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
