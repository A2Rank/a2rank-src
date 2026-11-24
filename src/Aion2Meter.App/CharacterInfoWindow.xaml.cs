using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aion2Meter.Core;
using Aion2Meter.Protocol;

namespace Aion2Meter.App;

/// <summary>
/// A character of the game's official character search at a glance: who it is, what is worn, the magic stones (마석) and the soul engraving (영혼 각인) of
/// every piece and added up. Opened from the name in the kill detail; the data is asked from aion2.plaync.com only then (<see cref="NcClient"/>).
/// </summary>
public partial class CharacterInfoWindow : Window
{
    private static CharacterInfoWindow? _open;
    private string _name = "";
    private int _server;
    private string? _pageUrl;
    private CancellationTokenSource? _cancel;
    private NcLoaded? _data;
    private bool _busy;

    // The pictures of the pieces, one download each (the window is drawn again whenever another piece has been read).
    private static readonly Dictionary<string, BitmapImage> Icons = [];

    public CharacterInfoWindow() => InitializeComponent();

    /// <summary>Where the window was last (null: never), where it opens by default, and what to do with a place it was moved or sized to (the controller keeps it in the settings).</summary>
    public static Func<Rect?>? LoadPlace { get; set; }
    public static Func<Rect>? DefaultPlace { get; set; }
    public static Action<Rect, bool>? SavePlace { get; set; }

    /// <summary>Opens (or reuses) the window for this character; <paramref name="activate"/> false: it does not take the keyboard from the game (the hotkey).</summary>
    public static void Show(string name, int server, Window? owner, bool activate = true)
    {
        if (_open is not { IsLoaded: true })
        {
            var window = new CharacterInfoWindow { ShowActivated = activate, Topmost = WindowPrefs.AlwaysOnTop, WindowStartupLocation = WindowStartupLocation.Manual };
            window.Place(LoadPlace?.Invoke() ?? DefaultPlace?.Invoke());
            window.LocationChanged += (_, _) => window.Remember(false);
            window.SizeChanged += (_, _) => window.Remember(false);
            window.Closing += (_, _) => window.Remember(true);
            window.Closed += (_, _) => _open = null;
            _open = window;
            window.Show();
        }
        if (activate) _open.Activate();
        _ = _open.Load(name, server, force: false);
    }

    /// <summary>The window shows this character now.</summary>
    public static bool IsShowing(string name, int server) => _open is { IsLoaded: true } w && w._name == name && w._server == server;

    public static void CloseOpen() => _open?.Close();

    /// <summary>The window as a normal one: its place and size, kept inside the screens (a monitor may be gone since).</summary>
    private void Place(Rect? place)
    {
        if (place is not { } r) return;
        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop, vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight;
        double width = Math.Clamp(r.Width, MinWidth, Math.Max(MinWidth, vw)), height = Math.Clamp(r.Height, MinHeight, Math.Max(MinHeight, vh));
        (Width, Height) = (width, height);
        Left = Math.Clamp(r.Left, vl, Math.Max(vl, vl + vw - width));
        Top = Math.Clamp(r.Top, vt, Math.Max(vt, vt + vh - Math.Min(height, 80)));
    }

    private void Remember(bool flush)
    {
        if (WindowState == WindowState.Normal && IsLoaded) SavePlace?.Invoke(new Rect(Left, Top, Width, Height), flush);
    }

