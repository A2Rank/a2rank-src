using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Aion2Meter.Capture;
using Aion2Meter.Core;
using Aion2Meter.Protocol;
using Forms = System.Windows.Forms;

namespace Aion2Meter.App;

/// <summary>
/// Owns everything: capture threads only enqueue events; the session and the windows live on the UI thread.
/// </summary>
public sealed class Controller : IDisposable
{
    private const int HotkeyBase = 700;

    private readonly ConcurrentQueue<CaptureEvent> _queue = new();
    private readonly TargetWatcher _targets = new();
    private readonly DebuffWatcher _debuffs = new();
    private readonly CaptureService _capture = new();
    private readonly DispatcherTimer _pump = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly OverlayWindow _overlay;
    private readonly KillListWindow _killList;
    private readonly LiveCombatWindow _live;
    private readonly ReleaseWindow _release;
    private SettingsWindow? _settingsWindow;
    private Forms.NotifyIcon? _tray;
    public Updater Updater { get; } = new();
    private Forms.ToolStripMenuItem? _updateItem;
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    private readonly DispatcherTimer _windowTimer = new() { Interval = TimeSpan.FromMinutes(10) };
    private bool _sendingWindows;
    private DateTimeOffset? _windowsSentAt;
    private readonly List<int> _hotkeys = [];
    private bool _dirty = true, _passthrough, _overlayVisible = true, _disposed, _leaving;
    private readonly bool _firstRun;

    public Controller()
    {
        Directory.CreateDirectory(Paths.Root);
        _firstRun = !File.Exists(Paths.Settings);
        Settings = AppSettings.Load(Paths.Settings);
        if (Settings.UploadWindows) Settings.ResetMode = ResetMode.RankingReset;   // fixed while the window records are sent
        Store = OpenStore();
        Session = new MeterSession(Store, Settings.ResetMode, Settings.PinThreshold, DateTimeOffset.Now);
        Session.Changed += () => _dirty = true;
        Session.WindowClosed += OnWindowClosed;
        _capture.Events += events =>
        {
            foreach (var e in events)
            {
                _queue.Enqueue(e);
                if (e is MessageEvent { Message: var m } && _targets.Observe(m, e.Time) is { } who) OnTargeted(who);   // at once, not in the 100 ms timer
            }
        };
        _overlay = new OverlayWindow(this);
        _killList = new KillListWindow { Width = Settings.KillListWidth, Height = Settings.KillListHeight };
        _live = new LiveCombatWindow { Width = Settings.LiveWidth, Height = Settings.LiveHeight };
        _release = new ReleaseWindow(() => SetPassthrough(false));
    }

    public AppSettings Settings { get; }
    public MeterStore Store { get; }
    public MeterSession Session { get; }
    public string? CaptureProblem { get; private set; }
    public string HotkeyNote { get; private set; } = "";

