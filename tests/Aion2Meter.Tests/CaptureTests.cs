using Aion2Meter.Capture;
using Aion2Meter.Protocol;

namespace Aion2Meter.Tests;

public class CaptureTests
{
    [Fact]
    public void Pcap_recording_replays_through_the_pipeline()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aion2meter-test-{Guid.NewGuid():N}.pcap");
        var t0 = DateTimeOffset.FromUnixTimeMilliseconds(1_791_630_000_123);
        byte[] sync = [.. Bytes.Frame(Bytes.Point(100)), .. Bytes.Frame(Bytes.Point(100)), .. Bytes.Frame(Bytes.Point(100))];
        try
        {
            using (var w = new PcapWriter(path, 1))
            {
                w.Write(t0, Bytes.Ethernet(1000, sync));
                w.Write(t0.AddSeconds(1), Bytes.Ethernet(1000 + (uint)sync.Length, Bytes.Frame(Bytes.Point(740))));
            }
            var frames = PcapReader.Read(path).ToList();
            Assert.Equal([t0, t0.AddSeconds(1)], frames.Select(f => f.Time));
            var pipeline = new Pipeline();
            var points = frames.SelectMany(f => pipeline.Feed(f.Data, f.LinkType, f.Time)).OfType<MessageEvent>()
                .Select(e => ((PointMessage)e.Message).Total);
            Assert.Equal([100UL, 100UL, 100UL, 740UL], points);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class RecordedCharacterSwitchTests
{
    /// <summary>
    /// testdata/character_switch.pcap (local only): zone move as 아라, change to 보리, change back to 아라.
    /// </summary>
    [Fact]
    public void Follows_the_played_character()
    {
        string? path = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null && path is null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "testdata", "character_switch.pcap"))) path = Path.Combine(dir.FullName, "testdata", "character_switch.pcap");
        if (path is null) return;

        using var store = new Aion2Meter.Core.MeterStore(":memory:");
        var frames = Aion2Meter.Capture.PcapReader.Read(path).ToList();
        var session = new Aion2Meter.Core.MeterSession(store, Aion2Meter.Core.ResetMode.RankingReset, 3, frames[0].Time);
        session.Start(frames[0].Time);
        var pipeline = new Pipeline();
        var seen = new List<string>();
        foreach (var f in frames)
            foreach (var e in pipeline.Feed(f.Data, f.LinkType, f.Time))
            {
                session.Handle(e);
                if (session.Character?.Name is { } name && (seen.Count == 0 || seen[^1] != name)) seen.Add(name);
            }
        // (the recording is local only and holds real names: the test checks the pattern, not the names)
        Assert.Equal(3, seen.Count);
        Assert.Equal(seen[0], seen[2]);
        Assert.NotEqual(seen[0], seen[1]);
        Assert.Equal(13 * 60 + 54, session.RankingOffset); // ranking deadline 02:13:54
    }
}
