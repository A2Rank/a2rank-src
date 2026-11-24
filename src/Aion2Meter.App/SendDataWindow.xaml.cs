using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Aion2Meter.Core;
using Microsoft.Win32;

namespace Aion2Meter.App;

/// <summary>
/// "데이터 보내기": consent to packet collection, the recordings and combat-log screenshots to send, then the upload.
/// Nothing leaves the PC without the user pressing the send button.
/// </summary>
public partial class SendDataWindow : Window
{
    private static readonly string[] ImageTypes = [".png", ".jpg", ".jpeg", ".bmp", ".webp"];
    private const int MaxImages = 30;

    private readonly Controller _controller;
    /// <summary>The newest recordings are all sent: the user cannot tell which fight a screenshot belongs to, so we find that out.</summary>
    private const int RecordingsToSend = 3;

    private List<FileInfo> _latest = [];
    private readonly List<FileInfo> _images = [];
    private bool _sending;

    public SendDataWindow(Controller controller)
    {
        _controller = controller;
        InitializeComponent();
        Consent.IsChecked = controller.Settings.RecordPackets;
        _controller.CutRecording();   // the fight that just happened becomes a finished file
        LoadRecordings();
        ShowState();
    }

    private void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnExample(object sender, RoutedEventArgs e)
    {
        bool show = ExamplePanel.Visibility != Visibility.Visible;
        ExamplePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ExampleButton.Content = show ? "예시 사진 접기" : "예시 사진 보기";
    }

    private void OnLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void OnConsent(object sender, RoutedEventArgs e)
    {
        _controller.Settings.RecordPackets = Consent.IsChecked == true;
        _controller.SettingsChanged();
        ShowState();
    }

    /// <summary>The newest non-empty recordings, shown as a plain list (nothing to pick).</summary>
    private void LoadRecordings()
    {
        // One file per fight now: the files of the last hour (at most 10); when there are none, the newest few.
        var all = DataUploader.Recordings(_controller.EffectiveRecordingFolder).Where(f => f.Length > 0).ToList();
        var recent = all.Where(f => DateTime.Now - f.LastWriteTime < TimeSpan.FromHours(1)).Take(10).ToList();
        _latest = recent.Count > 0 ? recent : all.Take(RecordingsToSend).ToList();
        RecordingRows.Children.Clear();
        foreach (var file in _latest)
            RecordingRows.Children.Add(new TextBlock { Text = $"{file.LastWriteTime:MM-dd HH:mm}  ·  {Size(file.Length)}", Margin = new Thickness(0, 2, 0, 0) });
        RecordingsEmpty.Visibility = _latest.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The recordings that go out: all of the newest ones, fewer (oldest dropped) only when the package would be too big.</summary>
    private IReadOnlyList<FileInfo> Outgoing()
    {
        var list = _latest.ToList();
        while (list.Count > 1 && DataUploader.Estimate(list, _images) > DataUploader.MaxPackage) list.RemoveAt(list.Count - 1);
        return list;
    }

    private void OnAddImages(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "인게임 전투 분석기 캡처 선택",
            Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp;*.webp",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == true) AddImages(dialog.FileNames);
    }

    // Ctrl+V anywhere in the window adds a picture from the clipboard; in the note box it stays a text paste unless only a picture is there.
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return;
        if (Keyboard.FocusedElement is TextBox && Clipboard.ContainsText()) return;
        if (PasteFromClipboard()) e.Handled = true;
    }

    private void OnPaste(object sender, RoutedEventArgs e)
    {
        if (!PasteFromClipboard()) StatusText.Text = "클립보드에 이미지가 없습니다. 먼저 화면을 캡처(Win+Shift+S)해 주세요.";
    }

