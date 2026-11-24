using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Aion2Meter.Core;
using Aion2Meter.Protocol;

namespace Aion2Meter.App;

/// <summary>
/// "내 기록": the counted periods ("구간 기록") the server holds for this installation, with the other players' names of the kills:
/// a summary, a chart of what was gained and lost in each period, the course of my total, the list of periods and the details of the picked one.
/// Only this installation can read them (its secret id); nothing here is public. Falls back to the copies kept on this PC when the
/// server cannot be reached.
/// </summary>
public partial class RecordsWindow : Window
{
    private readonly Func<IReadOnlyList<(string Name, int Server)>> _characters;
    private readonly Func<IEnumerable<string>> _local;
    private readonly Action _openSettings;
    private readonly Func<string, int, CancellationToken, Task<(string? Body, string? Problem)>> _fetch;
    private readonly Func<string, long, long, IReadOnlyList<KillRecord>> _names;
    private readonly Func<bool> _consented;
    private readonly Func<string?> _current;

    private readonly GainLossChart _bars = new();
    private readonly TotalChart _total = new();
    private List<Period> _all = [];
    private List<Period> _view = [];     // the picked character in the picked time range, oldest first
    private string? _character;
    private List<string> _knownNames = [];   // the characters this PC knows, the one being played first
    private string? _baseStatus;
    private int _hours = 168;            // 0 = everything, else the last N hours (a week to begin with: the PC keeps much more now)
    private int _picked = -1;            // index into _view
    private bool _daily;                 // one slot per day (the periods of a Korean-time day added up) instead of one per period

    public RecordsWindow(Controller controller)
        : this(controller.KnownCharacters, () => controller.Store.RecentWindows(5000), controller.ShowSettings,
               // Only this installation (its secret id) can read what it sent; without one nothing was ever sent.
               (name, server, cancel) => controller.Settings.InstallId is { } install
                   ? WindowUploader.FetchCharacterAsync(name, server, install, cancel)
                   : Task.FromResult<(string? Body, string? Problem)>((null, "서버에는 보낸 기록이 없습니다.")),
               (character, start, end) => controller.Store.KillsSince(character, DateTimeOffset.FromUnixTimeSeconds(start)).Where(k => k.Time.ToUnixTimeSeconds() < end).ToList(),
               () => controller.Settings.UploadWindows, () => controller.Session.Character?.Name)
    {
    }

    /// <param name="characters">The characters this PC knows (name and server): their periods are asked from the server.</param>
    /// <param name="local">The copies kept on this PC (JSON of <see cref="WindowRecord"/>, with the names), newest first: shown when the server cannot be reached.</param>
    /// <param name="fetch">Asks the server for what it holds of a character ("windows" JSON, numbers only, or why not).</param>
    /// <param name="names">This PC's kills of a character ("server:name") between two moments (unix seconds): where the other players' names come from, as the server has none.</param>
    /// <param name="consented">The consent to send periods is on.</param>
    /// <param name="current">The name of the character being played: its records are shown first (otherwise the one with the newest record).</param>
    public RecordsWindow(Func<IReadOnlyList<(string Name, int Server)>> characters, Func<IEnumerable<string>> local, Action openSettings,
        Func<string, int, CancellationToken, Task<(string? Body, string? Problem)>> fetch,
        Func<string, long, long, IReadOnlyList<KillRecord>> names, Func<bool> consented, Func<string?>? current = null)
    {
        (_characters, _local, _openSettings, _fetch, _names, _consented) = (characters, local, openSettings, fetch, names, consented);
        _current = current ?? (() => null);
        InitializeComponent();
        BarsHost.Children.Add(_bars);
        TotalHost.Children.Add(_total);
        _bars.SlotClicked += Pick;
        _total.SlotClicked += Pick;
        Loaded += async (_, _) => await Reload();
    }

    private void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private async void OnRefresh(object sender, RoutedEventArgs e) => await Reload();

    // ---- loading

