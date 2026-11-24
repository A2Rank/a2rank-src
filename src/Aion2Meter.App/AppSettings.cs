using System.IO;
using System.Text.Json;
using Aion2Meter.Core;

namespace Aion2Meter.App;

/// <summary>User choices, saved as JSON next to the database.</summary>
public sealed class AppSettings
{
    public ResetMode ResetMode { get; set; } = ResetMode.RankingReset;
    public int PinThreshold { get; set; } = 3;
    public bool AutoStart { get; set; } = true;
    /// <summary>Field boss alerts (10 and 5 minutes before): master switch, each boss, sound; and the next 정령왕 아그로 spawn we know (epoch ms).</summary>
    public bool BossAlerts { get; set; } = true;
    public bool BossKaira { get; set; } = true;
    public bool BossAgro { get; set; } = true;
    public bool BossSound { get; set; } = true;
    /// <summary>A short tone when I aim at a player of one of these classes (codes 11..19, see <see cref="PlayerClass"/>); the class is known from the moment they came into view. Off by default.</summary>
    public bool TargetAlert { get; set; }
    public int[] TargetClasses { get; set; } = [];
    public int TargetVolume { get; set; } = 60;
    /// <summary>Debuff alert: a short tone when a debuff of one of the chosen names (skill names) lands on me.</summary>
    public bool DebuffAlert { get; set; }
    public string[] DebuffNames { get; set; } = [];
    public int DebuffVolume { get; set; } = 60;
    /// <summary>The debuff alert of the one I aim at, apart from the one for me (its own names, volume and tone): a chosen debuff lands on it (mine, a friend's on an enemy; an enemy's on a friend I aim at).</summary>
    public bool TargetDebuffAlert { get; set; }
    public string[] TargetDebuffNames { get; set; } = [];
    public int TargetDebuffVolume { get; set; } = 60;
    /// <summary>Volume of the alert sound, 0..100 (the app's own, not the system's).</summary>
    public int BossVolume { get; set; } = 70;
    /// <summary>Minutes before a spawn to alert at (any of 10, 5, 1).</summary>
    public int[] BossMinutes { get; set; } = [10, 5];
    /// <summary>Look for a new version at start and every 6 hours and download it in the background.</summary>
    public bool AutoUpdate { get; set; } = true;
    /// <summary>The version whose changes were last shown; a different running version means the app was just updated.</summary>
    public string? LastSeenVersion { get; set; }
    public bool ShowKillList { get; set; } = true;
    /// <summary>The overlay, the kill list, the live fight window and the detail windows stay above all other windows (off: they are ordinary windows that other windows, also the game, can cover).</summary>
    public bool AlwaysOnTop { get; set; } = true;
    /// <summary>Where the character info window was last (it opens there again; null: not yet).</summary>
    public double? CharInfoLeft { get; set; }
    public double? CharInfoTop { get; set; }
    public double? CharInfoWidth { get; set; }
    public double? CharInfoHeight { get; set; }
    public bool Hotkeys { get; set; } = true;
    /// <summary>Shortcut per action name ("Ctrl+Alt+P"); "" turns one off. Missing actions use their default.</summary>
    public Dictionary<string, string> HotkeyBindings { get; set; } = [];

