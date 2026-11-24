using System.Windows;
using System.Windows.Input;

namespace Aion2Meter.App;

/// <summary>
/// Shown at the first start (and again when <see cref="Version"/> goes up, i.e. when the notice changes in a way that matters): what the program is, the risk
/// of using it and what is sent. Using the program needs the box ticked; "종료" closes the program. This is not the consent to send data (that stays in the settings).
/// </summary>
public partial class NoticeWindow : Window
{
    /// <summary>Raise when the notice changes so much that everybody should read it again.</summary>
    public const int Version = 1;

    public NoticeWindow() => InitializeComponent();

    private void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void OnChecked(object sender, RoutedEventArgs e) => StartButton.IsEnabled = Understood.IsChecked == true;

    private void OnStart(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnExit(object sender, RoutedEventArgs e) => DialogResult = false;
}
