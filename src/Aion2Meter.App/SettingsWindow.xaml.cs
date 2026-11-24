using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using Aion2Meter.Core;
using Microsoft.Win32;

namespace Aion2Meter.App;

/// <summary>Settings and records. Closing only hides it; the app keeps running on the overlay and tray.</summary>
public partial class SettingsWindow : Window
{
    private readonly Controller _controller;
    private bool _loading = true;

    public SettingsWindow(Controller controller)
    {
        _controller = controller;
        InitializeComponent();
        var s = controller.Settings;
        AutoStart.IsChecked = s.AutoStart;
        AutoUpdate.IsChecked = s.AutoUpdate;
        Record.IsChecked = s.RecordPackets;
        Keep7.IsChecked = s.NameKeepDays == 7;
        Keep30.IsChecked = s.NameKeepDays == 30;
        KeepForever.IsChecked = s.NameKeepDays is not (7 or 30);
        ShowKillList.IsChecked = s.ShowKillList;
        DockKillList.IsChecked = s.KillListDocked;
        AlwaysOnTop.IsChecked = s.AlwaysOnTop;
        ShowLive.IsChecked = s.ShowLive;
        LiveRowsText.Text = s.LiveRows.ToString();
        OpacitySlider.Value = Math.Round(s.PanelOpacity * 100);
        PassOpacitySlider.Value = Math.Round(s.PassthroughOpacity * 100);
        BossVolumeSlider.Value = s.BossVolume;
        TargetVolumeSlider.Value = s.TargetVolume;
        TargetVolumeText.Text = $"{TargetVolumeSlider.Value:0}%";
        foreach (var (code, name) in PlayerClass.All)
        {
            var box = new CheckBox { Content = name, Tag = code, Margin = new Thickness(0, 3, 16, 3) };
            box.Click += OnTargetChanged;
            TargetClassPanel.Children.Add(box);
        }
        BossVolumeText.Text = $"{BossVolumeSlider.Value:0}%";
        OpacityText.Text = $"{OpacitySlider.Value:0}%";
        PassOpacityText.Text = $"{PassOpacitySlider.Value:0}%";
        KillOpacitySlider.Value = Math.Round((s.KillListOpacity ?? s.PanelOpacity) * 100);
        KillPassOpacitySlider.Value = Math.Round((s.KillListPassthroughOpacity ?? s.PassthroughOpacity) * 100);
        KillOpacityText.Text = $"{KillOpacitySlider.Value:0}%";
        KillPassOpacityText.Text = $"{KillPassOpacitySlider.Value:0}%";
        Hotkeys.IsChecked = s.Hotkeys;
        (s.ResetMode == ResetMode.RankingReset ? ModeRanking : ModeHour).IsChecked = true;
        ThresholdText.Text = $"{s.PinThreshold}회";
        Scroll.MaxHeight = SystemParameters.WorkArea.Height - 120;
        Version.Text = $"버전 {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)} · 데이터 {Paths.Root}";
        BuildHotkeyRows();
        ShowUpdateState(null);
        IsVisibleChanged += (_, _) => { if (IsVisible && UpdateButton.IsEnabled) ShowUpdateState(null); };
        _loading = false;
    }

    /// <summary>Button text and status line from the updater's state; <paramref name="result"/> is the last check's message.</summary>
    private void ShowUpdateState(string? result)
    {
        var updater = _controller.Updater;
        if (updater.ReadyVersion is { } ready)
        {
            UpdateButton.Content = "적용하고 다시 시작";
            UpdateStatus.Text = $"새 버전 {ready}을 내려받았습니다. (현재 {updater.Version})";
        }
        else
        {
            UpdateButton.Content = "업데이트 확인";
            UpdateStatus.Text = result ?? $"현재 버전 {updater.Version}";
        }
        NewNotesText.Visibility = updater.ReadyVersion is not null && !string.IsNullOrWhiteSpace(updater.ReadyNotes) ? Visibility.Visible : Visibility.Collapsed;
        UpdateButton.IsEnabled = updater.Installed;
        if (!updater.Installed) UpdateStatus.Text = $"현재 버전 {updater.Version} · 설치한 앱에서만 업데이트할 수 있습니다.";
    }

