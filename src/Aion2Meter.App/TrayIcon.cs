using System.Drawing;
using System.Windows.Forms;

namespace Aion2Meter.App;

/// <summary>The tray icon: app.ico, embedded in the exe (the same file is the exe and installer icon).</summary>
internal static class TrayIcon
{
    public static Icon Create()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("app.ico")
            ?? throw new InvalidOperationException("app.ico is not embedded");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }
}