    /// <summary>
    /// Opens the database. A damaged one (after a power cut, say) must not keep the app from starting for good: it is set aside with the time in its name
    /// (the records in it are lost to the app, the files stay for a look) and a new one is made.
    /// </summary>
    private static MeterStore OpenStore()
    {
        try { return new MeterStore(Paths.Database); }
        catch (Microsoft.Data.Sqlite.SqliteException e)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            Log.Write($"the database could not be opened ({e.Message}): set aside as *.broken-{stamp}, starting a new one");
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var path in new[] { Paths.Database, Paths.Database + "-wal", Paths.Database + "-shm" })
                if (File.Exists(path)) File.Move(path, path + ".broken-" + stamp);
            return new MeterStore(Paths.Database);
        }
    }

    public void Run()
    {
        var area = SystemParameters.WorkArea;
        _overlay.Left = Settings.OverlayLeft ?? area.Right - _overlay.Width - 24;
        _overlay.Top = Settings.OverlayTop ?? area.Top + 80;
        _overlay.LocationChanged += (_, _) => PlaceKillList();
        _overlay.SizeChanged += (_, _) => PlaceKillList();
        _killList.Dragged += () =>
        {
            Settings.KillListDocked = false;
            (Settings.KillListLeft, Settings.KillListTop) = (_killList.Left, _killList.Top);
            SaveSettings();
            if (_settingsWindow?.IsVisible == true) _settingsWindow.Refresh(DateTimeOffset.Now);
        };
        _killList.SizeChanged += (_, _) => (Settings.KillListWidth, Settings.KillListHeight) = (_killList.Width, _killList.Height);
        _live.Dragged += () =>
        {
            (Settings.LiveLeft, Settings.LiveTop) = (_live.Left, _live.Top);
            SaveSettings();
        };
        _live.SizeChanged += (_, _) => (Settings.LiveWidth, Settings.LiveHeight) = (_live.Width, _live.Height);
        _live.SortChanged += sort =>
        {
            Settings.LiveSort = (int)sort;
            SaveSettings();
            Refresh(force: true);
        };
        _live.Reset += () =>
        {
            Session.Matcher.Damage.ClearLive();
            Refresh(force: true);
        };
        _live.ShowSort((LiveSort)Math.Clamp(Settings.LiveSort, 0, 2));
        WindowPrefs.AlwaysOnTop = Settings.AlwaysOnTop;
        ApplyPanelOpacity();
        _overlay.Show();
        ApplyTopmost();
        UpdateKillListVisibility();
        UpdateLiveVisibility();

        _pump.Tick += (_, _) => Pump();
        _clock.Tick += (_, _) =>
        {
            if (PreviewNow is not null) { _dirty = true; return; }   // a replayed recording stays as it ended
            Session.Tick(DateTimeOffset.Now);
            _capture.Tick();
            CheckBossAlerts(DateTimeOffset.Now);
            ExportRecords();
            if (++_recordingTicks % 15 == 0) KeepRecordingsSmall();
            if (_recordingTicks % 3600 == 0) ForgetOldNames();
            _dirty = true;
        };
        _pump.Start();
        _clock.Start();
        CreateTray();
        RegisterHotkeys();
        _updateTimer.Tick += async (_, _) =>
        {
            if (Settings.AutoUpdate) await CheckForUpdate(manual: false);
        };
        _updateTimer.Start();
        ForgetOldNames();   // at start too: the app is not always open for an hour
        _windowTimer.Tick += async (_, _) => await SendWindowsAsync();
        _windowTimer.Start();
        _ = SendWindowsAsync();   // windows left over from the last run
        _ = FirstUpdateCheck();
        _ = ShowWhatsNew();
        if (Settings.AutoStart) StartMeasuring();
        Refresh();
    }

    public void StartMeasuring()
    {
        var now = DateTimeOffset.Now;
        CaptureProblem = CaptureService.Problem();
        if (CaptureProblem is null)
        {
            try
            {
                if (_capture.Start(RecordingDirectory()) == 0)
                    CaptureProblem = "캡처할 수 있는 네트워크 장치가 없습니다.";
            }
            catch (Exception e)
            {
                Log.Write("capture start: " + e);
                CaptureProblem = "캡처를 시작하지 못했습니다: " + e.Message;
            }
        }
        if (CaptureProblem is null)
        {
            Session.Start(now);
            KeepRecordingsSmall();   // a folder that was already over the limit is brought back before more is written
        }
        else ShowSettings();
        Refresh();
    }

    public void StopMeasuring()
    {
        _capture.Stop();
        Pump();
        Session.Stop(DateTimeOffset.Now);
        Refresh();
    }

    public string StatusText(DateTimeOffset now)
    {
        if (CaptureProblem is not null) return CaptureProblem;
        if (!Session.Measuring) return "측정 중지";
        string text = Session.LastMessage is { } last && now - last < TimeSpan.FromSeconds(30)
            ? "측정 중 · 게임 신호 수신"
            : "측정 중 · 게임 신호 대기";
        if (Session.Points.Total is null) text += " · 기준값 대기";
        if (_capture.RecordingPath is not null) text += " · 녹화 중";
        else if (_capture.RecordingArmed) text += " · 전투 녹화 대기";
        return text;
    }

    /// <summary>Color of the overlay status dot: problem, stopped, waiting for the game, receiving.</summary>
    public string StatusBrush(DateTimeOffset now) =>
        CaptureProblem is not null ? "LossBrush"
        : !Session.Measuring ? "FaintBrush"
        : Session.LastMessage is { } last && now - last < TimeSpan.FromSeconds(30) ? "AccentBrush"
        : "WaitBrush";

    public void SetPassthrough(bool on)
    {
        _passthrough = on;
        ApplyPanelOpacity();
        _overlay.SetPassthrough(on);
        Native.SetClickThrough(_killList, on);
        Native.SetClickThrough(_live, on);
        if (on && _overlay.IsVisible)
        {
            _release.Place(_overlay.PassButtonBounds);
            _release.Show();
        }
        else _release.Hide();
    }

    public void ToggleOverlay()
    {
        _overlayVisible = !_overlayVisible;
        if (_overlayVisible)
        {
            _overlay.Show();
            SetPassthrough(_passthrough);
        }
        else
        {
            _overlay.Hide();
            _release.Hide();
        }
        UpdateKillListVisibility();
        UpdateLiveVisibility();
    }

    /// <summary>Applies the panel background opacity live; saved with the settings.</summary>
    public void SetPanelOpacity(double opacity, bool passthrough)
    {
        if (passthrough) Settings.PassthroughOpacity = Math.Clamp(opacity, 0.2, 1);
        else Settings.PanelOpacity = Math.Clamp(opacity, 0.2, 1);
        ApplyPanelOpacity();
        SaveSettings();
    }

    /// <summary>The kill list's own opacity; a value of null in the settings means "same as the overlay".</summary>
    public void SetKillListOpacity(double opacity, bool passthrough)
    {
        opacity = Math.Clamp(opacity, 0.2, 1);
        if (passthrough) Settings.KillListPassthroughOpacity = opacity;
        else Settings.KillListOpacity = opacity;
        ApplyPanelOpacity();
        SaveSettings();
    }

    private void ApplyPanelOpacity()
    {
        var color = ((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("PanelBaseBrush")).Color;
        double overlay = _passthrough ? Settings.PassthroughOpacity : Settings.PanelOpacity;
        double killList = (_passthrough ? Settings.KillListPassthroughOpacity : Settings.KillListOpacity) ?? overlay;
        Application.Current.Resources["PanelBrush"] = PanelBrush(color, overlay);
        Application.Current.Resources["KillListPanelBrush"] = PanelBrush(color, killList);
    }

    private static System.Windows.Media.SolidColorBrush PanelBrush(System.Windows.Media.Color color, double opacity)
    {
        color.A = (byte)Math.Round(Math.Clamp(opacity, 0.2, 1) * 255);
        var brush = new System.Windows.Media.SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Development: sample data shown in the kill list instead of the real one (--preview-kills).</summary>
    public HourlyKillList? PreviewKills { get; set; }

    /// <summary>Development: the time a replayed recording ended; the windows show the state at that moment (--preview-replay).</summary>
    public DateTimeOffset? PreviewNow { get; private set; }

    /// <summary>Development (--preview-replay file.pcap): feeds a recording through the session, as if it had just been played; use with A2RANK_DATA (a separate data folder).</summary>
    public void ReplayRecording(string path, TimeSpan? untilKst = null)
    {
        var frames = Aion2Meter.Capture.PcapReader.Read(path).ToList();
        if (frames.Count == 0) return;
        var pipeline = new Pipeline();
        DateTimeOffset tick = default, last = frames[0].Time;
        Session.Start(frames[0].Time);
        foreach (var f in frames)
            foreach (var e in pipeline.Feed(f.Data, f.LinkType, f.Time))
            {
                if (untilKst is { } u && Kst.Of(e.Time).TimeOfDay > u) goto done;
                Session.Handle(e);
                last = e.Time;
                if (e.Time - tick >= TimeSpan.FromSeconds(1)) { Session.Tick(e.Time); tick = e.Time; }
            }
    done:
        PreviewNow = last;
        _dirty = true;
    }

    public string? RecordError { get; private set; }

    // ---- Window records ("구간 기록"): finished hours of the game's ranking clock are queued and sent in batches. ----

    /// <summary>Why the last sending failed, null when it did not.</summary>
    public string? WindowsProblem { get; private set; }

    /// <summary>A counted period ended: kept to be sent (with the consent) and to be written to the record files (with a folder chosen).</summary>
    private void OnWindowClosed(WindowRecord w)
    {
        bool send = Settings.UploadWindows, export = Settings.RecordFolder is not null;
        Store.AddWindow(w.CharacterId, w.Start, w.End, w.ToJson(), send, export);
        if (send) _ = SendWindowsLaterAsync();
        if (export) ExportRecords();
    }

    /// <summary>Every app closes its window at the same moment (the ranking reset): sending at a random time within 3 minutes spreads the load on the server.</summary>
    private async Task SendWindowsLaterAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(0, 180)));
        await SendWindowsAsync();
    }

    public async Task SendWindowsAsync()
    {
        if (_sendingWindows || !Settings.UploadWindows || Settings.InstallId is not { } install) return;
        _sendingWindows = true;
        try
        {
            while (Settings.UploadWindows)
            {
                var pending = Store.PendingWindows(WindowUploader.BatchSize);
                if (pending.Count == 0) break;
                var problem = await WindowUploader.SendAsync(install, pending.Select(p => p.Json).ToList(), CancellationToken.None);
                if (problem is not null)
                {
                    WindowsProblem = problem;   // tried again with the next window or in 10 minutes
                    return;
                }
                Store.MarkWindowsSent(pending.Select(p => p.Id));
                _windowsSentAt = DateTimeOffset.Now;
                WindowsProblem = null;
            }
            if (Settings.NameKeepDays > 0) Store.PruneWindows(DateTimeOffset.Now.AddDays(-14));   // for good: the copies are the history shown in "내 기록"
        }
        finally
        {
            _sendingWindows = false;
            _dirty = true;
        }
    }

    /// <summary>The characters this PC knows (name and server; the one being played first): the server is asked for their periods.</summary>
    public IReadOnlyList<(string Name, int Server)> KnownCharacters()
    {
        var found = new List<(string, int)>();
        if (Session.Character is { Server: not 0 } now) found.Add((now.Name, now.Server));
        foreach (var id in Store.Characters())
            if (CharacterKey.Parse(id) is { Server: not 0 } key && !found.Contains((key.Name, key.Server))) found.Add((key.Name, key.Server));
        return found.Take(10).ToList();
    }

    private ManualWindow? _manual;

    /// <summary>Opens the user manual (docs/MANUAL.md, embedded), at the section whose title contains <paramref name="section"/> when given.</summary>
    public void ShowManual(string? section = null)
    {
        if (_manual is not { IsLoaded: true })
        {
            using var stream = typeof(Controller).Assembly.GetManifestResourceStream("manual");
            var sections = stream is null ? [] : Manual.Parse(new StreamReader(stream).ReadToEnd());
            _manual = new ManualWindow(sections);
            _manual.Closed += (_, _) => _manual = null;
            _manual.Show();
        }
        _manual.Activate();
        if (section is not null) _manual.GoToSection(section);
    }

    private RecordsWindow? _myRecords;

    /// <summary>Opens "내 기록": what the server holds for this installation (or the copies on this PC when it cannot be reached).</summary>
    public void ShowRecords()
    {
        if (_myRecords is { IsLoaded: true })
        {
            _myRecords.Activate();
            return;
        }
        _myRecords = new RecordsWindow(this);
        _myRecords.Closed += (_, _) => _myRecords = null;
        _myRecords.Show();
        _myRecords.Activate();
    }

    public void SetUploadWindows(bool on)
    {
        Settings.UploadWindows = on;
        if (on)
        {
            Settings.InstallId ??= Guid.NewGuid().ToString("N");
            // The window records follow the game's own hours (the complete ones are what a ranking can check): fixed to the ranking reset.
            if (Settings.ResetMode != ResetMode.RankingReset) SetResetMode(ResetMode.RankingReset);
        }
        SaveSettings();
        Refresh();
        if (on) _ = SendWindowsAsync();
    }

    /// <summary>Consent withdrawn: stops sending, asks the server to delete everything of this installation, and forgets the queue.</summary>
    /// <returns>null when done, otherwise why the server could not be asked (then the id is kept to try again).</returns>
    public async Task<string?> ForgetWindowsAsync()
    {
        Settings.UploadWindows = false;
        SaveSettings();
        if (Settings.InstallId is { } install && await WindowUploader.ForgetAsync(install, CancellationToken.None) is { } problem)
            return problem + " 보낸 기록이 서버에 남아 있으니 나중에 다시 눌러 주세요.";
        Store.DeleteWindows();
        Settings.InstallId = null;
        SaveSettings();
        Refresh();
        return null;
    }

    public string WindowStatusText()
    {
        if (!Settings.UploadWindows) return "보내지 않습니다.";
        var parts = new List<string>();
        if (Session.RankingOffset is null) parts.Add("게임에서 랭킹 창을 한 번 열면 구간이 게임의 초기화 시각에 맞춰집니다.");
        parts.Add($"보낼 기록 {Store.PendingWindowCount()}개");
        if (_windowsSentAt is { } sent) parts.Add($"마지막 전송 {sent:HH:mm}");
        if (WindowsProblem is { } problem) parts.Add(problem);
        return string.Join(" · ", parts);
    }

    /// <summary>Finishes the recording file in progress and goes on in a new one (measuring continues); the closed file, or null.</summary>
    public string? CutRecording() => _capture.CutRecording();

    private int _recordingTicks;

    /// <summary>
    /// The other players' names stay on this PC as long as chosen (<see cref="AppSettings.NameKeepDays"/>: 7 or 30 days, or for good, the default):
    /// with a number of days, older kills and record copies are deleted; for good, only the oldest kills beyond <see cref="MeterStore.MaxKeptKills"/> go.
    /// </summary>
    public void ForgetOldNames()
    {
        try
        {
            if (Settings.NameKeepDays > 0)
            {
                var (kills, windows) = Store.PruneNames(DateTimeOffset.Now.AddDays(-Settings.NameKeepDays));
                if (kills + windows > 0) Log.Write($"names: deleted {kills} kills and {windows} window records older than {Settings.NameKeepDays} days");
            }
            else if (Store.TrimKills(MeterStore.MaxKeptKills) is > 0 and var trimmed) Log.Write($"names: {trimmed} oldest kills deleted (over {MeterStore.MaxKeptKills})");
        }
        catch (Microsoft.Data.Sqlite.SqliteException e)
        {
            Log.Write("names: " + e.Message);   // tried again in an hour
        }
    }

    /// <summary>
    /// While packets are recorded: a recording that has grown past <see cref="RecordingFolder.MaxFileBytes"/> is ended (a new file goes on), and the
    /// folder is kept within <see cref="RecordingFolder.LimitBytes"/> by deleting the oldest recordings. Never the one being written.
    /// </summary>
    private void KeepRecordingsSmall()
    {
        if (!Settings.RecordPackets) return;
        try
        {
            if (_capture.RecordingPath is { } current && File.Exists(current) && new FileInfo(current).Length > RecordingFolder.MaxFileBytes) CutRecording();
            var (deleted, freed) = RecordingFolder.Trim(EffectiveRecordingFolder, RecordingFolder.LimitBytes, _capture.RecordingPath);
            if (deleted > 0) Log.Write($"recordings: over {RecordingFolder.LimitBytes / 1048576} MB, deleted the {deleted} oldest ({freed / 1048576} MB)");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write("recordings: " + e.Message);   // tried again a little later
        }
    }

    /// <summary>Where raw packet recordings go: the chosen folder, or the default one in the app data folder.</summary>
    public string EffectiveRecordingFolder => Settings.RecordingFolder ?? Paths.Recordings;

    /// <summary>Set when the chosen recording folder could not be used at the last start and the default was used instead.</summary>
    public string? RecordingNote { get; private set; }

    /// <summary>The folder to record into for this measurement, or null when recording is off. A folder that cannot be used (a missing drive) never stops measuring.</summary>
    private string? RecordingDirectory()
    {
        RecordingNote = null;
        if (!Settings.RecordPackets) return null;
        var folder = EffectiveRecordingFolder;
        if (RecordingFolder.Check(folder) is null) return folder;
        Log.Write($"recording folder {folder}: not usable, using the default");
        RecordingNote = $"녹화 폴더({folder})를 쓸 수 없어 기본 위치에 저장했습니다.";
        return Paths.Recordings;
    }

    /// <summary>Records go to <paramref name="folder"/> (null = default) from the next measurement on.</summary>
    /// <returns>null when saved, otherwise why the folder cannot be used (nothing is changed then).</returns>
    public string? SetRecordingFolder(string? folder)
    {
        if (folder is not null && RecordingFolder.Check(folder) is { } problem) return problem;
        Settings.RecordingFolder = folder;
        RecordingNote = null;
        SettingsChanged();
        return null;
    }

    /// <summary>Start writing per-period records to <paramref name="folder"/> from now on, or stop (null).</summary>
    public void SetRecordFolder(string? folder)
    {
        Settings.RecordFolder = folder;
        RecordError = null;
        if (folder is null) Store.DiscardExports();   // turned off: what waited is not written later
        SettingsChanged();
    }

    /// <summary>Writes the periods that wait for the record files (also those left from before a restart). A folder that cannot be written is tried again on the next tick.</summary>
    private void ExportRecords()
    {
        if (Settings.RecordFolder is not { } folder) return;
        var pending = Store.PendingExports(200);
        if (pending.Count == 0) return;
        foreach (var (id, json) in pending)
        {
            try
            {
                WindowFiles.Append(folder, WindowRecord.FromJson(json));
                Store.MarkExported([id]);
                RecordError = null;
            }
            catch (System.Text.Json.JsonException e)
            {
                Log.Write("record file: unreadable record dropped: " + e.Message);
                Store.MarkExported([id]);   // nothing to be done with it
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                RecordError = "저장하지 못했습니다: " + e.Message;   // retried on the next tick
                return;
            }
        }
    }

    public void ToggleKillList()
    {
        Settings.ShowKillList = !Settings.ShowKillList;
        SettingsChanged();
    }

    public void ToggleLive()
    {
        Settings.ShowLive = !Settings.ShowLive;
        SettingsChanged();
    }

    public void ShowSettings()
    {
        _settingsWindow ??= new SettingsWindow(this);
        _settingsWindow.Refresh(DateTimeOffset.Now);
        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    public void SetResetMode(ResetMode mode)
    {
        Settings.ResetMode = mode;
        Session.SetMode(mode, DateTimeOffset.Now);
        SettingsChanged();
    }

    public void SettingsChanged()
    {
        ApplyTopmost();
        Session.Kills.Threshold = Settings.PinThreshold;
        UpdateKillListVisibility();
        UpdateLiveVisibility();
        RegisterHotkeys();
        SaveSettings();
        Refresh();
    }

    public void Exit()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _updateTimer.Stop();
        _windowTimer.Stop();
        _pump.Stop();
        _clock.Stop();
        _capture.Dispose();
        Pump();
        Session.Shutdown(DateTimeOffset.Now);   // the period in progress becomes a record before the database closes
        SaveSettings();
        UnregisterHotkeys();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        Store.Dispose();
        // A downloaded update is installed after a normal exit (not when restarting for it or uninstalling).
        if (!_leaving) Updater.ApplyOnExit();
    }

    private void Pump()
    {
        while (_queue.TryDequeue(out var e))
        {
            try
            {
                Session.Handle(e);
                if (e is MessageEvent { Message: var message } && _debuffs.Observe(message, Session.Matcher.SelfEntity, e.Time, Session.Matcher.IsEnemy, Settings.TargetDebuffAlert ? _targets.Aimed : (uint?)null) is { } debuff) OnDebuff(debuff, _debuffs.LastOnMe);
            }
            catch (Exception ex)
            {
                Log.Write("event: " + ex);
            }
        }
        if (_dirty) Refresh();
    }

    private void Refresh(bool force = false)
    {
        _dirty = false;
        var now = PreviewNow ?? DateTimeOffset.Now;
        _overlay.Display(Session, now, StatusText(now), StatusBrush(now));
        _killList.Display(PreviewKills ?? Session.Kills);
        if (Settings.ShowLive && _overlayVisible) _live.Display(Session.Matcher.Live(now), (LiveSort)Math.Clamp(Settings.LiveSort, 0, 2), Math.Clamp(Settings.LiveRows, 3, 40), force);
        if (_settingsWindow?.IsVisible == true) _settingsWindow.Refresh(now);
        if (_tray is not null) _tray.Text = $"A2Rank · 이번 구간 {Fmt.Signed(Session.Buckets.Current?.Net ?? 0)}";
    }

    /// <summary>Under the overlay while docked, otherwise where the user left it.</summary>
    private void PlaceKillList()
    {
        if (Settings.KillListDocked || Settings.KillListLeft is null || Settings.KillListTop is null) _killList.DockUnder(_overlay);
        else (_killList.Left, _killList.Top) = (Settings.KillListLeft.Value, Settings.KillListTop.Value);
    }

    /// <summary>The windows of the meter above everything else, or not (settings, "항상 맨 위에 표시"); the "해제" button of the click-through mode always stays on top.</summary>
    private void ApplyTopmost()
    {
        WindowPrefs.AlwaysOnTop = Settings.AlwaysOnTop;
        foreach (Window w in Application.Current.Windows)
            if (w is OverlayWindow or KillListWindow or LiveCombatWindow or KillDetailWindow) w.Topmost = Settings.AlwaysOnTop;
    }

    private void UpdateKillListVisibility()
    {
        _overlay.SetKillListShown(Settings.ShowKillList);
        if (Settings.ShowKillList && _overlayVisible)
        {
            _killList.Show();
            PlaceKillList();
            Native.SetClickThrough(_killList, _passthrough);
        }
        else _killList.Hide();
    }

    private void UpdateLiveVisibility()
    {
        _overlay.SetLiveShown(Settings.ShowLive);
        if (Settings.ShowLive && _overlayVisible)
        {
            if (!_live.IsVisible)
            {
                var area = SystemParameters.WorkArea;
                _live.Left = Settings.LiveLeft ?? Math.Max(area.Left, _killList.Left - _live.Width - 6);   // by default next to the kill list, on its left
                _live.Top = Settings.LiveTop ?? _killList.Top;
                _live.Show();
            }
            Native.SetClickThrough(_live, _passthrough);
            Refresh(force: true);
        }
        else _live.Hide();
    }

    // ---- field boss alerts: 감시자 카이라 and 정령왕 아그로, both at fixed game hours (FieldBosses)

    private readonly BossAlerts _bossAlerts = new();

    public DateTimeOffset NextKaira(DateTimeOffset now) => FieldBosses.NextKaira(now);

    public DateTimeOffset NextAgro(DateTimeOffset now) => FieldBosses.NextAgro(now);

    private void CheckBossAlerts(DateTimeOffset now)
    {
        if (!Settings.BossAlerts) return;
        var due = _bossAlerts.Poll(
        [
            ("감시자 카이라", Settings.BossKaira ? NextKaira(now) : null),
            ("정령왕 아그로", Settings.BossAgro ? NextAgro(now) : null),
        ], now, Settings.BossMinutes);
        foreach (var alert in due) ShowBossAlert(alert);
    }

    /// <summary>Banner on the main panel, a sound, and (panel hidden) a tray balloon.</summary>
    private void ShowBossAlert(BossAlert alert)
    {
        Log.Write("boss alert: " + alert.Text);
        _overlay.ShowBossBanner(alert.Text);
        if (!_overlayVisible) _tray?.ShowBalloonTip(8000, "필드보스", alert.Text, Forms.ToolTipIcon.Info);
        if (!Settings.BossSound) return;
        BossSound.Play(alert.MinutesBefore <= 1 ? 2 : 1, Settings.BossVolume);   // the last-minute alert rings twice
    }


    /// <summary>I aimed at a player: a short tone when the class is one the user chose (settings, "조준 알림"). Called on the packet thread as soon as the message arrives: only a sound is played.</summary>
    private void OnTargeted(TargetedPlayer who)
    {
        if (Settings.TargetAlert && who.ClassCode == 0 && who.Id <= ushort.MaxValue)   // (monsters have bigger numbers: not worth a line)
            Log.Write($"target alert: class unknown for {who.Id} (identity messages seen {_targets.Identities}, biggest number {_targets.MaxSeen}, targets {_targets.Targets})");
        if (!Settings.TargetAlert || who.ClassCode == 0 || !Settings.TargetClasses.Contains(who.ClassCode)) return;
        Log.Write($"target alert: {PlayerClass.Name(who.ClassCode)} ({who.Id})");
        BossSound.PlayTarget(Settings.TargetVolume);
    }

    /// <summary>A damaging effect of another player landed on me: a short tone when it is one the user chose (settings, "디버프 알림").</summary>
    private void OnDebuff(string name, bool onMe)
    {
        if (onMe)
        {
            if (!Settings.DebuffAlert || !Settings.DebuffNames.Contains(name)) return;
            Log.Write($"debuff alert (on me): {name}");
            BossSound.PlayDebuff(Settings.DebuffVolume);
        }
        else
        {
            if (!Settings.TargetDebuffAlert || !Settings.TargetDebuffNames.Contains(name)) return;
            Log.Write($"debuff alert (on the target): {name}");
            BossSound.PlayTargetDebuff(Settings.TargetDebuffVolume);
        }
    }

    /// <summary>The debuffs (by name) that landed on me since the app started, for the list in the settings.</summary>
    public IEnumerable<string> DebuffsSeen => _debuffs.Seen.Keys;

    public void SetDebuffAlert(bool enabled, string[] names)
    {
        (Settings.DebuffAlert, Settings.DebuffNames) = (enabled, names);
        SaveSettings();
    }

    public void SetDebuffVolume(int volume)
    {
        Settings.DebuffVolume = Math.Clamp(volume, 0, 100);
        SaveSettings();
    }

    public void PlayDebuffPreview() => BossSound.PlayDebuff(Settings.DebuffVolume);

    public void SetTargetDebuffAlert(bool enabled, string[] names)
    {
        (Settings.TargetDebuffAlert, Settings.TargetDebuffNames) = (enabled, names);
        SaveSettings();
    }

    public void SetTargetDebuffVolume(int volume)
    {
        Settings.TargetDebuffVolume = Math.Clamp(volume, 0, 100);
        SaveSettings();
    }

    public void PlayTargetDebuffPreview() => BossSound.PlayTargetDebuff(Settings.TargetDebuffVolume);

    public void SetTargetAlert(bool enabled, int[] classes)
    {
        (Settings.TargetAlert, Settings.TargetClasses) = (enabled, classes);
        SaveSettings();
    }

    public void SetTargetVolume(int volume)
    {
        Settings.TargetVolume = Math.Clamp(volume, 0, 100);
        SaveSettings();
    }

    /// <summary>The short tone at the volume now set (a test, and the slider lets go of the knob).</summary>
    public void PlayTargetPreview() => BossSound.PlayTarget(Settings.TargetVolume);

    /// <summary>A test alert (settings): banner and sound as they would be.</summary>
    public void TestBossAlert()
    {
        var alert = new BossAlert("감시자 카이라 (시험)", DateTimeOffset.Now.AddMinutes(5), 5);
        _overlay.ShowBossBanner(alert.Text);
        BossSound.Play(1, Settings.BossVolume);   // the test always rings, whatever the sound switch says
    }
    public void SetBossVolume(int volume)
    {
        Settings.BossVolume = Math.Clamp(volume, 0, 100);
        SaveSettings();
    }

    /// <summary>The chime at the volume now set (the settings slider lets go of the knob).</summary>
    public void PlayBossPreview() => BossSound.Play(1, Settings.BossVolume);


    public void SetBossSettings(bool alerts, bool kaira, bool agro, bool sound, int[] minutes)
    {
        (Settings.BossAlerts, Settings.BossKaira, Settings.BossAgro, Settings.BossSound) = (alerts, kaira, agro, sound);
        Settings.BossMinutes = minutes;
        SaveSettings();
    }

    private void SaveSettings()
    {
        if (_overlay.IsLoaded) (Settings.OverlayLeft, Settings.OverlayTop) = (_overlay.Left, _overlay.Top);
        try
        {
            Settings.Save(Paths.Settings);
        }
        catch (IOException e)
        {
            Log.Write("settings save: " + e.Message);
        }
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("설정 열기", null, (_, _) => ShowSettings());
        menu.Items.Add("내 기록", null, (_, _) => ShowRecords());
        menu.Items.Add("사용 설명서", null, (_, _) => ShowManual());
        menu.Items.Add("표시창 보이기/숨기기", null, (_, _) => ToggleOverlay());
        menu.Items.Add("마우스 통과 켜기/끄기", null, (_, _) => SetPassthrough(!_passthrough));
        menu.Items.Add("측정 시작/중지", null, (_, _) =>
        {
            if (Session.Measuring) StopMeasuring();
            else StartMeasuring();
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        _updateItem = new Forms.ToolStripMenuItem("업데이트 확인", null, async (_, _) =>
        {
            if (Updater.ReadyVersion is not null) RestartForUpdate();
            else await CheckForUpdate(manual: true);
        });
        menu.Items.Add(_updateItem);
        if (Uninstaller.UpdateExe() is not null) menu.Items.Add("프로그램 제거…", null, (_, _) => RemoveProgram());
        menu.Items.Add("종료", null, (_, _) => Exit());
        _tray = new Forms.NotifyIcon { Icon = TrayIcon.Create(), Text = "A2Rank", ContextMenuStrip = menu, Visible = true };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) ShowSettings();
        };
    }

    private ChangelogWindow? _changelog;
    private static IReadOnlyList<ChangelogEntry>? _allChanges;

    /// <summary>The change history (docs/CHANGELOG.md, embedded in the exe), newest first.</summary>
    public static IReadOnlyList<ChangelogEntry> AllChanges => _allChanges ??= LoadChanges();

    private static IReadOnlyList<ChangelogEntry> LoadChanges()
    {
        using var stream = typeof(Controller).Assembly.GetManifestResourceStream("changelog");
        return stream is null ? [] : Changelog.Parse(new StreamReader(stream).ReadToEnd());
    }

    public void ShowChangelog(string title, string? subtitle, IReadOnlyList<ChangelogEntry> entries, bool activate)
    {
        _changelog?.Close();
        var window = _changelog = new ChangelogWindow(title, subtitle, entries, activate);
        window.Closed += (_, _) =>
        {
            if (_changelog == window) _changelog = null;
        };
        window.Show();
        if (activate) window.Activate();
    }

    public void ShowAllChanges() => ShowChangelog("변경 내역", $"현재 버전 {Updater.Version}", AllChanges, activate: true);

    /// <summary>What the downloaded (not yet applied) version changes, from the notes in the signed update list.</summary>
    public void ShowNewVersionNotes()
    {
        if (Updater.ReadyVersion is not { } version || string.IsNullOrWhiteSpace(Updater.ReadyNotes)) return;
        ShowChangelog($"새 버전 {version}의 변경 내용", $"현재 {Updater.Version} → {version}. 다시 시작하면 적용됩니다.",
            Changelog.Parse($"## {version}\n{Updater.ReadyNotes}"), activate: true);
    }

    /// <summary>After an update (the version differs from the one last seen): a window with what changed. Not on a fresh install.</summary>
    private async Task ShowWhatsNew()
    {
        if (!Updater.Installed) return;
        await Task.Delay(TimeSpan.FromSeconds(4));   // let start-up finish first
        if (_disposed) return;
        var current = Updater.Version;
        var last = Settings.LastSeenVersion;
        if (last == current) return;
        Settings.LastSeenVersion = current;
        SaveSettings();
        if (last is null && _firstRun) return;       // installed just now: nothing to compare with
        var entries = Changelog.Since(AllChanges, last, current);
        if (entries.Count == 0) return;
        ShowChangelog($"{current}(으)로 업데이트되었습니다", last is null ? null : $"{last} 이후 바뀐 점", entries, activate: false);
    }

    /// <summary>--update-now: check, download and restart into the new version right away (also used to test updating).</summary>
    public async Task UpdateNow()
    {
        await CheckForUpdate(manual: true);
        if (Updater.ReadyVersion is not null) RestartForUpdate();
    }

    /// <summary>The first check shortly after start, so it does not compete with start-up work.</summary>
    private async Task FirstUpdateCheck()
    {
        await Task.Delay(TimeSpan.FromSeconds(15));
        if (Settings.AutoUpdate) await CheckForUpdate(manual: false);
    }

    /// <summary>The scheduled check (every 6 hours) and the tray menu's check; the result is shown as a tray notification.</summary>
    private async Task CheckForUpdate(bool manual)
    {
        var message = await CheckUpdateAsync(manual);
        if (message is not null) _tray?.ShowBalloonTip(5000, "A2Rank", message, Forms.ToolTipIcon.Info);
    }

    /// <summary>Looks for and downloads a new version; returns what to tell the user (null: nothing to say).</summary>
    public async Task<string?> CheckUpdateAsync(bool manual = true)
    {
        var message = await Updater.CheckAsync(manual);
        if (Updater.ReadyVersion is { } version && _updateItem is not null) _updateItem.Text = $"새 버전 {version} 적용하고 다시 시작";
        return message;
    }

    public void RestartForUpdate()
    {
        _leaving = true;
        Dispose();
        Updater.ApplyAndRestart();
    }

    private void RemoveProgram()
    {
        var answer = MessageBox.Show(
            "A2Rank를 제거합니다.\n\n" +
            "[예]  프로그램과 함께 기록·설정·녹화 데이터도 모두 삭제\n" +
            "[아니요]  프로그램만 제거 (데이터는 남김, 나중에 다시 설치하면 이어서 사용)\n" +
            "[취소]  제거하지 않음\n\n" +
            $"데이터 폴더: {Paths.Root}",
            "A2Rank 제거", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer == MessageBoxResult.Cancel) return;
        _leaving = true;
        Dispose();
        if (!Uninstaller.Start(deleteData: answer == MessageBoxResult.Yes)) _leaving = false;
        Application.Current.Shutdown();
    }

    private void RegisterHotkeys()
    {
        UnregisterHotkeys();
        if (!Settings.Hotkeys)
        {
            HotkeyNote = "꺼져 있음";
            return;
        }
        var failed = new List<string>();
        foreach (var action in HotkeyActions.All)
        {
            if (!Gesture.TryParse(Settings.Binding(action), out var g)) continue;
            int id = HotkeyBase + (int)action;
            if (Native.RegisterHotKey(0, id, (uint)g.Modifiers | Native.MOD_NOREPEAT, g.VirtualKey)) _hotkeys.Add(id);
            else failed.Add(g.ToString());
        }
        if (_hotkeys.Count > 0) ComponentDispatcher.ThreadFilterMessage += OnThreadMessage;
        HotkeyNote = failed.Count > 0 ? $"다른 프로그램이 이미 쓰는 단축키라 등록하지 못했습니다: {string.Join(", ", failed)}" : "";
    }

    /// <summary>While a shortcut is being typed in the settings, the current ones must not fire.</summary>
    public void SuspendHotkeys() => UnregisterHotkeys();

    public void ResumeHotkeys() => RegisterHotkeys();

    /// <returns>Why it was not accepted, or null.</returns>
    public string? SetHotkey(HotkeyAction action, string gesture)
    {
        if (gesture.Length > 0)
        {
            var other = HotkeyActions.All.FirstOrDefault(a => a != action && Settings.Binding(a) == gesture, action);
            if (other != action) return $"{gesture}는 이미 '{HotkeyActions.Label(other)}'에 쓰고 있습니다.";
        }
        Settings.HotkeyBindings[action.ToString()] = gesture;
        SaveSettings();
        RegisterHotkeys();
        return null;
    }

    private void UnregisterHotkeys()
    {
        foreach (int id in _hotkeys) Native.UnregisterHotKey(0, id);
        _hotkeys.Clear();
        ComponentDispatcher.ThreadFilterMessage -= OnThreadMessage;
    }

    private void OnThreadMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message != Native.WM_HOTKEY) return;
        if (!_hotkeys.Contains((int)msg.wParam)) return;
        handled = true;
        switch ((HotkeyAction)((int)msg.wParam - HotkeyBase))
        {
            case HotkeyAction.Overlay:
                ToggleOverlay();
                break;
            case HotkeyAction.Passthrough:
                SetPassthrough(!_passthrough);
                break;
            case HotkeyAction.KillList:
                ToggleKillList();
                break;
            case HotkeyAction.Live:
                ToggleLive();
                break;
        }
    }
}
