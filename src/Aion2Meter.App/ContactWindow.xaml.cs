using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Aion2Meter.App;

/// <summary>"문의하기": a message, an optional way to answer and up to three pictures, sent to the collection server. Nothing else is sent but the app version.</summary>
public partial class ContactWindow : Window
{
    private static readonly string[] ImageTypes = [".png", ".jpg", ".jpeg", ".webp"];
    private readonly List<(string Name, byte[] Data)> _images = [];
    private bool _sending;
    private CancellationTokenSource? _cancel;

    public ContactWindow()
    {
        InitializeComponent();
        OnTextChanged(this, null!);
    }

    private void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _cancel?.Cancel();
        base.OnClosed(e);
    }

    private void OnLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs? e)
    {
        CountText.Text = $"{MessageBox.Text.Length} / 2000";
        SendButton.IsEnabled = !_sending && MessageBox.Text.Trim().Length >= 5;
    }

    private void OnAddImages(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "보낼 이미지 선택", Filter = "이미지|*.png;*.jpg;*.jpeg;*.webp", Multiselect = true };
        if (dialog.ShowDialog(this) == true) AddFiles(dialog.FileNames);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] names) AddFiles(names);
    }

    // Ctrl+V adds a picture from the clipboard; in the text boxes it stays a text paste unless only a picture is there.
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return;
        if (Keyboard.FocusedElement is TextBox && Clipboard.ContainsText()) return;
        if (!Clipboard.ContainsImage()) return;
        var source = Clipboard.GetImage();
        if (source is null) return;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        AddImage("붙여넣은 이미지.png", stream.ToArray());
        e.Handled = true;
    }

    private void AddFiles(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!ImageTypes.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)) continue;
            try
            {
                var info = new FileInfo(name);
                if (info.Length > WindowUploader.MaxContactImage) { StatusText.Text = $"{info.Name}: 4MB가 넘어서 뺐습니다."; continue; }
                AddImage(info.Name, File.ReadAllBytes(name));
            }
            catch (IOException) { StatusText.Text = "이미지를 읽지 못했습니다."; }
        }
    }

    private void AddImage(string name, byte[] data)
    {
        if (_images.Count >= WindowUploader.MaxContactImages) { StatusText.Text = $"이미지는 {WindowUploader.MaxContactImages}장까지 보낼 수 있습니다."; return; }
        if (data.Length > WindowUploader.MaxContactImage) { StatusText.Text = "4MB가 넘는 이미지는 보낼 수 없습니다."; return; }
        _images.Add((name, data));
        StatusText.Text = "";
        RebuildImages();
    }

    private void RebuildImages()
    {
        ImageRows.Children.Clear();
        foreach (var image in _images.ToList())
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var remove = new Button { Content = "빼기", Padding = new Thickness(8, 2, 8, 2) };
            remove.Click += (_, _) => { _images.Remove(image); RebuildImages(); };
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(new TextBlock
            {
                Text = $"{image.Name}  ({Math.Max(1, image.Data.Length / 1024)} KB)", VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            ImageRows.Children.Add(row);
        }
        AddImageButton.IsEnabled = _images.Count < WindowUploader.MaxContactImages;
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        _sending = true;
        SendButton.IsEnabled = false;
        StatusText.Text = _images.Count > 0 ? "이미지와 함께 보내는 중…" : "보내는 중…";
        _cancel = new CancellationTokenSource();
        string? problem = await WindowUploader.ContactAsync(MessageBox.Text.Trim(), ReplyBox.Text.Trim(), _images, _cancel.Token);
        _sending = false;
        if (!IsLoaded) return;
        if (problem is null)
        {
            StatusText.Text = "보냈습니다. 감사합니다.";
            MessageBox.Clear();
            ReplyBox.Clear();
            _images.Clear();
            RebuildImages();
        }
        else StatusText.Text = problem;
        OnTextChanged(this, null!);
    }
}
