using System.Diagnostics;
using Aion2Meter.Capture;
using Aion2Meter.Core;
using Aion2Meter.Protocol;

/// <summary>--bench: where the time goes on a recording: decoding, the session, the debuff watcher, and the refresh the window does every 100 ms.</summary>
static class Bench
{
    public static void Run(string path, TextWriter output)
    {
        var frames = PcapReader.Read(path).ToList();
        using var store = new MeterStore(":memory:");
        var session = new MeterSession(store, ResetMode.RankingReset, 3, frames[0].Time);
        session.Start(frames[0].Time);
        var pipeline = new Pipeline();
        var watcher = new DebuffWatcher();
        var events = new List<CaptureEvent>();
        var sw = Stopwatch.StartNew();
        foreach (var f in frames) events.AddRange(pipeline.Feed(f.Data, f.LinkType, f.Time));
        output.WriteLine($"frames {frames.Count}, events {events.Count}, decode {sw.ElapsedMilliseconds} ms");
        TimeSpan handle = default, observe = default, live = default, rankRefresh = default;
        long liveCalls = 0, maxLive = 0, maxOpp = 0;
        DateTimeOffset lastRefresh = default;
        foreach (var e in events)
        {
            var t = Stopwatch.GetTimestamp();
            session.Handle(e);
            handle += Stopwatch.GetElapsedTime(t);
            t = Stopwatch.GetTimestamp();
            if (e is MessageEvent { Message: var m }) watcher.Observe(m, session.Matcher.SelfEntity, e.Time, session.Matcher.IsEnemy, null);
            observe += Stopwatch.GetElapsedTime(t);
            if (e.Time - lastRefresh >= TimeSpan.FromMilliseconds(100))
            {
                lastRefresh = e.Time;
                t = Stopwatch.GetTimestamp();
                var l = session.Matcher.Live(e.Time);
                var el = Stopwatch.GetElapsedTime(t);
                live += el; liveCalls++; maxLive = Math.Max(maxLive, (long)el.TotalMilliseconds); maxOpp = Math.Max(maxOpp, l.Count);
                t = Stopwatch.GetTimestamp();
                _ = session.Kills.Sorted().Count();
                rankRefresh += Stopwatch.GetElapsedTime(t);
            }
        }
        output.WriteLine($"Handle {handle.TotalMilliseconds:0} ms, Observe {observe.TotalMilliseconds:0} ms, Live x{liveCalls} {live.TotalMilliseconds:0} ms (max {maxLive} ms, up to {maxOpp} opponents), Kills.Sorted {rankRefresh.TotalMilliseconds:0} ms");
        output.WriteLine($"recording length {(events[^1].Time - events[0].Time).TotalSeconds:0} s");
    }
}
