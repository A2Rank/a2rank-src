using Velopack;
using Velopack.Sources;

namespace Aion2Meter.App;

/// <summary>
/// Checks the update location for a newer version, downloads it in the background and applies it
/// when the user restarts (or at exit). Does nothing when run from a build folder instead of the installed app.
/// </summary>
public sealed class Updater
{
    /// <summary>
    /// Where releases.win.json, its .sig and the packages are (output of build/release.ps1, put there by build/deploy.ps1): the "update" release of the
    /// public releases repository. Up to 0.1.29 this was our own server (https://131.186.41.160/m-b782c7639d501d9b9cce31b9/): 0.1.30 is still put there
    /// so that those apps find the version that points here.
    /// </summary>
    public const string UpdateUrl = "https://github.com/A2Rank/releases/releases/download/update/";

    private readonly UpdateManager _manager = new(new SimpleWebSource(UpdateUrl, new SecureDownloader()));
    private UpdateInfo? _ready;

    public bool Installed => _manager.IsInstalled;
    public string Version => _manager.CurrentVersion?.ToString() ?? "개발 빌드";
    /// <summary>Version that is downloaded and waiting for a restart; null when there is none.</summary>
    public string? ReadyVersion => _ready?.TargetFullRelease.Version.ToString();
    /// <summary>The release notes (markdown) of that version, from the signed update list.</summary>
    public string? ReadyNotes => _ready?.TargetFullRelease.NotesMarkdown;

    /// <returns>A short result for the user, or null when nothing needs saying (silent check, no update).</returns>
    public async Task<string?> CheckAsync(bool manual)
    {
        if (!_manager.IsInstalled) return manual ? "설치된 앱에서만 업데이트를 확인합니다 (지금은 개발 빌드)." : null;
        if (_ready is not null) return $"{ReadyVersion} 버전이 준비되어 있습니다. 다시 시작하면 적용됩니다.";
        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null)
            {
                Log.Write($"update: up to date ({Version})");
                return manual ? $"최신 버전입니다 ({Version})." : null;
            }
            Log.Write($"update: {Version} -> {info.TargetFullRelease.Version}, downloading");
            await _manager.DownloadUpdatesAsync(info);
            _ready = info;
            Log.Write($"update: {ReadyVersion} downloaded");
            return $"새 버전 {ReadyVersion}을 내려받았습니다. 다시 시작하면 적용됩니다.";
        }
        catch (Exception e)
        {
            Log.Write("update: " + e.Message);
            return manual ? "업데이트를 확인하지 못했습니다. 인터넷 연결을 확인하세요." : null;
        }
    }

    public void ApplyAndRestart()
    {
        if (_ready is not null) _manager.ApplyUpdatesAndRestart(_ready.TargetFullRelease);
    }

    /// <summary>Installs a downloaded update after the app has exited (no restart).</summary>
    public void ApplyOnExit()
    {
        if (_ready is not null) _manager.WaitExitThenApplyUpdates(_ready.TargetFullRelease, silent: true, restart: false);
    }
}
