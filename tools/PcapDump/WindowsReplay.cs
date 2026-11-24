using Aion2Meter.Capture;
using Aion2Meter.Core;
using Aion2Meter.Protocol;

/// <summary>
/// --windows-replay: runs recordings (one measurement each) through a fresh MeterSession and writes the window records ("구간 기록") the
/// app would have made for one character, one JSON per line (WindowRecord.ToJson). A period that a recording's start or end cuts is
/// partial, like when the real measurement was stopped and started again; whole hours measured throughout are complete.
/// Usage: --windows-replay <character name> <out file> <folder-or-pcap ...>
/// </summary>
static class WindowsReplay
{
    public static void Run(string character, string outFile, IEnumerable<string> paths, TextWriter output)
    {
        var files = paths.SelectMany(p => Directory.Exists(p) ? Directory.GetFiles(p, "*.pcap") : [p]).OrderBy(f => f, StringComparer.Ordinal).ToList();
        using var store = new MeterStore(":memory:");
        store.Set("ranking_offset", "834");            // the game's reset second (xx:13:54), learned from the ranking messages
        store.Set("character", "1019:" + character);   // 이슈타르; the enter-world message tells the rest
        var records = new List<WindowRecord>();
        MeterSession? session = null;
        foreach (var file in files)
        {
            try
            {
                using var frames = PcapReader.Read(file).GetEnumerator();
                if (!frames.MoveNext()) continue;
                var first = frames.Current.Time;
                if (session is null)
                {
                    session = new MeterSession(store, ResetMode.RankingReset, 3, first);
                    session.WindowClosed += records.Add;
                }
                session.Start(first);
                var pipeline = new Pipeline();
                var last = first;
                var lastTick = first;
                do
                {
                    var f = frames.Current;
                    foreach (var e in pipeline.Feed(f.Data, f.LinkType, f.Time))
                    {
                        session.Handle(e);
                        if (e.Time - lastTick >= TimeSpan.FromSeconds(1)) { session.Tick(e.Time); lastTick = e.Time; }
                    }
                    last = f.Time;
                } while (frames.MoveNext());
                session.Tick(last);
                session.Stop(last);
                output.WriteLine($"{Path.GetFileName(file)}: records so far {records.Count}");
            }
            catch (IOException e)
            {
                output.WriteLine($"skipped {Path.GetFileName(file)} ({e.Message})");
            }
        }
        var mine = records.Where(r => r.CharacterName == character).ToList();
        File.WriteAllLines(outFile, mine.Select(r => r.ToJson()));
        string Kst(DateTimeOffset t) => t.ToOffset(TimeSpan.FromHours(9)).ToString("MM-dd HH:mm");
        output.WriteLine($"\n{character}: {mine.Count} records ({mine.Count(r => r.Complete)} complete), {records.Count - mine.Count} of other characters left out");
        foreach (var r in mine)
            output.WriteLine($"  {Kst(r.Start)}~{Kst(r.End)[6..]} {(r.Complete ? "complete" : r.Aligned ? "partial " : "partial*")} gain {r.Gain,7:N0} loss {r.Loss,6:N0} events {r.Events,3} kills {r.Kills.Count,3} total {r.StartTotal:N0} -> {r.EndTotal:N0}");
    }
}
