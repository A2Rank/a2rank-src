using Velopack;

namespace Aion2Meter.App;

internal static class Program
{
    // Velopack must run first: it handles the installer's install/update/uninstall hooks and exits for them.
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
