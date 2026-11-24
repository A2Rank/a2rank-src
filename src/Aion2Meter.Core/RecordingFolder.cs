namespace Aion2Meter.Core;

/// <summary>The folder raw packet recordings (.pcap) are written to: checking that it is usable and moving existing recordings.</summary>
public static class RecordingFolder
{
    /// <returns>null when files can be written there (the folder is created if needed), otherwise the reason it cannot.</returns>
    public static string? Check(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return e.Message;
        }
    }

    /// <summary>
    /// Moves the .pcap files of <paramref name="from"/> into <paramref name="to"/> (other files stay). A file that cannot be moved
    /// (for example the recording being written right now) is skipped; a name that already exists in the target is never overwritten.
    /// </summary>
    public static (int Moved, int Skipped) MoveRecordings(string from, string to)
    {
        if (!Directory.Exists(from) || Path.GetFullPath(from).TrimEnd('\\') == Path.GetFullPath(to).TrimEnd('\\')) return (0, 0);
        Directory.CreateDirectory(to);
        int moved = 0, skipped = 0;
        foreach (var file in Directory.GetFiles(from, "*.pcap"))
        {
            try
            {
                File.Move(file, FreeName(to, Path.GetFileName(file)));
                moved++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }
        return (moved, skipped);
    }

    /// <summary>The recordings of a folder are kept up to this much; past it the oldest are deleted (<see cref="Trim"/>).</summary>
    public const long LimitBytes = 300L * 1024 * 1024;

    /// <summary>A recording that has grown this big is ended and a new file started, so that old ones can be deleted in pieces of a sensible size.</summary>
    public const long MaxFileBytes = 100L * 1024 * 1024;

    /// <summary>
    /// Deletes the oldest .pcap files of <paramref name="folder"/> until what is left fits in <paramref name="limitBytes"/>. The file being written
    /// (<paramref name="keep"/>) is never deleted, and neither is one that cannot be (in use): the next oldest goes instead. Other files stay.
    /// </summary>
    /// <returns>How many files were deleted and how many bytes that freed.</returns>
    public static (int Deleted, long Freed) Trim(string folder, long limitBytes, string? keep = null)
    {
        if (!Directory.Exists(folder)) return (0, 0);
        string? keepFull = keep is null ? null : Path.GetFullPath(keep);
        var files = new List<(string Path, DateTime Time, long Length)>();
        foreach (var path in Directory.GetFiles(folder, "*.pcap"))
        {
            try
            {
                var info = new FileInfo(path);
                files.Add((path, info.LastWriteTimeUtc, info.Length));
            }
            catch (IOException) { }
        }
        long total = files.Sum(f => f.Length);
        int deleted = 0;
        long freed = 0;
        foreach (var (path, _, length) in files.OrderBy(f => f.Time).ThenBy(f => f.Path, StringComparer.Ordinal))
        {
            if (total <= limitBytes) break;
            if (keepFull is not null && string.Equals(Path.GetFullPath(path), keepFull, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                File.Delete(path);
                total -= length;
                freed += length;
                deleted++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // in use by something else: leave it, the next one goes instead
            }
        }
        return (deleted, freed);
    }

    /// <summary>Total size and number of the .pcap files in a folder.</summary>
    public static (int Count, long Bytes) Measure(string folder)
    {
        if (!Directory.Exists(folder)) return (0, 0);
        var files = Directory.GetFiles(folder, "*.pcap");
        return (files.Length, files.Sum(f => new FileInfo(f).Length));
    }

    private static string FreeName(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
        return path;
    }
}