    private async Task Load(string name, int server, bool force)
    {
        _cancel?.Cancel();
        var cancel = _cancel = new CancellationTokenSource();
        (_name, _server) = (name, server);
        Title = $"{name} [{Servers.Label(server)}] · 캐릭터 정보";
        _data = null;
        _busy = true;
        DetailsButton.IsEnabled = RefreshButton.IsEnabled = BrowserButton.IsEnabled = false;
        Body.Children.Clear();
        if (!force && NcClient.Cached(name, server) is { } kept)
        {
            _data = kept;
            _busy = false;
            Render(kept);   // read a moment ago: nothing is asked again (the refresh button asks anyway)
            return;
        }
        StatusText.Text = $"{name} [{Servers.Label(server)}]: 공식 캐릭터 정보실에서 찾는 중…";
        try
        {
            var result = await NcClient.LoadAsync(name, server, cancel.Token);
            if (cancel.IsCancellationRequested) return;
            _data = result;
            _busy = false;
            NcClient.Remember(result);
            Render(result);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>The button: read the stones and the engraving of every piece (about 19 requests), showing each as it arrives.</summary>
    private async void OnLoadDetails(object sender, RoutedEventArgs e)
    {
        if (_data is not { Found: true } basic || _busy) return;
        _cancel?.Cancel();
        var cancel = _cancel = new CancellationTokenSource();
        _busy = true;
        Render(basic);
        try
        {
            var result = await NcClient.LoadDetailsAsync(basic, data => Dispatcher.Invoke(() => { if (!cancel.IsCancellationRequested) { _data = data; Render(data); } }), cancel.Token);
            if (cancel.IsCancellationRequested) return;
            _data = result;
            _busy = false;
            NcClient.Remember(result);   // the fuller one replaces the basic one in memory
            Render(result);
        }
        catch (OperationCanceledException) { }
    }

    private void Render(NcLoaded d)
    {
        _pageUrl = d.PageUrl;
        BrowserButton.IsEnabled = true;
        RefreshButton.IsEnabled = !_busy;
        DetailsButton.IsEnabled = d.Found && !_busy && d.Items.Count < d.DetailsExpected;
        double scrolled = Scroll.VerticalOffset;
        Body.Children.Clear();
        if (!d.Found)
        {
            StatusText.Text = d.Problem ?? "찾지 못했습니다.";
            return;
        }
        var p = d.Profile!;
        StatusText.Text = d.Problem ?? (_busy ? $"마석·영혼 각인을 읽는 중… {d.Items.Count}/{d.DetailsExpected}" : $"{p.Name} [{p.ServerName}] · 공식 캐릭터 정보실");
        // who
        Body.Children.Add(Card(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = $"{p.Name}  [{p.ServerName}]", FontSize = 17, FontWeight = FontWeights.Bold },
                new TextBlock { Text = $"{p.RaceName} · {p.ClassName} Lv.{p.Level} · 전투력 {p.CombatPower:N0}" + (p.Title.Length > 0 ? $" · {p.Title}" : ""), Margin = new Thickness(0, 3, 0, 0) },
            },
        }));
        // the stat table as the official page shows it: the main stats and the ten god stats (with what the engraving of the pieces adds, once those are read)
        if (d.Stats is { Count: > 0 } all)
        {
            Body.Children.Add(Card(Summary("주요 스탯", NcCharacterData.MainStats(all).Select(m => new NcStat(m.Name, m.Value.ToString())).ToList(), "")));
            var fromEngraving = NcCharacterData.Sum(d.Items.SelectMany(i => i.SoulStats).Where(NcCharacterData.IsGodStat)).GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First().Value);
            string[] godOrder = ["Destruction", "Death", "Wisdom", "Destiny", "Space", "Time", "Life", "Illusion", "Freedom", "Justice"];
            var godGrid = new UniformGrid { Columns = 5 };
            foreach (var g in NcCharacterData.GodStats(all).OrderBy(g => Array.IndexOf(godOrder, g.Type) is var i && i < 0 ? 99 : i))
            {
                var cell = new StackPanel { Margin = new Thickness(0, 0, 8, 8) };
                cell.Children.Add(new TextBlock { Text = $"{g.Name}  {g.Value}", FontWeight = FontWeights.SemiBold });
                if (fromEngraving.TryGetValue(g.Name, out var plus)) cell.Children.Add(new TextBlock { Text = $"(영혼 각인 {plus})", FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xA8, 0xFF)) });
                cell.Children.Add(new TextBlock { Text = string.Join("\n", g.Effects), FontSize = 10.5, Foreground = (Brush)FindResource("FaintBrush"), TextWrapping = TextWrapping.Wrap });
                godGrid.Children.Add(cell);
            }
            Body.Children.Add(Card(new StackPanel
            {
                Children = { new TextBlock { Text = "주신 스탯", FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("AccentBrush"), Margin = new Thickness(0, 0, 0, 6) }, godGrid },
            }));
        }
        // at a glance: the stones and the engraving of all pieces added up
        var stones = NcCharacterData.Sum(d.Items.SelectMany(i => i.Stones.Select(s => new NcStat(s.Name, s.Value))));
        var soul = NcCharacterData.Sum(d.Items.SelectMany(i => i.SoulStats).Where(s => !NcCharacterData.IsGodStat(s)));
        var soulSkills = NcCharacterData.Sum(d.Items.SelectMany(i => i.SoulSkills));
        int slotsTotal = d.Items.Sum(i => i.StoneSlots), slotsFilled = d.Items.Sum(i => i.Stones.Count);
        if (d.Items.Count == 0)
        {
            Body.Children.Add(Card(new TextBlock
            {
                Text = _busy ? "마석과 영혼 각인을 읽는 중…" : "마석과 영혼 각인(합계와 장비별)은 오른쪽 위의 [마석·영혼 각인 불러오기]를 누르면 읽어 옵니다 (공식 사이트에 약 19번의 작은 요청이 갑니다).",
                TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("FaintBrush"),
            }));
        }
        else
        {
            var sums = new UniformGrid { Columns = 2, Margin = new Thickness(-4, 0, -4, 0) };
            sums.Children.Add(Pad(Card(Summary($"마석 합계 ({slotsFilled}/{slotsTotal}칸)", stones, "마석이 없습니다."))));
            sums.Children.Add(Pad(Card(Summary("영혼 각인 합계", [.. soul, .. soulSkills], "영혼 각인이 없습니다."))));
            Body.Children.Add(sums);
        }
        // every piece
        Body.Children.Add(new TextBlock { Text = "장비별", FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 6, 0, 4), Foreground = (Brush)FindResource("AccentBrush") });
        var detailed = d.Items.ToDictionary(i => i.Slot.Slot);
        foreach (var slot in d.Slots.Where(NcCharacterData.HasDetails))
            Body.Children.Add(ItemRow(slot, detailed.GetValueOrDefault(slot.Slot)));
        var others = d.Slots.Where(s => !NcCharacterData.HasDetails(s)).ToList();
        if (others.Count > 0)
            Body.Children.Add(new TextBlock
            {
                Text = "그 밖: " + string.Join(", ", others.Select(s => $"{s.Name}" + (s.Enchant + s.Exceed > 0 ? $" +{s.Enchant + s.Exceed}" : ""))),
                TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = (Brush)FindResource("FaintBrush"), Margin = new Thickness(2, 6, 0, 0),
            });
        Scroll.UpdateLayout();
        Scroll.ScrollToVerticalOffset(scrolled);   // staying where the user is while more pieces arrive
    }

    private Border Card(UIElement child) => new()
    {
        Background = (Brush)FindResource("TileBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 8), Child = child,
    };

    private static Border Pad(Border b)
    {
        b.Margin = new Thickness(4, 0, 4, 8);
        return b;
    }

    private UIElement Summary(string title, IReadOnlyList<NcStat> stats, string empty)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 19, Text = stats.Count == 0 ? empty : string.Join("   ", stats.Select(s => $"{s.Name} {s.Value}")) };
        return new StackPanel
        {
            Children = { new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("AccentBrush"), Margin = new Thickness(0, 0, 0, 4) }, text },
        };
    }

    private UIElement ItemRow(NcSlot slot, NcItem? item)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Image { Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Top };
        if (Uri.TryCreate(slot.Icon, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            if (!Icons.TryGetValue(slot.Icon, out var bitmap)) Icons[slot.Icon] = bitmap = new BitmapImage(uri);
            icon.Source = bitmap;
        }
        Grid.SetColumn(icon, 0);
        int level = slot.Enchant + slot.Exceed;
        var name = new StackPanel { Margin = new Thickness(6, 0, 8, 0) };
        name.Children.Add(new TextBlock { Text = (level > 0 ? $"+{level} " : "") + slot.Name, FontWeight = FontWeights.SemiBold, Foreground = Grade(slot.Grade), TextWrapping = TextWrapping.Wrap });
        name.Children.Add(new TextBlock { Text = SlotLabel(slot.SlotName), FontSize = 10.5, Foreground = (Brush)FindResource("FaintBrush") });
        Grid.SetColumn(name, 1);
        UIElement stones, soul;
        if (item is null)
        {
            stones = Faint(_busy ? "읽는 중…" : "");
            soul = Faint("");
        }
        else
        {
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 17 };
            foreach (var s in item.Stones)
            {
                t.Inlines.Add(new Run($"{s.Name} {s.Value}") { Foreground = Grade(s.Grade) });
                t.Inlines.Add(new Run("   "));
            }
            if (item.Stones.Count == 0) t.Inlines.Add(new Run(item.StoneSlots > 0 ? $"마석 없음 ({item.StoneSlots}칸)" : "-") { Foreground = (Brush)FindResource("FaintBrush") });
            stones = t;
            var e = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 17 };
            if (item.SoulStats.Count > 0 || item.SoulSkills.Count > 0)
            {
                e.Inlines.Add(new Run(string.Join("   ", item.SoulStats.Where(s => !NcCharacterData.IsGodStat(s)).Select(s => $"{s.Name} {s.Value}"))));
                var itemGods = item.SoulStats.Where(NcCharacterData.IsGodStat).ToList();
                if (itemGods.Count > 0)
                {
                    e.Inlines.Add(new LineBreak());
                    e.Inlines.Add(new Run("주신 " + string.Join("   ", itemGods.Select(s => $"{s.Name} {s.Value}"))) { Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xA8, 0xFF)) });
                }
                if (item.SoulSkills.Count > 0)
                {
                    e.Inlines.Add(new LineBreak());
                    e.Inlines.Add(new Run(string.Join("   ", item.SoulSkills.Select(s => $"{s.Name} {s.Value}"))) { Foreground = (Brush)FindResource("AccentBrush") });
                }
                if (item.SoulRate.Length > 0 && item.SoulRate != "100") e.Inlines.Add(new Run($"   (귀속 {item.SoulRate}%)") { Foreground = (Brush)FindResource("FaintBrush") });
            }
            else e.Inlines.Add(new Run("-") { Foreground = (Brush)FindResource("FaintBrush") });
            soul = e;
        }
        Grid.SetColumn(stones, 2);
        Grid.SetColumn(soul, 3);
        stones.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
        grid.Children.Add(icon);
        grid.Children.Add(name);
        grid.Children.Add(stones);
        grid.Children.Add(soul);
        return new Border { Background = (Brush)FindResource("TileBrush"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 4), Child = grid };
    }

    private TextBlock Faint(string text) => new() { Text = text, Foreground = (Brush)FindResource("FaintBrush") };

    private static Brush Grade(string grade) => grade switch
    {
        "Epic" => new SolidColorBrush(Color.FromRgb(0xFF, 0x9A, 0x3C)),
        "Legend" => new SolidColorBrush(Color.FromRgb(0xD7, 0x7B, 0xFF)),
        "Unique" => new SolidColorBrush(Color.FromRgb(0xFF, 0xD3, 0x4E)),
        "Rare" => new SolidColorBrush(Color.FromRgb(0x5A, 0xB0, 0xFF)),
        "Special" => new SolidColorBrush(Color.FromRgb(0x6E, 0xE7, 0xB7)),
        _ => Brushes.WhiteSmoke,
    };

    private static string SlotLabel(string slot) => slot switch
    {
        "MainHand" => "주무기", "SubHand" => "보조무기", "Helmet" => "투구", "Shoulder" => "견갑", "Torso" => "상의", "Pants" => "하의", "Gloves" => "장갑", "Boots" => "신발",
        "Cape" => "망토", "Belt" => "허리띠", "Necklace" => "목걸이", "Earring1" or "Earring2" => "귀걸이", "Ring1" or "Ring2" => "반지", "Bracelet1" or "Bracelet2" => "팔찌",
        "Brooch1" or "Brooch2" => "브로치", "Pendant" => "펜던트", _ => slot,
    };

    private void OnRefresh(object sender, RoutedEventArgs e) => _ = Load(_name, _server, force: true);

    private void OnOpenInBrowser(object sender, RoutedEventArgs e)
    {
        if (_pageUrl is not null) Process.Start(new ProcessStartInfo(_pageUrl) { UseShellExecute = true });
    }

    protected override void OnClosed(EventArgs e)
    {
        _cancel?.Cancel();
        base.OnClosed(e);
    }
}
