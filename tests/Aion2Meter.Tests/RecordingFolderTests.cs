using Aion2Meter.Core;

namespace Aion2Meter.Tests;

public sealed class RecordingFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "a2m-rec-" + Guid.NewGuid().ToString("N"));

    public RecordingFolderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Dir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private string Pcap(string folder, string name, int kb, DateTime written)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[kb * 1024]);
        File.SetLastWriteTimeUtc(path, written);
        return path;
    }

    [Fact]
    public void Trimming_deletes_the_oldest_recordings_until_the_rest_fits_and_leaves_other_files()
    {
        var folder = Dir("limit");
        var t = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);
        Pcap(folder, "capture-1.pcap", 100, t);
        Pcap(folder, "capture-2.pcap", 100, t.AddHours(1));
        Pcap(folder, "capture-3.pcap", 100, t.AddHours(2));
        Pcap(folder, "capture-4.pcap", 100, t.AddHours(3));
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "mine");

        var (deleted, freed) = RecordingFolder.Trim(folder, 250 * 1024);                       // 400 KB in, 250 KB allowed
        Assert.Equal((2, 200 * 1024L), (deleted, freed));
        Assert.Equal(["capture-3.pcap", "capture-4.pcap", "notes.txt"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
        Assert.Equal((0, 0L), RecordingFolder.Trim(folder, 250 * 1024));                       // nothing more to do
    }

    [Fact]
    public void Trimming_never_deletes_the_recording_being_written_even_when_it_is_the_oldest()
    {
        var folder = Dir("keep");
        var t = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);
        var current = Pcap(folder, "capture-now.pcap", 100, t);                                 // the oldest by its time, but it is being written
        Pcap(folder, "capture-b.pcap", 100, t.AddHours(1));
        Pcap(folder, "capture-c.pcap", 100, t.AddHours(2));

        RecordingFolder.Trim(folder, 250 * 1024, keep: current);                                  // 300 KB in: one has to go, not the oldest
        Assert.Equal(["capture-c.pcap", "capture-now.pcap"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());

        RecordingFolder.Trim(folder, 50 * 1024, keep: current);                                   // even when it alone is over the limit
        Assert.Equal(["capture-now.pcap"], Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    [Fact]
    public void Trimming_skips_a_recording_that_is_in_use_and_deletes_the_next_oldest()
    {
        var folder = Dir("busy");
        var t = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);
        var busy = Pcap(folder, "capture-1.pcap", 100, t);
        Pcap(folder, "capture-2.pcap", 100, t.AddHours(1));
        Pcap(folder, "capture-3.pcap", 100, t.AddHours(2));

        using (new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.None))              // something holds the oldest open
            RecordingFolder.Trim(folder, 200 * 1024);
        Assert.Equal(["capture-1.pcap", "capture-3.pcap"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Trimming_a_missing_folder_does_nothing()
    {
        Assert.Equal((0, 0L), RecordingFolder.Trim(Path.Combine(_root, "nothing-here"), 1));
        Assert.Equal(300L * 1024 * 1024, RecordingFolder.LimitBytes);
    }

    [Fact]
    public void A_writable_folder_is_accepted_and_created_and_leaves_nothing_behind()
    {
        var folder = Path.Combine(_root, "new", "deeper");
        Assert.Null(RecordingFolder.Check(folder));
        Assert.True(Directory.Exists(folder));
        Assert.Empty(Directory.GetFileSystemEntries(folder));
    }

    [Fact]
    public void A_folder_that_cannot_exist_is_refused_with_a_reason()
    {
        var file = Path.Combine(_root, "plain-file");
        File.WriteAllText(file, "x");
        Assert.False(string.IsNullOrEmpty(RecordingFolder.Check(Path.Combine(file, "sub"))));   // a folder below a file
    }

    [Fact]
    public void Moving_takes_only_pcap_files_and_never_overwrites()
    {
        var from = Dir("from");
        var to = Dir("to");
        File.WriteAllText(Path.Combine(from, "a.pcap"), "new a");
        File.WriteAllText(Path.Combine(from, "b.pcap"), "b");
        File.WriteAllText(Path.Combine(from, "notes.txt"), "stay");
        File.WriteAllText(Path.Combine(to, "a.pcap"), "old a");

        Assert.Equal((2, 0), RecordingFolder.MoveRecordings(from, to));

        Assert.Equal("old a", File.ReadAllText(Path.Combine(to, "a.pcap")));
        Assert.Equal("new a", File.ReadAllText(Path.Combine(to, "a (2).pcap")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(to, "b.pcap")));
        Assert.Equal(["notes.txt"], Directory.GetFiles(from).Select(Path.GetFileName));
    }

    [Fact]
    public void A_recording_in_use_is_skipped_not_lost()
    {
        var from = Dir("from");
        var to = Dir("to");
        var busy = Path.Combine(from, "writing.pcap");
        File.WriteAllText(Path.Combine(from, "done.pcap"), "done");
        using var open = new FileStream(busy, FileMode.Create, FileAccess.Write, FileShare.Read);   // like the recorder: others may read, not move

        Assert.Equal((1, 1), RecordingFolder.MoveRecordings(from, to));

        Assert.True(File.Exists(busy));
        Assert.True(File.Exists(Path.Combine(to, "done.pcap")));
    }

    [Fact]
    public void Moving_to_the_same_or_a_missing_folder_does_nothing()
    {
        var folder = Dir("same");
        File.WriteAllText(Path.Combine(folder, "a.pcap"), "a");
        Assert.Equal((0, 0), RecordingFolder.MoveRecordings(folder, folder));
        Assert.Equal((0, 0), RecordingFolder.MoveRecordings(Path.Combine(_root, "missing"), Dir("to")));
        Assert.Equal((1, 1L), RecordingFolder.Measure(folder));
    }
}
