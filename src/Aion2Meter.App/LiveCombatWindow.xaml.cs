using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Aion2Meter.Core;
using Aion2Meter.Protocol;

namespace Aion2Meter.App;

/// <summary>One opponent line of the live board, already formatted.</summary>
public sealed record LiveRow(uint Entity, string Rank, string Name, string Sub, string Dealt, string Received, Brush DealtBar, Brush ReceivedBar);

/// <summary>How the live board is ordered.</summary>
public enum LiveSort { Total, Dealt, Received }

/// <summary>
/// The fight as it happens: the players I trade damage with, biggest first (by what I did plus what I took, or by one of the two), only the top few
/// as lines (a 30 vs 30 fight has dozens of names) and the rest summed up. Click a line for the skill breakdown (<see cref="KillDetailWindow"/>);
/// drag anywhere else to move the window.
/// </summary>
public partial class LiveCombatWindow : Window
{
    private readonly Dictionary<uint, DamageLedger.LiveOpponent> _opponents = [];
    private KillDetailWindow? _detail;
    private uint? _detailEntity;
    private Point _pressAt;
    private LiveRow? _pressedRow;
    private int _pressedSort = -1;
    private bool _pressing, _dragging;
    private long _shownAt;

    /// <summary>The board is redrawn at most every 500 ms: false while it is too soon (the caller need not work out the board then).</summary>
    public bool Due => Environment.TickCount64 - _shownAt >= Gap;

    /// <summary>Milliseconds between two redraws of the board.</summary>
    public int Gap { get; set; } = 500;

    public LiveCombatWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Native.MakeOverlay(this);
        ShowSort(LiveSort.Total);
    }

    /// <summary>The user dragged the window to a place of its own.</summary>
    public event Action? Dragged;
    /// <summary>The user picked another order.</summary>
    public event Action<LiveSort>? SortChanged;
    /// <summary>The user emptied the board.</summary>
    public event Action? Reset;

    public void ShowSort(LiveSort sort)
    {
        void Mark(TextBlock t, bool on)
        {
            t.Foreground = (Brush)FindResource(on ? "AccentBrush" : "FaintBrush");
            t.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
        Mark(SortTotal, sort == LiveSort.Total);
        Mark(SortDealt, sort == LiveSort.Dealt);
        Mark(SortReceived, sort == LiveSort.Received);
    }

    private static bool InButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ButtonBase) return true;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    private static LiveRow? RowAt(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement { DataContext: LiveRow row }) return row;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        if (InButton(e.OriginalSource as DependencyObject)) return;
        _pressing = true;
        _dragging = false;
        _pressAt = e.GetPosition(this);
        _pressedRow = RowAt(e.OriginalSource as DependencyObject);
        _pressedSort = e.OriginalSource is TextBlock { Tag: string tag } && int.TryParse(tag, out int s) ? s : -1;
    }

    // A press that moves a few pixels is a drag of the window; one that does not is a click on a line (or on a sort word).
    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_pressing || _dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var at = e.GetPosition(this);
        if (Math.Abs(at.X - _pressAt.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(at.Y - _pressAt.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragging = true;
        DragMove();
        _pressing = false;
        _pressedRow = null;
        Dragged?.Invoke();
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        var row = _pressedRow;
        int sort = _pressedSort;
        bool clicked = _pressing && !_dragging;
        _pressing = _dragging = false;
        _pressedRow = null;
        _pressedSort = -1;
        if (!clicked) return;
        if (sort is >= 0 and <= 2)
        {
            ShowSort((LiveSort)sort);
            SortChanged?.Invoke((LiveSort)sort);
            return;
        }
        if (row is not null && RowAt(e.OriginalSource as DependencyObject) is { } up && up.Entity == row.Entity) OpenDetail(row.Entity);
    }

    private void OnReset(object sender, RoutedEventArgs e) => Reset?.Invoke();

    private void OpenDetail(uint entity)
    {
        if (!_opponents.TryGetValue(entity, out var o)) return;
        if (_detail is null || !_detail.IsLoaded)
        {
            _detail = new KillDetailWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = Math.Max(0, Left + 24), Top = Math.Max(0, Top + 24) };
            _detail.Closed += (_, _) => _detailEntity = null;
        }
        _detailEntity = entity;
        _detail.DisplayLive(o);
        _detail.Show();
        _detail.Activate();
    }

    private static long Key(DamageLedger.LiveOpponent o, LiveSort sort) => sort switch
    {
        LiveSort.Dealt => o.Dealt,
        LiveSort.Received => o.Received,
        _ => o.Dealt + o.Received,
    };

    private Brush Bar(string resource, double part)
    {
        var accent = ((SolidColorBrush)FindResource(resource)).Color;
        var track = ((SolidColorBrush)FindResource("TileBrush")).Color;
        part = Math.Clamp(part, 0, 1);
        var b = new LinearGradientBrush(new GradientStopCollection { new(accent, 0), new(accent, part), new(track, part), new(track, 1) }, new Point(0, 0), new Point(1, 0));
        b.Freeze();
        return b;
    }

    /// <summary>Redraws at most about twice a second: the numbers move with every hit.</summary>
    public void Display(IReadOnlyList<DamageLedger.LiveOpponent> board, LiveSort sort, int maxRows, bool force = false)
    {
        long now = Environment.TickCount64;
        if (!force && now - _shownAt < Gap) return;
        _shownAt = now;

        var ordered = sort == LiveSort.Total ? board : [.. board.OrderByDescending(o => Key(o, sort)).ThenBy(o => o.Entity)];
        _opponents.Clear();
        foreach (var o in ordered) _opponents[o.Entity] = o;

        OpponentCount.Text = ordered.Count == 0 ? "-" : $"{ordered.Count}명";
        long dealt = ordered.Sum(o => o.Dealt), received = ordered.Sum(o => o.Received);
        TotalDealt.Text = dealt > 0 ? Fmt.Damage(dealt) : "-";
        TotalReceived.Text = received > 0 ? Fmt.Damage(received) : "-";
        Empty.Visibility = ordered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var shown = ordered.Take(maxRows).ToList();
        double peak = Math.Max(1, shown.Select(o => Math.Max(o.Dealt, o.Received)).DefaultIfEmpty(1).Max());
        int rank = 0;
        Rows.ItemsSource = shown.Select(o =>
        {
            rank++;
            int cls = DamageLines.ClassOf(o.Class, o.ReceivedSkills);
            string sub = string.Join(" · ", new[] { o.Server != 0 ? Servers.Label(o.Server) : "", cls != 0 ? PlayerClass.Name(cls) : "" }.Where(s => s.Length > 0));
            return new LiveRow(o.Entity, rank.ToString(), o.Name.Length > 0 ? o.Name : $"#{o.Entity}", sub,
                o.Dealt > 0 ? $"준 {Fmt.Damage(o.Dealt)}" : "준 -", o.Received > 0 ? $"받음 {Fmt.Damage(o.Received)}" : "받음 -",
                Bar("AccentBrush", o.Dealt / peak), Bar("LossBrush", o.Received / peak));
        }).ToList();

        var rest = ordered.Skip(maxRows).ToList();
        More.Visibility = rest.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        More.Text = rest.Count > 0 ? $"… 그 외 {rest.Count}명 (준 {Fmt.Damage(rest.Sum(o => o.Dealt))} · 받음 {Fmt.Damage(rest.Sum(o => o.Received))})" : "";

        if (_detail is { IsLoaded: true } && _detailEntity is uint open && _opponents.TryGetValue(open, out var current)) _detail.DisplayLive(current);
    }
}