    /// <returns>true when the clipboard held a picture (or picture files) and it was added.</returns>
    private bool PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var before = _images.Count;
                AddImages(Clipboard.GetFileDropList().Cast<string>());
                if (_images.Count > before) return true;
            }
            if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } bitmap) return false;
            Directory.CreateDirectory(PasteFolder);
            var path = Path.Combine(PasteFolder, $"paste-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
            AddImages([path]);
            return true;
        }
        catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.ExternalException or UnauthorizedAccessException)
        {
            StatusText.Text = "클립보드를 읽지 못했습니다. 다시 시도해 주세요.";   // the clipboard is briefly locked by its owner
            return true;
        }
    }

    private static string PasteFolder => Path.Combine(Paths.Root, "paste");

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { if (Directory.Exists(PasteFolder)) Directory.Delete(PasteFolder, recursive: true); } catch (IOException) { }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] names) AddImages(names);
    }

    private void AddImages(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!ImageTypes.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)) continue;
            if (_images.Count >= MaxImages) { StatusText.Text = $"이미지는 {MaxImages}장까지 보낼 수 있습니다."; break; }
            var info = new FileInfo(name);
            if (!info.Exists || _images.Any(i => string.Equals(i.FullName, info.FullName, StringComparison.OrdinalIgnoreCase))) continue;
            _images.Add(info);
        }
        RebuildImageRows();
        ShowState();
    }

    private void RebuildImageRows()
    {
        ImageRows.Children.Clear();
        foreach (var image in _images.ToList())
        {
            var remove = new Button { Content = "×", Padding = new Thickness(7, 0, 7, 1), Margin = new Thickness(0, 0, 8, 0) };
            remove.Click += (_, _) => { _images.Remove(image); RebuildImageRows(); ShowState(); };
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            DockPanel.SetDock(remove, Dock.Left);
            row.Children.Add(remove);
            row.Children.Add(new TextBlock
            {
                Text = $"{image.Name}  ·  {Size(image.Length)}", TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            });
            ImageRows.Children.Add(row);
        }
    }

    private void ShowState()
    {
        bool on = Consent.IsChecked == true;
        ConsentState.Text = on ? "수집 중입니다. 지금 측정 중이면 측정을 한 번 껐다 켜야 녹화가 시작됩니다." : "";
        var outgoing = Outgoing();
        long estimate = DataUploader.Estimate(outgoing, _images);
        SizeText.Text = _images.Count == 0 ? "캡처를 추가해 주세요." : $"녹화 {outgoing.Count}개(자동) · 이미지 {_images.Count}장 · 약 {Size(estimate)}";
        // The screenshots are what we need from the user; the recordings come along by themselves.
        SendButton.IsEnabled = !_sending && on && _images.Count > 0 && outgoing.Count > 0 && estimate <= DataUploader.MaxPackage;
        if (estimate > DataUploader.MaxPackage) StatusText.Text = $"너무 큽니다. 최대 {DataUploader.MaxPackage / 1048576} MB까지 보낼 수 있습니다. 이미지를 줄여 주세요.";
        else if (!on) StatusText.Text = "먼저 1번의 동의를 체크해 주세요.";
        else if (_sending) { }
        else if (!DataUploader.Configured) StatusText.Text = "받는 서버 정보가 아직 이 버전에 들어 있지 않아 보낼 수 없습니다.";
        else if (_latest.Count == 0) StatusText.Text = "보낼 녹화가 아직 없습니다. 동의 후 측정을 켜 두고 전투한 다음 다시 열어 주세요.";
        else StatusText.Text = "";
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        _controller.CutRecording();   // what happened while this window was open is included too
        LoadRecordings();
        var recordings = Outgoing();
        var images = _images.ToList();
        _sending = true;
        SendButton.IsEnabled = false;
        StatusText.Text = "데이터를 묶는 중…";
        string? zip = null;
        try
        {
            string note = NoteBox.Text.Trim();
            zip = await Task.Run(() => DataUploader.Build(recordings, images, note));
            StatusText.Text = "보내는 중… 창을 닫지 마세요.";
            var problem = await DataUploader.SendAsync(zip, CancellationToken.None);
            if (problem is null)
            {
                _images.Clear();
                RebuildImageRows();
                StatusText.Text = "보냈습니다. 감사합니다!";
            }
            else StatusText.Text = problem;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log.Write("upload build: " + ex.Message);
            StatusText.Text = "파일을 읽지 못했습니다: " + ex.Message;
        }
        finally
        {
            if (zip is not null) DataUploader.TryDelete(zip);
            _sending = false;
            var result = StatusText.Text;
            ShowState();
            StatusText.Text = result;   // the outcome stays on screen; ShowState only refreshes the hints
        }
    }

    private static string Size(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576.0:0.#} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}
