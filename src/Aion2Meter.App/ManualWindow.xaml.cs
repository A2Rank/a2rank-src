using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Aion2Meter.Core;

namespace Aion2Meter.App;

/// <summary>The user manual (docs/MANUAL.md): a table of contents on the left, the sections as cards on the right.</summary>
public partial class ManualWindow : Window
{
    private readonly List<FrameworkElement> _cards = [];

    public ManualWindow(IReadOnlyList<ManualSection> sections)
    {
        InitializeComponent();
        Brush faint = (Brush)FindResource("FaintBrush"), accent = (Brush)FindResource("AccentBrush"), tile = (Brush)FindResource("TileBrush");
        foreach (var section in sections)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = section.Title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = accent, Margin = new Thickness(0, 0, 0, 6),
            });
            foreach (var line in section.Lines)
            {
                var (bullet, indent, color, top) = line.Kind switch
                {
                    ManualLineKind.Bullet => ("•", 0.0, (Brush)FindResource("TextBrush"), 3.0),
                    ManualLineKind.SubBullet => ("–", 16.0, faint, 1.0),
                    _ => ("", 0.0, faint, 3.0),
                };
                var row = new Grid { Margin = new Thickness(indent, top, 0, top) };
                if (bullet.Length > 0)
                {
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
                    row.Children.Add(new TextBlock { Text = bullet, Foreground = accent });
                }
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var text = new RichText { Markup = line.Text, TextWrapping = TextWrapping.Wrap, Foreground = color, LineHeight = 19 };
                Grid.SetColumn(text, bullet.Length > 0 ? 1 : 0);
                row.Children.Add(text);
                panel.Children.Add(row);
            }
            var card = new Border
            {
                Background = tile, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 0, 0, 10), Child = panel,
            };
            _cards.Add(card);
            Content.Children.Add(card);

            var button = new Button { Content = section.Title, Style = (Style)FindResource("TocButton") };
            button.Click += (_, _) => GoTo(card);
            Toc.Children.Add(button);
        }
    }

    /// <summary>Scrolls so that <paramref name="card"/> is at the top.</summary>
    private void GoTo(FrameworkElement card)
    {
        UpdateLayout();
        var top = card.TransformToAncestor(Content).Transform(new Point(0, 0)).Y;
        Scroll.ScrollToVerticalOffset(top);
    }

    /// <summary>Jumps to the first section whose title contains <paramref name="part"/>.</summary>
    public void GoToSection(string part)
    {
        int i = Toc.Children.OfType<Button>().ToList().FindIndex(b => b.Content is string s && s.Contains(part));
        if (i >= 0) Dispatcher.BeginInvoke(() => GoTo(_cards[i]), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