    public string Binding(HotkeyAction a) =>
        HotkeyBindings.TryGetValue(a.ToString(), out var g) ? g : HotkeyActions.Default(a);
    public bool RecordPackets { get; set; }
    /// <summary>The version of the first-start notice (<see cref="NoticeWindow.Version"/>) the user ticked; 0 = not yet.</summary>
    public int NoticeAccepted { get; set; }
    /// <summary>How long the other players' names (kills, copies of the period records) stay on this PC: 7, 30 or 0 = for good (default).</summary>
    public int NameKeepDays { get; set; }
    /// <summary>Consent: send each finished hour of the ranking clock ("구간 기록") to the collection server. Off by default.</summary>
    public bool UploadWindows { get; set; }
    /// <summary>Random id of this installation, made when the consent is given; sent with the windows so the server can drop them on request.</summary>
    public string? InstallId { get; set; }
    /// <summary>Folder for the raw packet recordings (.pcap); null = the default inside the app data folder.</summary>
    public string? RecordingFolder { get; set; }
    /// <summary>Folder for per-period record files; null = records are not written (default).</summary>
    public string? RecordFolder { get; set; }
    public double? OverlayLeft { get; set; }
    public double? OverlayTop { get; set; }
    /// <summary>The kill list follows the overlay; false once the user moved it on its own.</summary>
    /// <summary>Background opacity of the overlay and the kill list, 0.2-1: normally / while clicks pass through.</summary>
    public double PanelOpacity { get; set; } = 0.97;
    public double PassthroughOpacity { get; set; } = 0.7;
    /// <summary>The kill list's own background opacity (normally / while clicks pass through); null = same as the overlay's.</summary>
    public double? KillListOpacity { get; set; }
    public double? KillListPassthroughOpacity { get; set; }
    public bool KillListDocked { get; set; } = true;
    public double? KillListLeft { get; set; }
    public double? KillListTop { get; set; }
    public double KillListWidth { get; set; } = 440;
    public double KillListHeight { get; set; } = 260;
    /// <summary>The live fight window (실시간 전투): shown, order (0 total, 1 dealt, 2 received), lines listed, place and size.</summary>
    public bool ShowLive { get; set; }
    public int LiveSort { get; set; }
    public int LiveRows { get; set; } = 10;
    public double? LiveLeft { get; set; }
    public double? LiveTop { get; set; }
    public double LiveWidth { get; set; } = 330;
    public double LiveHeight { get; set; } = 380;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Log.Write("settings: " + e.Message);
        }
        return new();
    }

    public void Save(string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>
/// Where the app keeps its files: %LocalAppData%\A2RankData. Not %LocalAppData%\A2Rank, which is the
/// installer's folder: installing over it fails when it exists, and uninstalling deletes everything in it.
/// The program was called Aion2Meter before: its folder %LocalAppData%\Aion2MeterData is left alone, and its settings and database are copied
/// once into the new folder (<see cref="MigrateFromOldName"/>).
/// </summary>
public static class Paths
{
    public static readonly string Root = Environment.GetEnvironmentVariable("A2RANK_DATA") is { Length: > 0 } dev   // development: another data folder (screenshots from a replayed recording)
        ? dev : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "A2RankData");
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Database => Path.Combine(Root, "meter.db");
    public static string Recordings => Path.Combine(Root, "recordings");
    public static string LogFile => Path.Combine(Root, "app.log");

    /// <summary>
    /// First start under the new name: copies the settings and the records of the old folder (the packet recordings stay there: they are 300 MB at most and
    /// can be made again). The old folder is not touched, so an old installation that is still there keeps working. Only when the new folder is empty.
    /// </summary>
    public static void MigrateFromOldName()
    {
        try
        {
            var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aion2MeterData");
            if (!Directory.Exists(old) || File.Exists(Database) || File.Exists(Settings)) return;
            Directory.CreateDirectory(Root);
            bool records = File.Exists(Path.Combine(old, "meter.db")) && MeterStore.CopyDatabase(Path.Combine(old, "meter.db"), Database);
            if (File.Exists(Path.Combine(old, "settings.json"))) File.Copy(Path.Combine(old, "settings.json"), Settings);
            Log.Write($"migrated from {old}: settings and {(records ? "records copied" : "records NOT copied (the database could not be read)")}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException) { Log.Write("migration from the old folder failed: " + e.Message); }
    }
}

public static class Log
{
    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Paths.Root);
            File.AppendAllText(Paths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
    }
}

/// <summary>Window options that windows read when they are made (set from the settings by the controller).</summary>
public static class WindowPrefs
{
    public static bool AlwaysOnTop { get; set; } = true;
}
