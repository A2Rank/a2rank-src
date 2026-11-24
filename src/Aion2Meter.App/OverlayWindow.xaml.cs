using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Aion2Meter.Core;

namespace Aion2Meter.App;

/// <summary>The always-on-top panel over the game: this period's points and the buttons.</summary>
public partial class OverlayWindow : Window
{
    private readonly Controller _controller;

    public OverlayWindow(Controller controller)
    {
        _controller = controller;
        InitializeComponent();
        SourceInitialized += (_, _) => Native.MakeOverlay(this);
    }

    /// <summary>Where the pass button is, in the same units as <see cref="Window.Left"/>.</summary>
    public Rect PassButtonBounds
    {
        get
        {
            var p = PassButton.TranslatePoint(new Point(0, 0), this);
            return new Rect(Left + p.X, Top + p.Y, PassButton.ActualWidth, PassButton.ActualHeight);
        }
    }

    private readonly System.Windows.Threading.DispatcherTimer _bossBannerTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    /// <summary>The alert line at the top of the panel (field bosses); it goes away after five seconds or on a click.</summary>
    public void ShowBossBanner(string text)
    {
        BossBannerText.Text = "⏰ " + text;
        BossBanner.Visibility = Visibility.Visible;
        _bossBannerTimer.Stop();
        _bossBannerTimer.Tick -= HideBossBanner;
        _bossBannerTimer.Tick += HideBossBanner;
        _bossBannerTimer.Start();
    }

    private void HideBossBanner(object? sender, EventArgs e)
    {
        _bossBannerTimer.Stop();
        BossBanner.Visibility = Visibility.Collapsed;
    }

    private void OnBossBannerClick(object sender, MouseButtonEventArgs e) => HideBossBanner(null, EventArgs.Empty);

    private string? _partyTip;

    /// <summary>
    /// The party chip's tooltip: the members known by name, and how many are not known yet (a party seen only through status messages has no names).
    /// Set only when it changes: this runs every second, and a tooltip whose text is set again closes and never gets to stay open.
    /// </summary>
    private void SetPartyTip(PartyTracker party)
    {
        string? tip = null;
        if (party.Size > 0)
        {
            var names = party.Names.ToList();
            int unknown = party.UnknownCount;
            tip = names.Count > 0 ? "파티원: " + string.Join(", ", names) : "파티원 이름을 아직 모릅니다";
            if (unknown > 0) tip += names.Count > 0 ? $"\n(이름을 모르는 파티원 {unknown}명)" : $" ({unknown}명).\n파티를 새로 만들거나 가입할 때 측정 중이면 이름이 보입니다.";
        }
        if (tip == _partyTip) return;
        _partyTip = tip;
        PartyChip.ToolTip = tip;
    }

    public void SetPassthrough(bool on)
    {
        Native.SetClickThrough(this, on);
        PassButton.Content = on ? "" : "";
        SettingsButton.IsEnabled = ExitButton.IsEnabled = KillListButton.IsEnabled = LiveButton.IsEnabled = !on;
    }