    private void OnShowRecords(object sender, RoutedEventArgs e) => _controller.ShowRecords();
    private void OnHelp(object sender, RoutedEventArgs e) => _controller.ShowManual();
    private void OnHelpWindows(object sender, RoutedEventArgs e) => _controller.ShowManual("구간 기록 보내기");
    private void OnHelpData(object sender, RoutedEventArgs e) => _controller.ShowManual("데이터 보내기");

    private void OnShowChanges(object sender, RoutedEventArgs e) => _controller.ShowAllChanges();

    private void OnShowNewNotes(object sender, RoutedEventArgs e) => _controller.ShowNewVersionNotes();

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        if (_controller.Updater.ReadyVersion is not null)
        {
            _controller.RestartForUpdate();
            return;
        }
        UpdateButton.IsEnabled = false;
        UpdateStatus.Text = "확인하는 중…";
        var result = await _controller.CheckUpdateAsync();
        ShowUpdateState(result);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    public void Refresh(DateTimeOffset now)
    {
        var c = _controller;
        DockKillList.IsChecked = c.Settings.KillListDocked;
        ShowKillList.IsChecked = c.Settings.ShowKillList;
        ShowLive.IsChecked = c.Settings.ShowLive;
        MeasureButton.Content = c.Session.Measuring ? "측정 중지" : "측정 시작";
        CaptureStatus.Text = c.StatusText(now);
        NpcapLink.Visibility = c.CaptureProblem is not null ? Visibility.Visible : Visibility.Collapsed;
        // Sending the window records ("구간 기록") needs the game's own hours: while it is on the basis is fixed to the ranking reset.
        bool pinned = c.Settings.UploadWindows;
        (c.Settings.ResetMode == ResetMode.RankingReset ? ModeRanking : ModeHour).IsChecked = true;
        ModeHour.IsEnabled = !pinned;
        ModeNote.Text = (c.Session.RankingOffset is int o
            ? $"랭킹 초기화: 매시 {o / 60:00}분 {o % 60:00}초 (랭킹 정보 기준)"
            : "랭킹 정보를 받으면 자동으로 맞춥니다 (그 전까지 정각 기준). 게임에서 랭킹 창을 한 번 열어 주세요.")
            + (pinned ? "\n구간 기록 보내기에 동의해서 랭킹 초기화 시각으로 고정되어 있습니다." : "");
        HotkeyNote.Text = _hotkeyError ?? c.HotkeyNote;
        HotkeyRows.IsEnabled = c.Settings.Hotkeys;
        RecordFolderText.Text = c.Settings.RecordFolder is { } folder ? $"저장 중: {folder}" + (c.RecordError is { } err ? $"\n{err}" : "") : "저장하지 않음 (기본)";
        RecordOffButton.IsEnabled = c.Settings.RecordFolder is not null;
        RecordOpenButton.IsEnabled = c.Settings.RecordFolder is not null;
        RecordingFolderText.Text = "녹화 저장 위치: " + c.EffectiveRecordingFolder + (c.Settings.RecordingFolder is null ? " (기본)" : "")
            + (c.RecordingNote is { } note ? "\n" + note : "");
        RecordingDefaultButton.IsEnabled = c.Settings.RecordingFolder is not null;
        UploadWindows.IsChecked = c.Settings.UploadWindows;
        UploadStatus.Text = _forgetResult ?? c.WindowStatusText();
        ForgetButton.IsEnabled = c.Settings.InstallId is not null;
        RefreshTarget();
        RefreshDebuff();
        RefreshBoss(now);
    }


    private static readonly string[] Pages = ["PageMeasure", "PageScreen", "PageBoss", "PageDebuff", "PageData", "PageInfo"];

