using System.Diagnostics;
using System.IO;

namespace Aion2Meter.App;

/// <summary>Removes the installed app through Velopack's Update.exe, optionally with the user's data.</summary>
internal static class Uninstaller
{
    /// <summary>Update.exe sits next to the app's "current" folder; null when not installed (build folder, portable).</summary>
    public static string? UpdateExe()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Update.exe"));
        return File.Exists(path) && !File.Exists(Path.Combine(Path.GetDirectoryName(path)!, ".portable")) ? path : null;
    }

    /// <summary>Call after the app has closed its database. Starts the uninstall and returns; the app should exit.</summary>
    public static bool Start(bool deleteData)
    {
        var updateExe = UpdateExe();
        if (updateExe is null) return false;
        if (deleteData)
        {
            try { Directory.Delete(Paths.Root, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write("delete data: " + e.Message); }
        }
        Process.Start(new ProcessStartInfo(updateExe, "--uninstall --silent") { UseShellExecute = false, CreateNoWindow = true });
        return true;
    }
}