    private async Task Reload()
    {
        RefreshButton.IsEnabled = false;
        try
        {
            StatusText.Text = "서버에서 불러오는 중…";
            // The server knows a character by name and server: ask for each character this PC knows. The other players' names are not there:
            // they are put on from this PC's own kills (matched by time, kind and points).
            var all = new List<Period>();
            string? problem = null;
            int asked = 0, answered = 0;
            _knownNames = _characters().Select(c => c.Name).Distinct().ToList();
            foreach (var (name, server) in _characters())
            {
                asked++;
                var (body, why) = await _fetch(name, server, CancellationToken.None);
                if (body is null) { problem ??= why; continue; }
                answered++;
                all.AddRange(ParseServer(body).Select(p => WithNames(p, $"{server}:{name}")));
            }
            // The copies kept on this PC (they carry the names; kept as long as chosen in the settings) are the history; the server has the last 7 days
            // and fills in what this PC missed (another PC, a reinstall). The same period (character, start, end) counts once.
            var have = all.Select(p => (p.Character, p.Start, p.End)).ToHashSet();
            foreach (var (p, id) in ParseLocal(_local()))
                if (have.Add((p.Character, p.Start, p.End))) all.Add(WithNames(p, id));
            _all = all;
            StatusText.Text = asked > 0 && answered == 0
                ? (problem ?? "서버에서 불러오지 못했습니다.") + $" 이 PC에 있는 기록 {_all.Count}개를 보여 드립니다."
                : $"기록 {_all.Count}개 (이 PC의 기록 + 서버에만 있는 기록). 서버에는 최근 7일치가 있고 이 PC의 기록은 설정의 보관 기간 동안 남습니다. 상대 이름은 이 PC에서만 보입니다.";
            if (_all.Count == 0)
            {
                ShowMessage(asked == 0
                    ? "아직 이 PC에서 알아본 캐릭터가 없습니다. 게임에 접속해서 측정을 켜면 그 캐릭터의 기록이 여기에 나옵니다."
                    : "아직 기록이 없습니다. 측정 중에 랭킹 포인트가 오르내린 구간이 끝나면(한 시간이 지나거나, 측정을 멈추거나, 앱을 끄면) 서버에 올라가고 이곳에 나옵니다. 포인트 변화가 없던 구간은 올라가지 않습니다."
                    + (_consented() ? "" : "\n\n지금은 설정의 \"구간 기록 보내기\"에 동의하지 않아서 새 구간이 서버로 올라가지 않습니다."), openSettings: !_consented());
                return;
            }
            Message.Visibility = Visibility.Collapsed;
            Dashboard.Visibility = Visibility.Visible;
            _baseStatus = StatusText.Text;
            // the character being played first (also when it has no record yet: the note says so and the buttons switch), else the one whose newest record is the latest
            _character ??= _current() is { } now && _knownNames.Contains(now)
                ? now
                : _all.OrderByDescending(p => p.Start).ThenByDescending(p => p.End).First().Character;
            ApplyView();
        }
        catch (JsonException e)
        {
            Log.Write("records: " + e.Message);
            StatusText.Text = "받은 내용을 읽지 못했습니다.";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void ShowMessage(string text, bool openSettings)
    {
        Dashboard.Visibility = Visibility.Collapsed;
        Chips.Children.Clear();
        Message.Children.Clear();
        Message.Visibility = Visibility.Visible;
        Message.Children.Add(Card(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, new Thickness(0, 0, 0, 8)));
        if (!openSettings) return;
        var open = new Button { Content = "설정 열기", HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += (_, _) => _openSettings();
        Message.Children.Add(open);
    }

    /// <summary>Puts the other players' names on a period of the server from this PC's kills of that moment (same second, kind and points).</summary>
    private Period WithNames(Period p, string characterId)
    {
        var mine = _names(characterId, p.Start, p.End);
        if (mine.Count == 0) return p;
        foreach (var k in mine) _localKills[(k.Time.ToUnixTimeSeconds(), (int)k.Kind, k.Delta)] = k;
        var byKey = new Dictionary<(long, int, long), string>();
        foreach (var k in mine)
            if (k.Name.Length > 0) byKey[(k.Time.ToUnixTimeSeconds(), (int)k.Kind, k.Delta)] = k.Name;
        return p with { Kills = p.Kills.Select(k => k.Name.Length == 0 && byKey.TryGetValue((k.Time, k.Kind, k.Delta), out var name) ? k with { Name = name } : k).ToList() };
    }

    private static List<Period> ParseServer(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("windows").EnumerateArray().Select(Read).ToList();
    }

    /// <summary>The copies kept on this PC, each with the id ("server:name") its kills are stored under.</summary>
    private static List<(Period P, string Id)> ParseLocal(IEnumerable<string> json) => json.Select(j =>
    {
        using var doc = JsonDocument.Parse(j);
        var p = Read(doc.RootElement);
        return (p, $"{doc.RootElement.GetProperty("c").GetProperty("s").GetInt32()}:{p.Character}");
    }).ToList();

    private static Period Read(JsonElement w)
    {
        var c = w.GetProperty("c");
        var parties = w.GetProperty("p").EnumerateArray().Select(p => (p[0].GetInt32(), p[1].GetInt64(), p[2].GetInt64(), p[3].GetInt32())).ToList();
        var kills = w.GetProperty("k").EnumerateArray().Select(k =>
            new PeriodKill(k[0].GetInt64(), k[1].GetInt32(), k[2].GetInt64(), k[3].GetInt32(), k.GetArrayLength() > 5 ? k[5].GetString() ?? "" : "")).ToList();
        return new Period(c.GetProperty("n").GetString() ?? "", w.GetProperty("start").GetInt64(), w.GetProperty("end").GetInt64(),
            w.GetProperty("t0").GetUInt64(), w.GetProperty("t1").GetUInt64(), w.GetProperty("gain").GetInt64(), w.GetProperty("loss").GetInt64(),
            w.GetProperty("ev").GetInt32(), w.GetProperty("un").GetInt64(),
            w.TryGetProperty("flags", out var f) ? f.GetString() ?? "" : "", parties, kills,
            !w.TryGetProperty("cp", out var cp) || cp.GetInt32() != 0,
            w.TryGetProperty("f", out var first) ? first.GetInt64() : w.GetProperty("start").GetInt64(),
            w.TryGetProperty("l", out var last) ? last.GetInt64() : w.GetProperty("end").GetInt64());
    }

    // ---- what is shown

    private void ApplyView()
    {
        long cutoff = _hours == 0 ? 0 : DateTimeOffset.UtcNow.AddHours(-_hours).ToUnixTimeSeconds();
        _view = _all.Where(p => p.Character == _character && p.End >= cutoff).OrderBy(p => p.Start).ThenBy(p => p.End).ToList();
        if (_daily) _view = ToDays(_view);
        _picked = _view.Count - 1;
        if (_view.Count == 0 && _character is not null)
            StatusText.Text = _hours == 0 ? $"{_character}: 구간 기록이 아직 없습니다. 측정 중에 포인트가 오르내린 구간이 끝나면 생깁니다. 위의 이름 버튼으로 다른 캐릭터를 볼 수 있습니다."
                : $"{_character}: 이 기간에는 기록이 없습니다.";
        else if (_baseStatus is not null) StatusText.Text = _baseStatus;
        BuildChips();
        BuildSummary();
        _bars.Set(_view, _picked);
        _total.Set(_view, _picked);
        BuildList();
        BuildDetail();
    }

    /// <summary>One period per Korean-time day: gains, losses, events, parties and kills added up; the points go from the first start to the last end.</summary>
    private static List<Period> ToDays(List<Period> periods) => periods
        .GroupBy(p => Kst.Of(DateTimeOffset.FromUnixTimeSeconds(p.Start)).Date)
        .OrderBy(g => g.Key)
        .Select(g =>
        {
            var list = g.OrderBy(p => p.Start).ThenBy(p => p.End).ToList();
            long start = new DateTimeOffset(g.Key, Kst.Offset).ToUnixTimeSeconds();
            var parties = list.SelectMany(p => p.Parties).GroupBy(x => x.Size)
                .Select(x => (x.Key, x.Sum(y => y.Gain), x.Sum(y => y.Loss), x.Sum(y => y.Events))).OrderBy(x => x.Key).ToList();
            return new Period(list[0].Character, start, start + 86400, list[0].T0, list[^1].T1, list.Sum(p => p.Gain), list.Sum(p => p.Loss),
                list.Sum(p => p.Events), list.Sum(p => p.Unmatched), "", parties, list.SelectMany(p => p.Kills).DistinctBy(k => (k.Time, k.Kind, k.Delta, k.Server, k.Name)).OrderBy(k => k.Time).ToList(), true,   // records saved before 0.2.13 list a hour's kills in both of its pieces
                list.Min(p => p.First), list.Max(p => p.Last), IsDay: true, Count: list.Count);
        }).ToList();

    private void Pick(int index)
    {
        if (index < 0 || index >= _view.Count) return;
        _picked = index;
        _bars.Pick(index);
        _total.Pick(index);
        BuildList();
        BuildDetail();
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private static Border Card(UIElement content, Thickness margin) => new()
    {
        Background = (Brush)Application.Current.FindResource("TileBrush"), CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12, 9, 12, 9), Margin = margin, Child = content,
    };

    private TextBlock Note(string text, Brush? color = null, double top = 3) => new()
    {
        Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0), Foreground = color ?? Res("FaintBrush"),
    };

    private Button Chip(string text, bool on, Action click)
    {
        var button = new Button
        {
            Content = text, FontSize = 11, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 4),
            Background = on ? Res("AccentBrush") : new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            Foreground = on ? new SolidColorBrush(Color.FromRgb(0x0F, 0x14, 0x1A)) : Res("TextBrush"),
            FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal,
        };
        button.Click += (_, _) => click();
        return button;
    }

    private void BuildChips()
    {
        Chips.Children.Clear();
        var characters = _knownNames.Concat(_all.Select(p => p.Character)).Distinct().ToList();   // also a character without a record yet
        if (characters.Count > 1)
            foreach (var name in characters)
                Chips.Children.Add(Chip(name, name == _character, () => { _character = name; ApplyView(); }));
        if (characters.Count > 1) Chips.Children.Add(new Border { Width = 14 });
        Chips.Children.Add(Chip("구간별", !_daily, () => { _daily = false; ApplyView(); }));
        Chips.Children.Add(Chip("일별", _daily, () => { _daily = true; ApplyView(); }));
        Chips.Children.Add(new Border { Width = 14 });
        foreach (var (label, hours) in new[] { ("전체", 0), ("최근 24시간", 24), ("최근 3일", 72), ("최근 7일", 168), ("최근 30일", 720) })
            Chips.Children.Add(Chip(label, hours == _hours, () => { _hours = hours; ApplyView(); }));
    }

    private Border Tile(string caption, string value, Brush? color = null, double size = 15) => new()
    {
        Background = Res("TileBrush"), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(3),
        Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = caption, FontSize = 11, Foreground = Res("FaintBrush") },
                new TextBlock { Text = value, FontSize = size, FontWeight = FontWeights.Bold, Foreground = color ?? Res("TextBrush"), TextTrimming = TextTrimming.CharacterEllipsis },
            },
        },
    };

    private static Brush Sign(Period p, Brush gain, Brush loss) => p.Net >= 0 ? gain : loss;

    private void BuildSummary()
    {
        Summary.Children.Clear();
        long gain = _view.Sum(p => p.Gain), loss = _view.Sum(p => p.Loss), net = gain - loss;
        // like the lists below: a duel won counts as a kill, a duel lost as a death
        int kills = _view.Sum(p => p.Kills.Count(k => k.Kind == 0 || k.Kind == 3 && k.Delta > 0));
        int deaths = _view.Sum(p => p.Kills.Count(k => k.Kind == 1 || k.Kind == 3 && k.Delta <= 0));
        int complete = _view.Count(p => p.Complete);
        Summary.Children.Add(Tile(_daily ? "일" : "구간", _daily ? $"{_view.Count}일 ({_view.Sum(p => p.Count)}구간)" : $"{_view.Count}개", size: 15));
        Summary.Children.Add(Tile("순증", Fmt.Signed(net), net >= 0 ? Res("GainBrush") : Res("LossBrush")));
        Summary.Children.Add(Tile("획득", Fmt.Signed(gain), Res("GainBrush")));
        Summary.Children.Add(Tile("손실", loss == 0 ? "0" : "-" + loss.ToString("N0"), Res("LossBrush")));
        Summary.Children.Add(Tile("처치 · 사망", $"{kills} · {deaths}", size: 15));
        Summary.Children.Add(Tile(_daily ? "하루 평균" : "구간당 평균", _view.Count == 0 ? "-" : Fmt.Signed(net / _view.Count), size: 15));
        if (!_daily && complete > 0 && complete < _view.Count) StatusText.Text = StatusText.Text.Split('\n')[0] + $"\n구간 {_view.Count}개 중 {complete}개가 한 시간을 처음부터 끝까지 측정한 완전한 구간입니다.";
    }

    private void BuildList()
    {
        PeriodList.Children.Clear();
        for (int i = _view.Count - 1; i >= 0; i--)
        {
            int index = i;
            var p = _view[i];
            bool picked = i == _picked;
            var title = new TextBlock { Text = p.Label, FontSize = 12, FontWeight = picked ? FontWeights.SemiBold : FontWeights.Normal };
            var net = new TextBlock { Text = Fmt.Signed(p.Net), FontWeight = FontWeights.SemiBold, Foreground = Sign(p, Res("GainBrush"), Res("LossBrush")) };
            // every period says which kind it is (the time range alone cannot: a hour that was only measured from its middle looks whole)
            var sub = Note($"획득 {Fmt.Signed(p.Gain)} · 손실 {(p.Loss == 0 ? "0" : "-" + p.Loss.ToString("N0"))} · " + (p.IsDay ? $"구간 {p.Count}개" : p.Complete ? "완전한 구간" : "일부 집계"),
                p.Complete && !p.IsDay ? Res("AccentBrush") : null, top: 1);
            DockPanel.SetDock(net, Dock.Right);
            var row = new DockPanel { Children = { net, title } };
            var card = new Border
            {
                Background = picked ? new SolidColorBrush(Color.FromArgb(0x33, 0x2D, 0xD4, 0xBF)) : Res("TileBrush"), CornerRadius = new CornerRadius(6),
                BorderBrush = picked ? Res("AccentBrush") : Brushes.Transparent, BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand,
                Child = new StackPanel { Children = { row, sub } },
            };
            card.MouseLeftButtonUp += (_, _) => Pick(index);
            PeriodList.Children.Add(card);
        }
    }

    private void BuildDetail()
    {
        Detail.Children.Clear();
        if (_picked < 0 || _picked >= _view.Count) return;
        var p = _view[_picked];

        var head = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        head.Children.Add(new TextBlock { Text = p.Label, FontSize = 15, FontWeight = FontWeights.SemiBold });
        var from = Kst.Of(DateTimeOffset.FromUnixTimeSeconds(p.First));
        var to = Kst.Of(DateTimeOffset.FromUnixTimeSeconds(p.Last));
        if (p.IsDay) head.Children.Add(Note($"{p.Character} · 이 날(한국 시간 0시~24시)의 구간 {p.Count}개를 합친 기록 (변화를 본 시간 {from:HH:mm:ss} ~ {to:HH:mm:ss})"));
        else head.Children.Add(Note(p.Complete
            ? $"{p.Character} · 한 시간을 처음부터 끝까지 측정한 완전한 구간 (변화가 있던 시간 {from:HH:mm:ss} ~ {to:HH:mm:ss})"
            : $"{p.Character} · 일부 집계: 이 시간 전체를 처음부터 끝까지 측정하지 못한 구간입니다 (측정을 켜거나 끄거나 끊긴 시간이 있었습니다). 변화를 본 시간 {from:HH:mm:ss} ~ {to:HH:mm:ss}"));
        if (p.Flags.Length > 0) head.Children.Add(Note("표시: " + string.Join(", ", p.Flags.Split(',').Select(Explain)), Res("WaitBrush")));
        Detail.Children.Add(head);

        var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(-3, 0, -3, 6) };
        tiles.Children.Add(Tile("순증", Fmt.Signed(p.Net), p.Net >= 0 ? Res("GainBrush") : Res("LossBrush")));
        tiles.Children.Add(Tile("획득", Fmt.Signed(p.Gain), Res("GainBrush")));
        tiles.Children.Add(Tile("손실", p.Loss == 0 ? "0" : "-" + p.Loss.ToString("N0"), Res("LossBrush")));
        tiles.Children.Add(Tile("변화 횟수", $"{p.Events}회"));
        Detail.Children.Add(tiles);

        var totals = new UniformGrid { Columns = 2, Margin = new Thickness(-3, 0, -3, 6) };
        totals.Children.Add(Tile("총 포인트 (시작 → 끝)", $"{p.T0:N0} → {p.T1:N0}", size: 13));
        int kills = p.Kills.Count(k => k.Kind == 0), deaths = p.Kills.Count(k => k.Kind == 1), duels = p.Kills.Count(k => k.Kind == 3), monsters = p.Kills.Count(k => k.Kind == 2);
        totals.Children.Add(Tile("처치 · 사망 · 결투", $"{kills} · {deaths} · {duels}" + (monsters > 0 ? $"   (몬스터 {monsters})" : ""), size: 13));
        Detail.Children.Add(totals);

        // party sizes: how the gain of this period splits (a bar per size)
        if (p.Parties.Count > 0)
        {
            var parties = new StackPanel();
            parties.Children.Add(Note("파티 인원별 순증", top: 0));
            long max = Math.Max(1, p.Parties.Max(x => Math.Abs(x.Gain - x.Loss)));
            foreach (var party in p.Parties.OrderBy(x => x.Size))
            {
                long net = party.Gain - party.Loss;
                var bar = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = net >= 0 ? Res("GainBrush") : Res("LossBrush"), HorizontalAlignment = HorizontalAlignment.Left };
                var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                grid.Children.Add(new TextBlock { Text = party.Size == 1 ? "솔로" : $"{party.Size}인", FontSize = 11.5 });
                var track = new Grid { VerticalAlignment = VerticalAlignment.Center };
                track.SizeChanged += (_, e) => bar.Width = Math.Max(2, e.NewSize.Width * Math.Abs(net) / max);
                track.Children.Add(bar);
                Grid.SetColumn(track, 1);
                grid.Children.Add(track);
                var value = new TextBlock { Text = $"{Fmt.Signed(net)} ({party.Events}회)", FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(value, 2);
                grid.Children.Add(value);
                parties.Children.Add(grid);
            }
            Detail.Children.Add(Card(parties, new Thickness(0, 0, 0, 8)));
        }

        // kills on the left, deaths on the right, each in the order they happened. Like the kill list: a duel won is a kill, a duel lost a death.
        var wins = p.Kills.Where(k => k.Kind == 0 || k.Kind == 3 && k.Delta > 0).OrderBy(k => k.Time).ToList();
        var losses = p.Kills.Where(k => k.Kind == 1 || k.Kind == 3 && k.Delta <= 0).OrderBy(k => k.Time).ToList();
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var killsColumn = KillColumn("처치", Res("GainBrush"), wins, "처치 기록이 없습니다.");
        var deathsColumn = KillColumn("사망", Res("LossBrush"), losses, "사망 기록이 없습니다.");
        killsColumn.Margin = new Thickness(0, 0, 10, 0);
        deathsColumn.Margin = new Thickness(10, 0, 0, 0);
        var divider = new Border { Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 2, 0, 2) };
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(deathsColumn, 2);
        columns.Children.Add(killsColumn);
        columns.Children.Add(divider);
        columns.Children.Add(deathsColumn);
        var box = new StackPanel();
        box.Children.Add(columns);
        if (monsters > 0) box.Children.Add(Note($"몬스터 처치 {monsters}건은 목록에 넣지 않았습니다.", top: 8));
        if (p.Unmatched != 0) box.Children.Add(Note($"처치와 연결하지 못한 획득 {Fmt.Signed(p.Unmatched)}", top: monsters > 0 ? 2 : 8));
        Detail.Children.Add(Card(box, new Thickness(0)));
        DetailScroll.ScrollToTop();
    }

    /// <summary>One of the two lists (kills or deaths): a heading with the count and the points, then a line for each.</summary>
    private StackPanel KillColumn(string title, Brush color, List<PeriodKill> items, string empty)
    {
        long points = items.Where(k => k.Kind != 3).Sum(k => k.Delta);
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"{title} {items.Count}건" + (points != 0 ? $" · {Fmt.Signed(points)}" : ""), FontSize = 12.5, FontWeight = FontWeights.SemiBold,
            Foreground = color, Margin = new Thickness(0, 0, 0, 4),
        });
        if (items.Count == 0) panel.Children.Add(Note(empty, top: 0));
        foreach (var k in items) panel.Children.Add(KillLine(k, color));
        return panel;
    }

    private FrameworkElement KillLine(PeriodKill k, Brush color)
    {
        // kinds: 0 kill, 1 death, 3 duel (points +1 / -1 = won / lost); the column says which side, so only a duel needs a word
        string duel = k.Kind == 3 ? (k.Delta > 0 ? "결투 승" : "결투 패") + "  " : "";
        string points = k.Kind == 3 ? "" : Fmt.Signed(k.Delta) + "  ";
        string who = k.Name.Length == 0 ? "" : k.Name + (k.Server > 0 ? $" ({Servers.Label(k.Server)})" : "");
        var line = new TextBlock
        {
            Text = $"{Kst.Of(DateTimeOffset.FromUnixTimeSeconds(k.Time)):HH:mm:ss}  {duel}{points}{who}", FontSize = 11.5, Foreground = color,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 2),
        };
        // the damage of this fight is kept on this PC (with the names): a click opens the same detail window as the kill list; an arrow shows that it can be opened
        if (!_localKills.TryGetValue((k.Time, k.Kind, k.Delta), out var local) || local.Damage is null && local.Attackers is not { Count: > 0 }) return line;
        var arrow = new TextBlock { Text = "›", FontSize = 15, Foreground = color, Margin = new Thickness(8, -2, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        var row = new DockPanel { Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = "클릭하면 준 피해·받은 피해를 자세히 봅니다" };
        DockPanel.SetDock(arrow, Dock.Right);
        row.Children.Add(arrow);
        row.Children.Add(line);
        row.MouseLeftButtonUp += (_, _) => OpenDamage(local);
        return row;
    }

    private readonly Dictionary<(long, int, long), KillRecord> _localKills = [];
    private KillDetailWindow? _detail;

    private void OpenDamage(KillRecord r)
    {
        bool death = r.Kind == KillKind.Death || r.Kind == KillKind.Duel && r.Delta <= 0;
        var d = r.Damage;
        var tally = new KillTally(r.Name, r.Server, r.Race, 1, r.Delta, r.Time, d?.Dealt ?? 0, d?.Received ?? 0,
            SkillDamage.Merge(null, d?.DealtBySkill), SkillDamage.Merge(null, d?.ReceivedBySkill),
            AttackerDamage.Merge(null, r.Attackers), [r with { Duel = r.Kind == KillKind.Duel || r.Duel }], d?.DealtHow, d?.ReceivedHow, SkillDamage.Merge(null, d?.HealBySkill));
        if (_detail is null || !_detail.IsLoaded)
            _detail = new KillDetailWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        _detail.Display(tally, death);
        _detail.Show();
        _detail.Activate();
    }

    private static string Explain(string flag) => flag switch
    {
        "net" => "합계가 맞지 않음",
        "gap-before" or "gap-after" => "이웃 구간과 총 포인트가 이어지지 않음",
        "gain-high" => "획득이 매우 큼",
        "offset" => "초기화 시각이 다른 사용자와 다름",
        "future" => "시각이 미래",
        _ => flag,
    };
}
