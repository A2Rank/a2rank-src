// Lists every game message in a pcap recording, for working out unknown messages (docs/PROTOCOL.md).
// Usage: dotnet run --project tools/PcapDump -- <file.pcap> [text to look for, e.g. a character name ...]
//        dotnet run --project tools/PcapDump -- <file.pcap> --session   (what the meter would count)
// Messages containing a search text are marked with its name and the byte offset.
using System.Text;
using Aion2Meter.Capture;
using Aion2Meter.Protocol;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: PcapDump <file.pcap> [search text ...]");
    return 1;
}
if (args.Length >= 1 && args[0] == "--near-points")
{
    NearPoints.Run(args.Skip(1), Console.Out);
    return 0;
}
if (args.Length >= 2 && args[0] == "--skills")
{
    SkillSummary.Run(args[1], Console.Out);
    return 0;
}
if (args.Length >= 4 && args[0] == "--windows-replay")
{
    WindowsReplay.Run(args[1], args[2], args.Skip(3), Console.Out);
    return 0;
}
if (args.Length >= 2 && args[0] == "--rank-scan")
{
    RankScan.Run(args.Skip(1), Console.Out);
    return 0;
}
if (args.Length >= 2 && args[0] == "--skills-any")
{
    SkillsAny.Run(args[1], args.Skip(2), Console.Out);
    return 0;
}
if (args.Length >= 2 && args[0] == "--q")
{
    MsgQuery.Run(args.Skip(1).ToArray(), Console.Out);
    return 0;
}
if (args.Length >= 2 && args[0] == "--heals")
{
    Heals.Run(args[1], args.Length > 2 ? args[2] : "00:00", args.Length > 3 ? args[3] : "99:99", Console.Out);
    return 0;
}
if (args.Length >= 3 && args[0] == "--periodic")
{
    Periodic.Run(args[1], args[2], args.Length > 3 && args[3] == "list", Console.Out);
    return 0;
}
if (args.Length >= 3 && args[0] == "--skills-table")
{
    SkillsTable.Run(args[1], args[2], Console.Out);
    return 0;
}
if (args.Length >= 2 && args[0] == "--damage")
{
    DamageSummary.Run(args[1], Console.Out);
    return 0;
}
if (args.Length >= 1 && args[0] == "--enter-totals")
{
    EnterTotals.Run(args.Skip(1), Console.Out);
    return 0;
}
if (args.Length >= 4 && args[0] == "--enter-hex")
{
    EnterHex.Run(args[1], args[2], ulong.Parse(args[3]), Console.Out);
    return 0;
}
if (args.Length >= 3 && args[0] == "--hex")
{
    FullHex.Run(args[1], args[2], args.Length > 3 ? args[3] : null, Console.Out);
    return 0;
}
if (args.Length >= 3 && args[0] == "--taken")
{
    TakenList.Run(args[1], args[2], Console.Out);
    return 0;
}
if (args.Length >= 5 && args[0] == "--hits")
{
    HitsSummary.Run(args[1], uint.Parse(args[2]), args[3], args[4], Console.Out);
    return 0;
}
if (args.Length >= 3 && args[0] == "--rare")
{
    RareOps.Run(args[1], int.Parse(args[2]), args.Length > 3 ? args[3] : null, Console.Out);
    return 0;
}
if (args.Length >= 4 && args[0] == "--grep")
{
    GrepBytes.Run(args[1], args[2], args[3], Console.Out);
    return 0;
}
if (args.Length >= 4 && args[0] == "--by")
{
    ByKey.Run(args[1], args[2], Convert.ToUInt32(args[3], 16), Console.Out);
    return 0;
}
if (args.Length >= 1 && args[0] == "--find-totals")
{
    TotalSearch.Run(args.Skip(1), Console.Out);
    return 0;
}
if (args.Length >= 2 && args[0] == "--bench")
{
    Bench.Run(args[1], Console.Out);
    return 0;
}
if (args.Length >= 2 && args[0] == "--effects")
{
    EffectScan.Run(args.Skip(1), Console.Out);
    return 0;
}
if (args.Length >= 1 && args[0] == "--identities")
{
    Identities.Run(args.Skip(1), Console.Out);
    return 0;
}
if (args.Length >= 2 && args[1] == "--session")
{
    Replay.Run(args[0], Console.Out, args.Length >= 3 ? args[2] : null);
    return 0;
}
var needles = args.Skip(1).Select(s => (Text: s, Bytes: Encoding.UTF8.GetBytes(s))).ToList();
var flows = new Dictionary<FlowKey, TcpFlow>();
var counts = new Dictionary<string, int>();
var output = Console.Out;

