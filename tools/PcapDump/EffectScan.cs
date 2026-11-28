using Aion2Meter.Capture;
using Aion2Meter.Core;
using Aion2Meter.Protocol;

/// <summary>--effects: every effect id that landed (2A 38) in the recordings and has no name yet: how often, how long, who cast it (class), on whom.</summary>
static class EffectScan
{
    public static void Run(IEnumerable<string> paths, TextWriter output)
    {
        var rows = new Dictionary<uint, Row>();
        foreach (var path in paths)
        {
            var pipeline = new Pipeline();
            var classOf = new Dictionary<uint, int>();
            foreach (var f in PcapReader.Read(path))
                foreach (var e in pipeline.Feed(f.Data, f.LinkType, f.Time))
                {
                    if (e is not MessageEvent { Message: var m }) continue;
                    if (m is IdentityMessage id) classOf[id.Entity] = id.ClassCode;
                    if (m is not EffectAppliedMessage a) continue;
                    if (EffectNames.Known(a.EffectId) is not null || BuffEffectNames.Of(a.EffectId) is not null) continue;
                    if (!rows.TryGetValue(a.EffectId, out var r)) rows[a.EffectId] = r = new Row();
                    r.Count++; if (classOf.ContainsKey(a.Target)) r.OnPlayer++; if (a.Source == a.Target) r.Self++;
                    r.Durations[a.DurationMs] = r.Durations.GetValueOrDefault(a.DurationMs) + 1;
                    r.Kinds.Add(a.Kind);
                    r.Targets.Add(a.Target);
                    r.Sources.Add(classOf.TryGetValue(a.Source, out var c) ? c : -1);
                    r.Files.Add(Path.GetFileName(path));
                }
        }
        output.WriteLine($"{rows.Count} unnamed effect ids");
        foreach (var (id, r) in rows.OrderByDescending(x => x.Value.Count))
            output.WriteLine($"{id,10} skill~{id / 10} cls{id / 10 / 1000000} x{r.Count,-4} self{100 * r.Self / r.Count,3}% onPlayer{100 * r.OnPlayer / r.Count,3}% targets {r.Targets.Count,-3} dur {string.Join(",", r.Durations.OrderByDescending(d => d.Value).Take(3).Select(d => $"{d.Key / 1000.0:0.#}s*{d.Value}"))} kind {string.Join("/", r.Kinds)} srcClass {string.Join(",", r.Sources.OrderBy(x => x))} files {r.Files.Count}");
    }

    sealed class Row
    {
        public int Count, Self, OnPlayer;
        public Dictionary<uint, int> Durations = [];
        public HashSet<byte> Kinds = [];
        public HashSet<uint> Targets = [];
        public SortedSet<int> Sources = [];
        public HashSet<string> Files = [];
    }
}