    /// <summary>A category tab was picked: only its page is shown.</summary>
    private void OnTab(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string page }) return;
        foreach (var name in Pages)
            if (FindName(name) is StackPanel panel) panel.Visibility = name == page ? Visibility.Visible : Visibility.Collapsed;
        Scroll?.ScrollToTop();
    }

    private void OnBossChanged(object sender, RoutedEventArgs e)
    {
        bool alerts = BossAlerts.IsChecked == true, kaira = BossKaira.IsChecked == true, agro = BossAgro.IsChecked == true, sound = BossSound.IsChecked == true;
        var minutes = new List<int>();
        if (Boss10.IsChecked == true) minutes.Add(10);
        if (Boss5.IsChecked == true) minutes.Add(5);
        if (Boss1.IsChecked == true) minutes.Add(1);
        _controller.SetBossSettings(alerts, kaira, agro, sound, minutes.ToArray());
        RefreshBoss(DateTimeOffset.Now);
    }

    private void OnBossVolume(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BossVolumeText is null) return; // during InitializeComponent
        BossVolumeText.Text = $"{e.NewValue:0}%";
        if (_loading) return;
        _controller.SetBossVolume((int)e.NewValue);
    }

    /// <summary>Knob let go: the chime plays once at the new volume.</summary>
    private void OnBossVolumeDone(object sender, MouseButtonEventArgs e) => _controller.PlayBossPreview();

    private void OnBossTest(object sender, RoutedEventArgs e) => _controller.TestBossAlert();

    private void OnTargetChanged(object sender, RoutedEventArgs e)
    {
        var classes = TargetClassPanel.Children.OfType<CheckBox>().Where(b => b.IsChecked == true).Select(b => (int)b.Tag).ToArray();
        _controller.SetTargetAlert(TargetAlert.IsChecked == true, classes);
        RefreshTarget();
    }

    // The two debuff alerts have the same shape: a switch, a list of names (per class, then the others), a volume and a test.
    private sealed class DebuffSection(CheckBox enable, StackPanel panel, Slider slider, TextBlock volumeText)
    {
        public CheckBox Enable { get; } = enable;
        public StackPanel Panel { get; } = panel;
        public Slider Slider { get; } = slider;
        public TextBlock VolumeText { get; } = volumeText;
        public string Signature = "";
        // the groups of the list: header, its names and the box panel (a group is opened by a click on its header; it stays open when the list is built again)
        public List<(TextBlock Header, string Title, string[] Names, WrapPanel Wrap)> Groups { get; } = [];
        public HashSet<string> Open { get; } = [];
        public bool Built;
        public IEnumerable<CheckBox> Boxes => Panel.Children.OfType<WrapPanel>().SelectMany(p => p.Children.OfType<CheckBox>());
    }

    private DebuffSection? _mine, _aimed;
    private DebuffSection Mine => _mine ??= new DebuffSection(DebuffAlert, DebuffPanel, DebuffVolumeSlider, DebuffVolumeText);
    private DebuffSection Aimed => _aimed ??= new DebuffSection(TargetDebuffAlert, TargetDebuffPanel, TargetDebuffVolumeSlider, TargetDebuffVolumeText);

    private static string[] Chosen(DebuffSection s) => s.Boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToArray();

    private void OnDebuffChanged(object sender, RoutedEventArgs e)
    {
        _controller.SetDebuffAlert(Mine.Enable.IsChecked == true, Chosen(Mine));
        RefreshDebuff();
    }

    private void OnTargetDebuffChanged(object sender, RoutedEventArgs e)
    {
        _controller.SetTargetDebuffAlert(Aimed.Enable.IsChecked == true, Chosen(Aimed));
        RefreshDebuff();
    }

    /// <summary>
    /// The lists: the debuffs known by name, per class (<see cref="DebuffWatcher.Catalog"/>), and below them the others that were chosen (and, for the one on me, that landed on me since the start;
    /// new ones are added while the window is open).
    /// </summary>
    private void RefreshDebuff()
    {
        var s = _controller.Settings;
        Refresh(Mine, s.DebuffAlert, s.DebuffNames, s.DebuffVolume, _controller.DebuffsSeen, OnDebuffChanged);
        Refresh(Aimed, s.TargetDebuffAlert, s.TargetDebuffNames, s.TargetDebuffVolume, [], OnTargetDebuffChanged);
    }

    private void Refresh(DebuffSection section, bool enabled, string[] chosen, int volume, IEnumerable<string> seen, RoutedEventHandler changed)
    {
        section.Enable.IsChecked = enabled;
        var inCatalog = DebuffWatcher.CommonNames.ToHashSet();
        var others = chosen.Concat(seen).Where(n => !inCatalog.Contains(n)).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        string signature = string.Join(",", others);
        if (signature != section.Signature || section.Panel.Children.Count == 0)
        {
            section.Signature = signature;
            section.Panel.Children.Clear();
            section.Groups.Clear();
            void Group(string title, IEnumerable<string> names)
            {
                var list = names.ToArray();
                // the first time, a group with something chosen in it is open; the rest are closed (the list is long)
                if (!section.Built && list.Any(chosen.Contains)) section.Open.Add(title);
                var header = new TextBlock { Style = (Style)FindResource("Note"), Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, Tag = title };
                var wrap = new WrapPanel { Visibility = section.Open.Contains(title) ? Visibility.Visible : Visibility.Collapsed };
                header.MouseLeftButtonUp += (_, _) =>
                {
                    bool open = wrap.Visibility != Visibility.Visible;
                    wrap.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                    if (open) section.Open.Add(title); else section.Open.Remove(title);
                    HeaderText(section, header, title, list);
                };
                foreach (var name in list)
                {
                    var box = new CheckBox { Content = name, Tag = name, Margin = new Thickness(0, 3, 16, 3) };
                    box.Click += changed;
                    wrap.Children.Add(box);
                }
                section.Panel.Children.Add(header);
                section.Panel.Children.Add(wrap);
                section.Groups.Add((header, title, list, wrap));
            }
            foreach (var (group, names) in DebuffWatcher.Catalog) Group(group, names);
            if (others.Count > 0) Group(seen.Any() ? "그 밖에 (나에게 걸린 적이 있는 것 등)" : "그 밖에", others);
            section.Built = true;
        }
        foreach (var box in section.Boxes)
        {
            box.IsChecked = chosen.Contains((string)box.Tag);
            box.IsEnabled = enabled;
        }
        foreach (var (header, title, list, _) in section.Groups) HeaderText(section, header, title, list);
        section.Slider.IsEnabled = enabled;
        if (section.Slider.Value != volume) section.Slider.Value = volume;
    }

    /// <summary>"▸ 검성 (2/6)": closed or open, and how many of its names are chosen.</summary>
    private void HeaderText(DebuffSection section, TextBlock header, string title, string[] names)
    {
        int count = names.Count(n => section.Boxes.Any(b => (string)b.Tag == n && b.IsChecked == true));
        header.Text = (section.Open.Contains(title) ? "▾ " : "▸ ") + title + (count > 0 ? $" ({count}/{names.Length})" : $" ({names.Length})");
        header.Foreground = (System.Windows.Media.Brush)FindResource(count > 0 ? "AccentBrush" : "FaintBrush");
    }

    private void OnDebuffVolume(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DebuffVolumeText is null) return; // during InitializeComponent
        DebuffVolumeText.Text = $"{e.NewValue:0}%";
        if (_loading) return;
        _controller.SetDebuffVolume((int)e.NewValue);
    }

    private void OnDebuffVolumeDone(object sender, MouseButtonEventArgs e) => _controller.PlayDebuffPreview();

    private void OnDebuffTest(object sender, RoutedEventArgs e) => _controller.PlayDebuffPreview();

    private void OnTargetDebuffVolume(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TargetDebuffVolumeText is null) return; // during InitializeComponent
        TargetDebuffVolumeText.Text = $"{e.NewValue:0}%";
        if (_loading) return;
        _controller.SetTargetDebuffVolume((int)e.NewValue);
    }

    private void OnTargetDebuffVolumeDone(object sender, MouseButtonEventArgs e) => _controller.PlayTargetDebuffPreview();

    private void OnTargetDebuffTest(object sender, RoutedEventArgs e) => _controller.PlayTargetDebuffPreview();

    private void RefreshTarget()
    {
        var s = _controller.Settings;
        TargetAlert.IsChecked = s.TargetAlert;
        foreach (var box in TargetClassPanel.Children.OfType<CheckBox>())
        {
            box.IsChecked = s.TargetClasses.Contains((int)box.Tag);
            box.IsEnabled = s.TargetAlert;
        }
        TargetVolumeSlider.IsEnabled = s.TargetAlert;
    }

    private void OnTargetVolume(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TargetVolumeText is null) return; // during InitializeComponent
        TargetVolumeText.Text = $"{e.NewValue:0}%";
        if (_loading) return;
        _controller.SetTargetVolume((int)e.NewValue);
    }

    private void OnTargetVolumeDone(object sender, MouseButtonEventArgs e) => _controller.PlayTargetPreview();

    private void OnTargetTest(object sender, RoutedEventArgs e) => _controller.PlayTargetPreview();

    private static string Until(DateTimeOffset at, DateTimeOffset now)
    {
        var left = at - now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        return left.TotalHours >= 1 ? $"{(int)left.TotalHours}시간 {left.Minutes}분 후" : $"{left.Minutes}분 {left.Seconds}초 후";
    }

    private void RefreshBoss(DateTimeOffset now)
    {
        var c = _controller;
        BossAlerts.IsChecked = c.Settings.BossAlerts;
        BossKaira.IsChecked = c.Settings.BossKaira;
        BossAgro.IsChecked = c.Settings.BossAgro;
        BossSound.IsChecked = c.Settings.BossSound;
        Boss10.IsChecked = c.Settings.BossMinutes.Contains(10);
        Boss5.IsChecked = c.Settings.BossMinutes.Contains(5);
        Boss1.IsChecked = c.Settings.BossMinutes.Contains(1);
        BossKaira.IsEnabled = BossAgro.IsEnabled = BossSound.IsEnabled = Boss10.IsEnabled = Boss5.IsEnabled = Boss1.IsEnabled = BossVolumeSlider.IsEnabled = c.Settings.BossAlerts;
        var kaira = c.NextKaira(now);
        string text = $"감시자 카이라: {kaira.ToOffset(FieldBosses.GameOffset):MM-dd HH:mm} ({Until(kaira, now)})";
        var agro = c.NextAgro(now);
        text += $"\n정령왕 아그로: {agro.ToOffset(FieldBosses.GameOffset):MM-dd HH:mm} ({Until(agro, now)})";
        BossNext.Text = text + "\n(시각은 게임 시간 기준)";
    }
    private string? _forgetResult;

    private void OnUploadWindows(object sender, RoutedEventArgs e)
    {
        // Read the box before the question: the settings window refreshes itself every second, also while the question is open,
        // and would put the box back to the saved (unchecked) value before the answer is looked at.
        bool on = UploadWindows.IsChecked == true;
        if (on)
        {
            var answer = MessageBox.Show(this,
                "한 시간이 끝날 때마다 그 시간의 랭킹 포인트 변화와 처치·사망 기록(시각, 종류, 포인트, 상대의 서버)을 내 캐릭터 이름·서버와 함께 서버로 보냅니다.\n상대 캐릭터의 이름은 보내지 않고, 이 PC에만 남습니다 (기본은 계속, 설정에서 7일·30일로 바꿀 수 있습니다).\n측정 중에 포인트 변화가 없던 구간은 보내지 않습니다.\n동의하면 집계 기준이 게임의 랭킹 초기화 시각으로 고정됩니다.\n\n서버의 기록은 보낸 이 설치(앱)만 읽을 수 있고, 캐릭터 이름과 서버만으로는 조회되지 않습니다. 나중에 랭킹이나 웹사이트에 보여 주게 되면 그 전에 다시 알리고 동의를 받습니다.\n언제든 끄고 이 설치가 보낸 기록을 지울 수 있습니다.\n\n개인정보 처리방침: https://github.com/A2Rank/releases/blob/main/PRIVACY.md (설정 창에서도 볼 수 있습니다)\n\n보내는 데 동의하시겠습니까?",
                "구간 기록 보내기", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                UploadWindows.IsChecked = false;
                return;
            }
        }
        _forgetResult = null;
        _controller.SetUploadWindows(on);
    }

    private async void OnForgetWindows(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, "서버에 보낸 내 구간 기록을 모두 지우고, 보내기를 끕니다.\n계속하시겠습니까?", "보낸 기록 삭제",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        ForgetButton.IsEnabled = false;
        UploadStatus.Text = "지우는 중…";
        _forgetResult = await _controller.ForgetWindowsAsync() ?? "서버의 기록을 지웠고 보내기를 껐습니다.";
        Refresh(DateTimeOffset.Now);
    }


    private string? _hotkeyError;

    /// <summary>One row per action: name, a box that records the next key combination, and a clear button.</summary>
    private void BuildHotkeyRows()
    {
        static string Show(string gesture) => gesture.Length == 0 ? "없음" : gesture;
        foreach (var action in HotkeyActions.All)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            var clear = new Button { Content = "지우기", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 1, 8, 1) };
            var box = new TextBox { Width = 150, IsReadOnly = true, IsReadOnlyCaretVisible = false, Cursor = Cursors.Hand,
                                    Text = Show(_controller.Settings.Binding(action)), TextAlignment = TextAlignment.Center };
            DockPanel.SetDock(clear, Dock.Right);
            DockPanel.SetDock(box, Dock.Right);
            row.Children.Add(clear);
            row.Children.Add(box);
            row.Children.Add(new TextBlock { Text = HotkeyActions.Label(action), VerticalAlignment = VerticalAlignment.Center });

            box.GotKeyboardFocus += (_, _) =>
            {
                _controller.SuspendHotkeys();
                box.Text = "키를 누르세요…";
            };
            box.LostKeyboardFocus += (_, _) =>
            {
                box.Text = Show(_controller.Settings.Binding(action));
                _controller.ResumeHotkeys();
                Refresh(DateTimeOffset.Now);
            };
            box.PreviewKeyDown += (_, e) =>
            {
                e.Handled = true;
                if ((e.Key == Key.System ? e.SystemKey : e.Key) == Key.Escape)
                {
                    Keyboard.ClearFocus();
                    return;
                }
                if (Gesture.FromKeyEvent(e) is not { } g) return; // only modifiers so far
                if (!g.IsUsable)
                {
                    _hotkeyError = "Ctrl·Alt·Shift·Win 중 하나와 함께 누르세요 (F1~F12는 단독 가능).";
                    HotkeyNote.Text = _hotkeyError;
                    return;
                }
                _hotkeyError = _controller.SetHotkey(action, g.ToString());
                Keyboard.ClearFocus();
            };
            clear.Click += (_, _) =>
            {
                _hotkeyError = _controller.SetHotkey(action, "");
                box.Text = Show(_controller.Settings.Binding(action));
                Refresh(DateTimeOffset.Now);
            };
            HotkeyRows.Children.Add(row);
        }
    }

    private void OnRecordFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "구간 기록을 저장할 폴더" };
        if (_controller.Settings.RecordFolder is { } current && Directory.Exists(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog(this) == true) _controller.SetRecordFolder(dialog.FolderName);
    }

    private SendDataWindow? _sendData;

    private void OnSendData(object sender, RoutedEventArgs e)
    {
        if (_sendData is { IsLoaded: true }) { _sendData.Activate(); return; }
        _sendData = new SendDataWindow(_controller) { Owner = this };
        _sendData.Closed += (_, _) => { _sendData = null; Record.IsChecked = _controller.Settings.RecordPackets; };
        _sendData.Show();
    }

    private void OnRecordOff(object sender, RoutedEventArgs e) => _controller.SetRecordFolder(null);

    private async void OnRecordingFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "원시 패킷 녹화를 저장할 폴더" };
        if (Directory.Exists(_controller.EffectiveRecordingFolder)) dialog.InitialDirectory = _controller.EffectiveRecordingFolder;
        if (dialog.ShowDialog(this) == true) await ChangeRecordingFolder(dialog.FolderName);
    }

    private async void OnRecordingDefault(object sender, RoutedEventArgs e) => await ChangeRecordingFolder(null);

    /// <summary>Switches the recording folder and offers to move the recordings already made into the new one.</summary>
    private async Task ChangeRecordingFolder(string? folder)
    {
        var old = _controller.EffectiveRecordingFolder;
        if (_controller.SetRecordingFolder(folder) is { } problem)
        {
            MessageBox.Show(this, $"이 폴더에는 저장할 수 없습니다.\n{problem}", "원시 패킷 녹화", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var target = _controller.EffectiveRecordingFolder;
        var (count, bytes) = RecordingFolder.Measure(old);
        if (count > 0 && !string.Equals(Path.GetFullPath(old).TrimEnd('\\'), Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            var ask = MessageBox.Show(this,
                $"이전 폴더에 있는 녹화 {count}개({bytes / 1048576.0:0.#} MB)도 새 폴더로 옮길까요?\n\n{old}\n→ {target}\n\n(지금 쓰고 있는 녹화는 옮기지 않고 그대로 둡니다.)",
                "원시 패킷 녹화 옮기기", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask == MessageBoxResult.Yes)
            {
                RecordingFolderText.Text = "녹화를 옮기는 중…";
                var (moved, skipped) = await Task.Run(() => RecordingFolder.MoveRecordings(old, target));
                MessageBox.Show(this, $"{moved}개를 옮겼습니다." + (skipped > 0 ? $"\n{skipped}개는 사용 중이거나 옮기지 못해 그대로 두었습니다." : ""),
                    "원시 패킷 녹화 옮기기", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        Refresh(DateTimeOffset.Now);
    }

    private void OnMeasure(object sender, RoutedEventArgs e)
    {
        if (_controller.Session.Measuring) _controller.StopMeasuring();
        else _controller.StartMeasuring();
    }

    private void OnKeepDays(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _controller.Settings.NameKeepDays = Keep7.IsChecked == true ? 7 : Keep30.IsChecked == true ? 30 : 0;
        _controller.ForgetOldNames();
        _controller.SettingsChanged();
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = _controller.Settings;
        s.AutoStart = AutoStart.IsChecked == true;
        s.AutoUpdate = AutoUpdate.IsChecked == true;
        s.RecordPackets = Record.IsChecked == true;
        s.ShowKillList = ShowKillList.IsChecked == true;
        s.AlwaysOnTop = AlwaysOnTop.IsChecked == true;
        s.KillListDocked = DockKillList.IsChecked == true;
        s.ShowLive = ShowLive.IsChecked == true;
        s.Hotkeys = Hotkeys.IsChecked == true;
        _controller.SettingsChanged();
    }

    private void OnOpacity(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityText is null || PassOpacityText is null) return; // during InitializeComponent
        bool passthrough = sender == PassOpacitySlider;
        (passthrough ? PassOpacityText : OpacityText).Text = $"{e.NewValue:0}%";
        if (_loading) return;
        _controller.SetPanelOpacity(e.NewValue / 100, passthrough);
    }

    private void OnKillListOpacity(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (KillOpacityText is null || KillPassOpacityText is null) return; // during InitializeComponent
        bool passthrough = sender == KillPassOpacitySlider;
        (passthrough ? KillPassOpacityText : KillOpacityText).Text = $"{e.NewValue:0}%";
        if (_loading) return;
        _controller.SetKillListOpacity(e.NewValue / 100, passthrough);
    }

    private void OnMode(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        // Fixed while the window records are sent (the disabled button cannot be clicked; a shortcut key could still reach it).
        var mode = ModeRanking.IsChecked == true || _controller.Settings.UploadWindows ? ResetMode.RankingReset : ResetMode.OnTheHour;
        _controller.SetResetMode(mode);
        Refresh(DateTimeOffset.Now);
    }

    private void OnThresholdDown(object sender, RoutedEventArgs e) => SetThreshold(_controller.Settings.PinThreshold - 1);
    private void OnThresholdUp(object sender, RoutedEventArgs e) => SetThreshold(_controller.Settings.PinThreshold + 1);

    private void OnLiveRowsDown(object sender, RoutedEventArgs e) => SetLiveRows(_controller.Settings.LiveRows - 1);
    private void OnLiveRowsUp(object sender, RoutedEventArgs e) => SetLiveRows(_controller.Settings.LiveRows + 1);

    private void SetLiveRows(int n)
    {
        _controller.Settings.LiveRows = Math.Clamp(n, 3, 40);
        LiveRowsText.Text = _controller.Settings.LiveRows.ToString();
        _controller.SettingsChanged();
    }

    private void SetThreshold(int n)
    {
        _controller.Settings.PinThreshold = Math.Clamp(n, 2, 10);
        ThresholdText.Text = $"{_controller.Settings.PinThreshold}회";
        _controller.SettingsChanged();
    }

    private void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnClose(object sender, RoutedEventArgs e) => Hide();

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Paths.Root);
        Process.Start(new ProcessStartInfo(Paths.Root) { UseShellExecute = true });
    }

    /// <summary>Opens the folder the record files (CSV/TXT) are saved in.</summary>
    private void OnOpenRecordFolder(object sender, RoutedEventArgs e)
    {
        if (_controller.Settings.RecordFolder is not { } folder) return;
        try
        {
            if (!Directory.Exists(folder))
            {
                // A drive that is not there (or a folder that was deleted): say so instead of opening nothing.
                MessageBox.Show(this, $"저장 폴더를 찾을 수 없습니다.\n{folder}\n\n폴더가 있는 드라이브가 연결되어 있는지 확인하거나 [폴더 선택…]으로 다시 골라 주세요.",
                    "기록 파일", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Log.Write("open record folder: " + ex.Message);
            MessageBox.Show(this, "폴더를 열지 못했습니다: " + ex.Message, "기록 파일", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private ContactWindow? _contact;

    private void OnContact(object sender, RoutedEventArgs e)
    {
        if (_contact is { IsLoaded: true }) { _contact.Activate(); return; }
        _contact = new ContactWindow { Owner = this };
        _contact.Closed += (_, _) => _contact = null;
        _contact.Show();
    }

    private void OnLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}