foreach (var frame in PcapReader.Read(args[0]))
{
    var segment = PacketParser.Parse(frame.Data, frame.LinkType, frame.Time);
    if (segment is null) continue;
    if (!flows.TryGetValue(segment.Flow, out var flow)) flows[segment.Flow] = flow = new TcpFlow();
    if (segment.Syn) output.WriteLine($"{Stamp(frame.Time)} SYN {segment.Flow.Destination}:{segment.Flow.DestinationPort}");
    var (messages, broken) = flow.Feed(segment);
    if (broken) output.WriteLine($"{Stamp(frame.Time)} BROKEN gen={flow.Generation}");
    foreach (var m in messages)
    {
        string op = m.Length >= 2 ? $"{m[0]:x2} {m[1]:x2}" : $"{m[0]:x2}";
        counts[op] = counts.GetValueOrDefault(op) + 1;
        var parsed = MessageParser.Parse(m);
        var hits = needles.Select(n => (n.Text, At: m.AsSpan().IndexOf(n.Bytes))).Where(h => h.At >= 0)
            .Select(h => $"[{h.Text}@{h.At}]").ToList();
        // Frequent unparsed traffic is only shown when it contains a search text.
        if (parsed is null && hits.Count == 0 && counts[op] > 3) continue;
        output.WriteLine($"{Stamp(frame.Time)} {Port(segment.Flow)} op={op} len={m.Length} {string.Join(' ', hits)} {Describe(parsed)}");
        output.WriteLine("    " + Convert.ToHexString(m.AsSpan(0, Math.Min(m.Length, 160))).ToLowerInvariant());
    }
    if (segment.FinOrRst) output.WriteLine($"{Stamp(frame.Time)} CLOSED {Port(segment.Flow)}");
}
output.WriteLine("---- message counts by first two bytes");
foreach (var (op, n) in counts.OrderByDescending(c => c.Value)) output.WriteLine($"{op}  {n}");
return 0;

static string Stamp(DateTimeOffset t) => t.ToOffset(TimeSpan.FromHours(9)).ToString("HH:mm:ss.fff");
static string Port(FlowKey f) => $"#{f.DestinationPort}";
static string Describe(GameMessage? m) => m switch
{
    null => "",
    PointMessage p => $"POINT total={p.Total}",
    EnterWorldMessage w => $"ENTER entity={w.Entity} {w.Name}/{w.Server}",
    DeathMessage d => $"DEATH victim={d.Victim} killer={d.Killer} {d.KillerName}/{d.KillerServer} race={d.KillerRace} pvp={d.Pvp}",
    DamageMessage d => $"DAMAGE target={d.Target} source={d.Source} flags={d.Flags:x2} skill={d.Skill} total={d.Total} main={d.Main} extras=[{string.Join(',', d.Extras)}]",
    IdentityMessage i => $"IDENTITY entity={i.Entity} {i.Name}/{i.Server} race={i.Race} lv={i.Level}",
    RankingMessage r => $"RANKING list={r.IsList} filter={r.Filter} n={r.Entries.Count} ranks={r.Entries.FirstOrDefault()?.Rank}-{r.Entries.LastOrDefault()?.Rank} total={r.Total} last={r.Entries.LastOrDefault()?.Name}:{r.Entries.LastOrDefault()?.Score} first={r.Entries.FirstOrDefault()?.Name}/{r.Entries.FirstOrDefault()?.Server} id={r.Entries.FirstOrDefault()?.CharacterId} score={r.Entries.FirstOrDefault()?.Score} rank={r.Entries.FirstOrDefault()?.Rank} deadline={r.Deadline.ToOffset(TimeSpan.FromHours(9)):HH:mm:ss}",
    _ => m.ToString(),
};
