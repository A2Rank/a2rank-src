using Aion2Meter.Protocol;

namespace Aion2Meter.Core;

/// <summary>
/// How the hits of one direction (mine to them, or theirs to me) landed: counts of critical / smash / block / front / back /
/// multi / regen / 철벽 hits, and the life restored by them (the amount after the hit's end, e.g. a life-steal).
/// </summary>
public sealed record HitStats(int Hits = 0, int Critical = 0, int Smash = 0, int Block = 0, int Front = 0, int Back = 0,
    int Multi = 0, int Regen = 0, long Healed = 0, int Perfect = 0, int Casts = 0, long Max = 0, int Immune = 0, int PerfectBlock = 0, int Dodge = 0)
{
    public static readonly HitStats Empty = new();

    public bool IsEmpty => Hits == 0 && Dodge == 0;

    /// <summary>An attack on me that did not land (회피).</summary>
    public HitStats AddDodge() => this with { Dodge = Dodge + 1 };

    public HitStats Add(DamageMessage d) => this with
    {
        Hits = Hits + 1,
        Critical = Critical + (d.Mods.HasFlag(HitMods.Critical) ? 1 : 0),
        Smash = Smash + (d.Mods.HasFlag(HitMods.Smash) ? 1 : 0),
        Block = Block + (d.Mods.HasFlag(HitMods.Block) ? 1 : 0),
        Front = Front + (d.Mods.HasFlag(HitMods.Front) ? 1 : 0),
        Back = Back + (d.Mods.HasFlag(HitMods.HasDirection) && !d.Mods.HasFlag(HitMods.Front) ? 1 : 0),
        Multi = Multi + (d.Mods.HasFlag(HitMods.Multi) ? 1 : 0),
        Regen = Regen + (d.Mods.HasFlag(HitMods.Regen) ? 1 : 0),
        Healed = Healed + d.Healed,
        Perfect = Perfect + (d.Mods.HasFlag(HitMods.Perfect) ? 1 : 0),
        // The first hit of a skill use: for multi-hit skills one use is several hits.
        Casts = Casts + (d.HitIndex == 1 ? 1 : 0),
        Max = Math.Max(Max, d.Total),
        Immune = Immune + (d.Mods.HasFlag(HitMods.Immune) ? 1 : 0),
        PerfectBlock = PerfectBlock + (d.Mods.HasFlag(HitMods.PerfectBlock) ? 1 : 0),
    };

    public static HitStats Merge(HitStats? a, HitStats? b) => a is null ? b ?? Empty : b is null ? a : new HitStats(
        a.Hits + b.Hits, a.Critical + b.Critical, a.Smash + b.Smash, a.Block + b.Block, a.Front + b.Front, a.Back + b.Back,
        a.Multi + b.Multi, a.Regen + b.Regen, a.Healed + b.Healed, a.Perfect + b.Perfect, a.Casts + b.Casts, Math.Max(a.Max, b.Max), a.Immune + b.Immune, a.PerfectBlock + b.PerfectBlock, a.Dodge + b.Dodge);

    /// <summary>"hits,crit,smash,block,front,back,multi,regen,healed,perfect,casts,max" for the database.</summary>
    public string Encode()
    {
        if (IsEmpty) return "";
        string text = string.Join(',', Hits, Critical, Smash, Block, Front, Back, Multi, Regen, Healed, Perfect, Casts, Max);
        // the 13th field (철벽), the 14th (완벽 막기) and the 15th (회피) only when there is one, so older strings stay as they were
        return Dodge > 0 ? $"{text},{Immune},{PerfectBlock},{Dodge}" : PerfectBlock > 0 ? $"{text},{Immune},{PerfectBlock}" : Immune > 0 ? text + "," + Immune : text;
    }

    /// <summary>The inverse of <see cref="Encode"/>; empty for anything that does not parse.</summary>
    public static HitStats Decode(string? text)
    {
        var f = (text ?? "").Split(',');
        // 9 fields from before 완벽 was counted, 10 with it, 12 with the number of uses and the biggest hit, 13 with 철벽, 14 with 완벽 막기, 15 with 회피.
        if (f.Length is not (9 or 10 or 12 or 13 or 14 or 15) || !f.Take(8).All(x => int.TryParse(x, out _)) || !long.TryParse(f[8], out long healed)) return Empty;
        int[] v = [.. f.Take(8).Select(int.Parse)];
        int perfect = f.Length >= 10 && int.TryParse(f[9], out int p) ? p : 0;   // (the 12-field form had it too, but it was dropped when reading)
        int casts = f.Length >= 12 && int.TryParse(f[10], out int c2) ? c2 : 0;
        long max = f.Length >= 12 && long.TryParse(f[11], out long m2) ? m2 : 0;
        int immune = f.Length >= 13 && int.TryParse(f[12], out int i2) ? i2 : 0;
        int perfectBlock = f.Length >= 14 && int.TryParse(f[13], out int pb) ? pb : 0;
        int dodge = f.Length == 15 && int.TryParse(f[14], out int dg) ? dg : 0;
        return new HitStats(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], healed, perfect, casts, max, immune, perfectBlock, dodge);
    }
}
