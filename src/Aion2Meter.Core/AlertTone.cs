using System.Text;

namespace Aion2Meter.Core;

/// <summary>The app's own alert sound as a WAV made in memory (no sound file, no Windows system sound).</summary>
public static class AlertTone
{
    private const int Rate = 44100;

    /// <summary>A very short single note (about 0.15 s, E6, quick decay) for the target alert; <paramref name="volume"/> is 0..100.</summary>
    public static byte[] BuildShort(int volume = 60)
    {
        var samples = new short[(int)(0.2 * Rate)];
        double gain = Math.Pow(Math.Clamp(volume, 0, 100) / 100.0, 1.5);
        for (int i = 0; i < samples.Length; i++)
        {
            double x = (double)i / Rate;
            double envelope = Math.Min(1, x / 0.004) * Math.Exp(-x * 22);
            double wave = Math.Sin(2 * Math.PI * 1318.5 * x) + 0.3 * Math.Sin(2 * Math.PI * 2637.0 * x);
            samples[i] = (short)Math.Clamp((int)(wave * envelope * 14000 * gain), short.MinValue, short.MaxValue);
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
