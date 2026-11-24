using Aion2Meter.Capture;
using Aion2Meter.Core;
using Aion2Meter.Protocol;

/// <summary>--session: runs the recording through a fresh MeterSession (in-memory store) and prints what the app would show.</summary>
static class Replay
{
    /// <param name="character">Last played character as saved by the app (e.g. "1019:보리"), to replay a mid-game start.</param>
    public static void Run(string path, TextWriter output, string? character = null)
    {
        var frames = PcapReader.Read(path).ToList();
        using var store = new MeterStore(":memory:");
        if (character is not null) store.Set("character", character);
        var session = new MeterSession(store, ResetMode.RankingReset, 3, frames[0].Time);
        session.Start(frames[0].Time);
        var pipeline = new Pipeline();
        string last = "";
        foreach (var f in frames)
            foreach (var e in pipeline.Feed(f.Data, f.LinkType, f.Time))
            {
                if (e is MessageEvent { Message: EnterWorldMessage w }) output.WriteLine($"  enter {w.Name}/{w.Server} entity={w.Entity} before={session.Character} confirmed={session.CharacterConfirmed}");
                session.Handle(e);
                var b = session.Buckets.Current;
                string state = $"{session.Character?.Name ?? "?"} total={session.Points.Total} net={b?.Net} gain={b?.Gain} loss={b?.Loss} " +
                               $"events={b?.Events} kills={session.Kills.KillCount} unknown={session.Kills.UnknownCount} deaths={session.Kills.Deaths} rank={session.Rank?.AllRank}/{session.Rank?.AllTotal} class={session.Rank?.ClassRank}/{session.Rank?.ClassTotal}";
                if (state == last) continue;
                last = state;
                string what = e is MessageEvent m ? m.Message.GetType().Name.Replace("Message", "") : e.GetType().Name;
                output.WriteLine($"{e.Time.ToOffset(TimeSpan.FromHours(9)):HH:mm:ss.fff} {what,-10} {state}");
            }
        output.WriteLine($"\nkill list of the last hour ({session.Kills.Hour.ToOffset(TimeSpan.FromHours(9)):HH:mm}): name/server count points | damage I dealt, damage I took");
        foreach (var t in session.Kills.Sorted())
            output.WriteLine($"  {t.Name,-14} {t.Server,5} x{t.Count,-2} {t.Points,7:N0} | dealt {t.Dealt,12:N0}  took {t.Received,10:N0}");
    }
}
