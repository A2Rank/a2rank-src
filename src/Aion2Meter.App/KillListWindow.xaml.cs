using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Aion2Meter.Core;
using Aion2Meter.Protocol;

namespace Aion2Meter.App;

/// <summary>One line of a list (kills or deaths), already formatted for display.</summary>
public sealed record KillRow(string Key, string Name, string Server, string Count, string Points, string Time, FontWeight Weight,
    Brush NameColor, Brush BadgeBackground, Brush BadgeForeground, Brush RowBackground, Brush AccentBar, Brush PointsColor,
    string DamageDealt = "", string DamageReceived = "", Visibility DamageVisibility = Visibility.Collapsed,
    string Chevron = "", bool CanExpand = false, string? DamageTip = null,
    string PointsSplit = "", Visibility PointsSplitVisibility = Visibility.Collapsed);

/// <summary>
/// This clock hour's kills (left) and deaths (right): docked under the overlay and moving with it, resizable.
/// Click a line to open its detail window (<see cref="KillDetailWindow"/>); drag anywhere else to move the window.
/// </summary>
public partial class KillListWindow : Window
{
    private const int MaxSplit = 4;
    private const string OpenGlyph = "";   // Segoe MDL2 Assets: open in new window

    private readonly Dictionary<string, (KillTally Tally, bool Death)> _tallies = [];
    private KillDetailWindow? _detail;
    private string? _detailKey;
    private Point _pressAt;
    private KillRow? _pressedRow;
    private bool _pressing, _dragging;

