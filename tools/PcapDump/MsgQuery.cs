using System.Text;
using Aion2Meter.Capture;
using Aion2Meter.Protocol;

/// <summary>
/// --q: search the game messages of recordings without reading the pcap again each time.
/// The first run reads a recording and writes its messages (time, opcode, hex) to a cache file in %TEMP%\a2rank-msgcache; every run after that reads only the cache
/// (a recording that grew since gets a new cache). Several recordings can be given; the times are Korean time (HH:mm:ss.fff).
/// Usage: --q &lt;pcap|.msgs ...&gt; [--op 0438] [--from HH:mm[:ss]] [--to HH:mm[:ss]] [--has hex] [--text 아라] [--max 40] [--count]
///   --op     opcode (the first two bytes) as hex, e.g. 0438 or 578d      --has   bytes as hex that must be inside the message (at a byte boundary)
///   --text   a text that must be inside (UTF-8)                          --count only how many per opcode      --max  at most N lines (default 40)
/// </summary>
static class MsgQuery
{
    private static readonly string CacheDir = Path.Combine(Path.GetTempPath(), "a2rank-msgcache");

    public static void Run(string[] args, TextWriter output)
    {
        var files = new List<string>();
        string? op = null, from = null, to = null, has = null, text = null;
        int max = 40;
        bool count = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--op": op = args[++i].ToLowerInvariant(); break;
                case "--from": from = args[++i]; break;
                case "--to": to = args[++i]; break;
                case "--has": has = args[++i].ToLowerInvariant(); break;
                case "--text": text = args[++i]; break;
                case "--max": max = int.Parse(args[++i]); break;
                case "--count": count = true; break;
                default: files.Add(args[i]); break;
            }
        }
        if (files.Count == 0) { output.WriteLine("usage: --q <pcap|.msgs ...> [--op 0438] [--from HH:mm] [--to HH:mm] [--has hex] [--text str] [--max N] [--count]"); return; }
        string? textHex = text is null ? null : Convert.ToHexString(Encoding.UTF8.GetBytes(text)).ToLowerInvariant();
        var perOp = new SortedDictionary<string, int>();
        int shown = 0, matched = 0;
        foreach (var file in files)
        {
            foreach (var line in Lines(file, output))
            {
                int a = line.IndexOf('\t'), b = line.IndexOf('\t', a + 1);
                string time = line[..a], code = line[(a + 1)..b], hex = line[(b + 1)..];
                if (op is not null && !code.StartsWith(op, StringComparison.Ordinal)) continue;
                if (from is not null && string.CompareOrdinal(time, from) < 0) continue;
                if (to is not null && string.CompareOrdinal(time, to.Length < 12 ? to + "~" : to) > 0) continue;   // "02:21" includes the whole minute
                if (has is not null && !Contains(hex, has)) continue;
                if (textHex is not null && !Contains(hex, textHex)) continue;
                matched++;
                perOp[code] = perOp.GetValueOrDefault(code) + 1;
                if (!count && shown < max)
                {
                    output.WriteLine($"{time} op={code} len={hex.Length / 2} {(hex.Length > 240 ? hex[..240] + "…" : hex)}");
                    shown++;
                }
            }
        }
        output.WriteLine($"-- {matched} messages" + (shown < matched && !count ? $" (first {shown} shown, --max for more)" : ""));
        if (count || matched > shown) output.WriteLine("   per opcode: " + string.Join("  ", perOp.OrderByDescending(x => x.Value).Take(20).Select(x => $"{x.Key}={x.Value}")));
    }

    private static bool Contains(string hex, string needle)
    {
        for (int i = hex.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = hex.IndexOf(needle, i + 1, StringComparison.Ordinal))
            if (i % 2 == 0) return true;
        return false;
    }

    /// <summary>The lines "time TAB opcode TAB hex" of a recording (cached) or of a .msgs file.</summary>
    private static IEnumerable<string> Lines(string file, TextWriter output)
    {
        if (file.EndsWith(".msgs", StringComparison.OrdinalIgnoreCase)) return File.ReadLines(file, Encoding.UTF8);
        var info = new FileInfo(file);
        Directory.CreateDirectory(CacheDir);
        string cache = Path.Combine(CacheDir, $"{info.Name}-{info.Length}-{info.LastWriteTimeUtc.Ticks}.msgs");
        if (!File.Exists(cache))
        {
            output.WriteLine($"(reading {info.Name} once: {info.Length / 1048576} MB, the next runs use the cache)");
            // a recording that is still being written is opened by the app for writing: copy it with a share mode that allows that, then read the copy
            string copy = cache + ".pcap";
            using (var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = File.Create(copy)) src.CopyTo(dst);
            string part = cache + ".part";
            using (var w = new StreamWriter(part, false, new UTF8Encoding(false)))
            {
                var flows = new Dictionary<FlowKey, TcpFlow>();
                foreach (var frame in PcapReader.Read(copy))
                {
                    var segment = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
                    if (segment is null) continue;
                    if (!flows.TryGetValue(segment.Flow, out var flow)) flows[segment.Flow] = flow = new TcpFlow();
                    string time = frame.Time.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss.fff");
                    foreach (var m in flow.Feed(segment).Messages)
                        if (m.Length >= 2) w.WriteLine($"{time}\t{m[0]:x2}{m[1]:x2}\t{Convert.ToHexString(m).ToLowerInvariant()}");
                }
            }
            File.Move(part, cache, true);
            File.Delete(copy);
        }
        return File.ReadLines(cache, Encoding.UTF8);
    }
}
