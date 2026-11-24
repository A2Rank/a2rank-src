using System.Text;

namespace Aion2Meter.Core;

/// <summary>The app's own alert sound as a WAV made in memory (no sound file, no Windows system sound).</summary>
public static class AlertTone
{
    private const int Rate = 44100;

    /// <summary>A very short single note (about 0.15 s, E6, quick decay) for the target alert; <paramref name="volume"/> is 0..100.</summary>
    public static byte[] BuildShort(int volume = 60, double frequency = 1318.5)
    {
        var samples = new short[(int)(0.2 * Rate)];
        double gain = Math.Pow(Math.Clamp(volume, 0, 100) / 100.0, 1.5);
        for (int i = 0; i < samples.Length; i++)
        {
            double x = (double)i / Rate;
            double envelope = Math.Min(1, x / 0.004) * Math.Exp(-x * 22);
            double wave = Math.Sin(2 * Math.PI * frequency * x) + 0.3 * Math.Sin(2 * Math.PI * frequency * 2 * x);
            samples[i] = (short)Math.Clamp((int)(wave * envelope * 14000 * gain), short.MinValue, short.MaxValue);
        }
        return Wav(samples);
    }

    /// <summary>
    /// A short "띵동" for the debuff alert (about 0.7 s): a high bell note (E6) and a lower one (B5) right after it, each with a ringing decay and a bit of the inharmonic overtone of a bell.
    /// It differs from the target alert (one short note) and from the boss chime (three rising notes); <paramref name="volume"/> is 0..100.
    /// </summary>
    public static byte[] BuildDingDong(int volume = 60)
    {
        double[] notes = [1318.5, 987.8];
        const double noteStart = 0.24, noteLength = 0.45, total = 0.72;
        var samples = new short[(int)(total * Rate)];
        double gain = Math.Pow(Math.Clamp(volume, 0, 100) / 100.0, 1.5);
        for (int n = 0; n < notes.Length; n++)
        {
            int first = (int)(n * noteStart * Rate), count = (int)(noteLength * Rate);
            for (int i = 0; i < count && first + i < samples.Length; i++)
            {
                double x = (double)i / Rate;
                double envelope = Math.Min(1, x / 0.004) * Math.Exp(-x * 9);                       // sharp attack, short ring
                double wave = Math.Sin(2 * Math.PI * notes[n] * x) + 0.35 * Math.Sin(2 * Math.PI * notes[n] * 2.76 * x) + 0.2 * Math.Sin(2 * Math.PI * notes[n] * 4 * x);
                int value = samples[first + i] + (int)(wave * envelope * 11000 * gain);
                samples[first + i] = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
            }
        }
        return Wav(samples);
    }

    /// <summary>
    /// A short "띠링" for the debuff alert of the one I aim at (about 0.5 s): two bell notes going up (C6, then G6, quicker than the 띵동 which goes down), so that the two are told apart by ear;
    /// <paramref name="volume"/> is 0..100.
    /// </summary>
    public static byte[] BuildRising(int volume = 60)
    {
        double[] notes = [1046.5, 1568.0];
        const double noteStart = 0.14, noteLength = 0.38, total = 0.52;
        var samples = new short[(int)(total * Rate)];
        double gain = Math.Pow(Math.Clamp(volume, 0, 100) / 100.0, 1.5);
        for (int n = 0; n < notes.Length; n++)
        {
            int first = (int)(n * noteStart * Rate), count = (int)(noteLength * Rate);
            for (int i = 0; i < count && first + i < samples.Length; i++)
            {
                double x = (double)i / Rate;
                double envelope = Math.Min(1, x / 0.003) * Math.Exp(-x * 11);
                double wave = Math.Sin(2 * Math.PI * notes[n] * x) + 0.3 * Math.Sin(2 * Math.PI * notes[n] * 2 * x);
                int value = samples[first + i] + (int)(wave * envelope * 12000 * gain);
                samples[first + i] = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
            }
        }
        return Wav(samples);
    }

    /// <summary>16-bit mono WAV: C6, E6, G6 one after the other with a bell-like decay, repeated <paramref name="times"/> times; <paramref name="volume"/> is 0..100.</summary>
    public static byte[] Build(int times = 1, int volume = 70)
    {
        double[] notes = [1046.5, 1318.5, 1568.0];
        const double noteStart = 0.16, noteLength = 0.55, chime = 0.16 * 2 + 0.55, gap = 0.35;
        times = Math.Clamp(times, 1, 5);
        double total = times * chime + (times - 1) * gap + 0.1;
        var samples = new short[(int)(total * Rate)];
        double gain = Math.Pow(Math.Clamp(volume, 0, 100) / 100.0, 1.5);   // a little softer than linear at the low end
        for (int t = 0; t < times; t++)
        {
            double offset = t * (chime + gap);
            for (int n = 0; n < notes.Length; n++)
            {
                int first = (int)((offset + n * noteStart) * Rate), count = (int)(noteLength * Rate);
                for (int i = 0; i < count && first + i < samples.Length; i++)
                {
                    double x = (double)i / Rate;
                    double envelope = Math.Min(1, x / 0.006) * Math.Exp(-x * 5.5);          // quick attack, ringing decay
                    double wave = Math.Sin(2 * Math.PI * notes[n] * x) + 0.3 * Math.Sin(2 * Math.PI * notes[n] * 2 * x);
                    int value = samples[first + i] + (int)(wave * envelope * 14000 * gain);
                    samples[first + i] = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
                }
            }
        }
        return Wav(samples);
    }

    private static byte[] Wav(short[] samples)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            int bytes = samples.Length * 2;
            w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(bytes);
            foreach (var s in samples) w.Write(s);
        }
        return stream.ToArray();
    }
}