    public KillListWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Native.MakeOverlay(this);
    }

    /// <summary>The user dragged the window to a place of its own.</summary>
    public event Action? Dragged;

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        _pressing = true;
        _dragging = false;
        _pressAt = e.GetPosition(this);
        _pressedRow = RowAt(e.OriginalSource as DependencyObject);
    }

    // A press that moves a few pixels is a drag of the window; one that does not is a click on a line.
    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_pressing || _dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var at = e.GetPosition(this);
        if (Math.Abs(at.X - _pressAt.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(at.Y - _pressAt.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragging = true;
        DragMove(); // returns when the mouse button is released
        _pressing = false;
        _pressedRow = null;
        Dragged?.Invoke();
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        var row = _pressedRow;
        bool clicked = _pressing && !_dragging;
        _pressing = false;
        _dragging = false;
        _pressedRow = null;
        if (!clicked || row is not { CanExpand: true } || (RowAt(e.OriginalSource as DependencyObject) is { } up && up.Key != row.Key)) return;
        OpenDetail(row.Key);
    }

    private static KillRow? RowAt(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement { DataContext: KillRow row }) return row;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }

    private void OpenDetail(string key)
    {
        if (!_tallies.TryGetValue(key, out var item)) return;
        if (_detail is null || !_detail.IsLoaded)
        {
            _detail = new KillDetailWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = Math.Max(0, Left + 24), Top = Math.Max(0, Top + 24) };
            _detail.Closed += (_, _) => _detailKey = null;
        }
        _detailKey = key;
        _detail.Display(item.Tally, item.Death);
        _detail.Show();
        _detail.Activate();
    }

    public void DockUnder(Window overlay)
    {
        Left = overlay.Left;
        Top = overlay.Top + overlay.ActualHeight + 6;
    }

    private HourlyKillList? _shown;
    private int _shownRevision = -1, _shownThreshold;
    private long _shownAt;

    /// <summary>Of the names that are not pinned, this many (the newest) are listed: some hundreds of rows made the window crawl.</summary>
    private const int MaxOtherRows = 50;

    public void Display(HourlyKillList list)
    {
        // Redrawn only when something changed, and at most about once a second: rebuilding every row each second was the lag.
        long now = Environment.TickCount64;
        if (ReferenceEquals(list, _shown) && list.Revision == _shownRevision && list.Threshold == _shownThreshold) return;
        if (ReferenceEquals(list, _shown) && now - _shownAt < 1500) return;
        (_shown, _shownRevision, _shownThreshold, _shownAt) = (list, list.Revision, list.Threshold, now);
        _tallies.Clear();
        var hour = Kst.Of(list.Hour);
        HourText.Text = $"{hour:HH}:00 – {hour.AddHours(1):HH}:00";
        KillCount.Text = $"{list.KillCount}회";
        UnknownCount.Text = list.UnknownCount > 0 ? $"{list.UnknownCount}건" : "-";
        UnknownCount.ToolTip = list.UnknownCount > 0 ? $"이름을 확인하지 못한 처치 포인트 {Fmt.Signed(list.UnknownPoints)}" : null;
        DeathCount.Text = list.Deaths > 0 ? $"{list.Deaths}회" : "-";

        Brush Res(string key) => (Brush)FindResource(key);
        Brush repeat = Res("RepeatBrush"), tint = Res("RepeatTintBrush"), tile = Res("TileBrush"), text = Res("TextBrush"),
              ink = Res("PanelBaseBrush"), gain = Res("GainBrush"), loss = Res("LossBrush");

        KillRow Row(KillTally t, bool death, bool pinned)
        {
            string key = (death ? "d|" : "k|") + t.Name + "|" + t.Server;
            _tallies[key] = (t, death);
            bool canExpand = t.Dealt > 0 || t.Received > 0 || t.Attackers is { Count: > 0 } || t.Count > 1;
            // The list shows the latest kill (or death) only; the whole story is in the detail window.
            // Points of each kill, newest last (not for a single kill: the total is that).
            string split = t.Entries is { Count: > 1 } many
                ? (many.Count > MaxSplit ? "… " : "") + string.Join(" · ", many.TakeLast(MaxSplit).Select(e => Fmt.Signed(e.Delta))) : "";
            var latest = t.Entries?.LastOrDefault()?.Damage;
            long latestDealt = latest?.Dealt ?? 0, latestReceived = latest?.Received ?? 0;
            return new KillRow(key, t.Name, Servers.Label(t.Server), t.Count.ToString(), t.Entries is { Count: > 0 } es && es.All(e => e.Duel) ? "결투" : Fmt.Signed(t.Points),
                $"{Kst.Of(t.Last):HH:mm}", pinned ? FontWeights.Bold : FontWeights.SemiBold,
                NameColor: pinned ? repeat : text,
                BadgeBackground: death ? loss : pinned ? repeat : tile,
                BadgeForeground: death || pinned ? ink : text,
                RowBackground: pinned ? tint : Brushes.Transparent,
                AccentBar: pinned ? repeat : Brushes.Transparent,
                PointsColor: death ? loss : gain,
                DamageDealt: latestDealt > 0 ? $"최근 준 {Fmt.Damage(latestDealt)}" + (latestReceived > 0 ? "  " : "") : "",
                DamageReceived: latestReceived > 0 ? (latestDealt > 0 ? "· " : "") + $"받음 {Fmt.Damage(latestReceived)}" : "",
                DamageVisibility: latestDealt > 0 || latestReceived > 0 ? Visibility.Visible : Visibility.Collapsed,
                Chevron: canExpand ? OpenGlyph : "",
                CanExpand: canExpand,
                DamageTip: canExpand ? "눌러서 자세히 보기" : null,
                PointsSplit: split, PointsSplitVisibility: split.Length > 0 ? Visibility.Visible : Visibility.Collapsed);
        }

        var sorted = list.Sorted();
        var pinnedRows = sorted.Where(list.IsPinned).Select(t => Row(t, false, true)).ToList();
        var others = sorted.Where(t => !list.IsPinned(t)).ToList();
        var otherRows = others.Take(MaxOtherRows).Select(t => Row(t, false, false)).ToList();
        OtherMore.Text = others.Count > MaxOtherRows ? $"… 그 외 {others.Count - MaxOtherRows}명 (오래된 처치)" : "";
        OtherMore.Visibility = others.Count > MaxOtherRows ? Visibility.Visible : Visibility.Collapsed;
        PinnedRows.ItemsSource = pinnedRows;
        OtherRows.ItemsSource = otherRows;
        PinnedLabel.Text = $"{list.Threshold}회 이상";
        PinnedLabel.Visibility = pinnedRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // The "recent" label only separates the two groups.
        OtherLabel.Visibility = pinnedRows.Count > 0 && otherRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Empty.Visibility = sorted.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var killers = list.Killers();
        DeathRows.ItemsSource = killers.Select(t => Row(t, true, false)).ToList();
        DeathLabel.Text = list.Deaths > 0 ? $"사망 {list.Deaths}회" : "사망";
        DeathEmpty.Visibility = killers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_detail is { IsLoaded: true } && _detailKey is not null && _tallies.TryGetValue(_detailKey, out var open))
            _detail.Display(open.Tally, open.Death);
    }

}
