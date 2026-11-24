using System.Windows;

namespace Aion2Meter.App;

public partial class App : Application
{
    private Mutex? _single;
    private Controller? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Paths.MigrateFromOldName();
        _single = new Mutex(true, @"Local\A2Rank", out bool first);
        if (!first)
        {
            MessageBox.Show("A2Rank가 이미 실행 중입니다. 작업 표시줄 오른쪽 트레이 아이콘을 확인하세요.", "A2Rank");
            Shutdown();
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write(args.Exception.ToString());
            args.Handled = true;
        };
        _controller = new Controller();
        // The first start (and a start after the notice changed): what this is, the risk, what is sent - nothing starts before it is ticked.
        if (_controller.Settings.NoticeAccepted < NoticeWindow.Version && !e.Args.Any(a => a.StartsWith("--preview")))
        {
            if (new NoticeWindow().ShowDialog() != true)
            {
                Shutdown();
                return;
            }
            _controller.Settings.NoticeAccepted = NoticeWindow.Version;
            _controller.SettingsChanged();
        }
        _controller.Run();
        if (e.Args.Contains("--settings")) _controller.ShowSettings();
        if (e.Args.Contains("--preview-kills")) _controller.PreviewKills = SampleKills();
        if (Array.IndexOf(e.Args, "--preview-replay") is >= 0 and var at && at + 1 < e.Args.Length) _controller.ReplayRecording(e.Args[at + 1], Array.IndexOf(e.Args, "--preview-until") is >= 0 and var un && un + 1 < e.Args.Length ? TimeSpan.Parse(e.Args[un + 1]) : null);
        if (Array.IndexOf(e.Args, "--preview-character") is >= 0 and var pc && pc + 2 < e.Args.Length && int.TryParse(e.Args[pc + 2], out var pcServer)) CharacterInfoWindow.Show(e.Args[pc + 1], pcServer, null);   // development: the character page of a name and server code
        if (e.Args.Contains("--update-now")) _ = _controller.UpdateNow();
    }

    private static Core.HourlyKillList SampleKills()
    {
        var now = DateTimeOffset.Now;
        var list = new Core.HourlyKillList();
        // Damage per skill: split the totals over a few skills of the class (keys of Protocol.Skills) so the unfolded view has something to show.
        static Core.DamageStats Damage(long dealt, long received)
        {
            Core.SkillDamage[] Split(long total, params uint[] keys) =>
                [.. keys.Select((k, i) => new Core.SkillDamage(k, 3 - i, total * (keys.Length - i) / (keys.Length * (keys.Length + 1) / 2)))];
            return new Core.DamageStats(dealt, received, 6, 3,
                dealt > 0 ? Split(dealt, 0x676A, 0x6563, 0x69AD, 0x65BF) : [], received > 0 ? Split(received, 0x65CE, 0x5390) : []);
        }
        // (the last two columns: damage I dealt to them / they dealt to me; 0 = none seen)
        foreach (var (name, server, minutes, points, dealt, received) in new[] { ("예시가", 2004, 30, 640L, 182_400L, 36_200L), ("예시나", 2007, 25, 550L, 9_800L, 0L),
                     ("예시가", 2004, 20, 610L, 245_000L, 12_000L), ("예시다", 2012, 15, 1200L, 0L, 5_300L), ("예시가", 2004, 10, 700L, 130_000L, 48_000L),
                     ("예시나", 2007, 5, 530L, 21_500L, 3_400L), ("예시라", 2001, 2, 2300L, 1_234_567_890L, 80_000L) })
            list.Add(new Core.KillRecord(Core.KillKind.Kill, name, server, 2, points, now.AddMinutes(-Math.Min(minutes, now.Minute)),
                Damage: dealt + received == 0 ? null : Damage(dealt, received)));
        list.AddUnknown(new Core.UnmatchedGain(420, now));
        // who hurt me before the deaths: the killer and two helpers, plus monsters
        static IReadOnlyList<Core.AttackerDamage> Attackers(long killer) =>
        [
            new("예시마", 2003, 4444, false, killer, 4, [new(0x6563, 2, killer * 3 / 5), new(0x69AD, 2, killer * 2 / 5)]),
            new("예시사", 2005, 5555, false, killer / 2, 3, [new(0x5390, 1, killer / 3), new(0x5579, 2, killer / 6)]),
            new("", 0, 6666, false, killer / 8, 1, [new(0x6590, 1, killer / 8)]),
            new("", 0, 0, true, killer / 10, 5, [new(0x6563, 5, killer / 10)]),
        ];
        list.Add(new Core.KillRecord(Core.KillKind.Death, "예시마", 2003, 2, -400, now.AddMinutes(-Math.Min(3, now.Minute)), Damage: Damage(9_000, 61_000), Attackers: Attackers(61_000)));
        list.Add(new Core.KillRecord(Core.KillKind.Death, "예시마", 2003, 2, -400, now, Damage: Damage(4_000, 38_000), Attackers: Attackers(38_000)));
        list.Add(new Core.KillRecord(Core.KillKind.Death, "예시바", 2008, 1, -310, now.AddMinutes(-Math.Min(1, now.Minute))));
        return list;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