    /// <param name="statusBrush">Resource key of the status dot color.</param>
    public void Display(MeterSession s, DateTimeOffset now, string status, string statusBrush)
    {
        CharacterText.Text = s.Character is { } c
            ? c.Label + (s.CharacterConfirmed ? "" : " · 지난 접속")
            : "랭킹 포인트";
        StatusDot.Fill = Brush(statusBrush);
        PartyChip.Visibility = s.Party.Size > 0 ? Visibility.Visible : Visibility.Collapsed;
        PartyText.Text = $"파티 {s.Party.Size}인";
        SetPartyTip(s.Party);

        var current = s.Buckets.Current;
        long net = current?.Net ?? 0;
        Net.Text = Fmt.Signed(net);
        Net.Foreground = Brush(net > 0 ? "GainBrush" : net < 0 ? "LossBrush" : "TextBrush");
        if (current is null)
        {
            Period.Text = "이번 구간 · 대기";
            Remaining.Text = "";
            ProgressFill.Width = 0;
        }
        else
        {
            Period.Text = $"이번 구간 · {Kst.Of(current.Start):HH:mm}부터" + (current.Partial ? " · 일부" : "");
            Remaining.Text = $"다음 집계 {Fmt.Remaining(current.End - now)}";
            double done = (now - current.Start) / (current.End - current.Start);
            ProgressFill.Width = Math.Clamp(done, 0, 1) * ProgressTrack.ActualWidth;
        }
        Gain.Text = Fmt.Signed(current?.Gain ?? 0);
        Loss.Text = Fmt.Signed(-(current?.Loss ?? 0));
        var previous = s.Buckets.Previous;
        PreviousCaption.Text = previous?.Partial == true ? "직전 구간 · 일부" : "직전 구간";
        Previous.Text = previous is null ? "-" : Fmt.Signed(previous.Net);
        Today.Text = Fmt.Signed(s.Today(now).Net);
        AllRank.Text = s.Rank?.AllRank is int all ? $"{all:N0}위" : "-";
        ClassRank.Text = s.Rank?.ClassRank is int cls ? $"{cls:N0}위" : "-";
        string asOf = s.Rank is { } r ? $"{Kst.Of(r.AsOf):HH:mm} 랭킹 갱신 기준" : "게임에서 랭킹 창을 열면 표시됩니다";
        AllRank.ToolTip = ClassRank.ToolTip = asOf;
        Total.Text = s.Points.Total is ulong total
            ? $"총 {total:N0}점" + (s.Points.TotalIsLive ? "" : " · 마지막 확인값")
            : "총 포인트 수신 대기";
        Status.Text = status;
    }

    private Brush Brush(string key) => (Brush)FindResource(key);

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnPass(object sender, RoutedEventArgs e) => _controller.SetPassthrough(true);
    private void OnSettings(object sender, RoutedEventArgs e) => _controller.ShowSettings();
    private void OnExit(object sender, RoutedEventArgs e) => _controller.Exit();
    private void OnKillList(object sender, RoutedEventArgs e) => _controller.ToggleKillList();
    private void OnLive(object sender, RoutedEventArgs e) => _controller.ToggleLive();
    private void OnRecords(object sender, RoutedEventArgs e) => _controller.ShowRecords();

    /// <summary>The list icon is lit while the kill list window is shown.</summary>
    public void SetKillListShown(bool shown)
    {
        KillListButton.Foreground = Brush(shown ? "AccentBrush" : "FaintBrush");
    }

    /// <summary>The chart icon is lit while the live fight window is shown.</summary>
    public void SetLiveShown(bool shown)
    {
        LiveButton.Foreground = Brush(shown ? "AccentBrush" : "FaintBrush");
    }
}

/// <summary>Shown while clicks pass through: covers the pass button so only "해제" can be clicked.</summary>
public sealed class ReleaseWindow : Window
{
    public ReleaseWindow(Action release)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        FontFamily = new FontFamily("Malgun Gothic");
        var button = new Button
        {
            Content = "해제",
            Style = (Style)Application.Current.FindResource("ReleaseButton"),
            ToolTip = "마우스 통과 해제",
        };
        button.Click += (_, _) => release();
        Content = button;
        SourceInitialized += (_, _) => Native.MakeOverlay(this);
    }

    /// <summary>Over the lock button, wide enough for the label (the buttons next to it are disabled meanwhile).</summary>
    public void Place(Rect bounds)
    {
        Left = bounds.Left;
        Top = bounds.Top;
        Width = Math.Max(bounds.Width, 44);
        Height = bounds.Height;
    }
}

public static class Fmt
{
    public static string Signed(long v) => v > 0 ? $"+{v:N0}" : v.ToString("N0");

    /// <summary>Damage amounts in Korean units: 1,234 / 12.3만 / 6,966만 / 1.23억 (the numbers get long fast against bosses).</summary>
    public static string Damage(long v) =>
        v >= 100_000_000 ? $"{v / 1e8:0.##}억"
        : v >= 1_000_000 ? $"{v / 1e4:N0}만"
        : v >= 10_000 ? $"{v / 1e4:0.#}만"
        : v.ToString("N0");

    public static string Remaining(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t:mm\\:ss}" : t.ToString("mm\\:ss");
    }
}
