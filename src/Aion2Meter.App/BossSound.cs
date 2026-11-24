using System.IO;
using System.Media;
using Aion2Meter.Core;

namespace Aion2Meter.App;

/// <summary>Plays the app's own alert tone (<see cref="AlertTone"/>) at the volume set in the settings.</summary>
internal static class BossSound
{
    private static SoundPlayer? _player;   // kept until the next play: a SoundPlayer that is collected stops playing

    /// <summary>The tone of the debuff alert of the one I aim at: a short rising "띠링" (<see cref="AlertTone.BuildRising"/>), against the falling 띵동 of the debuffs that land on me.</summary>
    public static void PlayTargetDebuff(int volume = 60)
    {
        try
        {
            _player?.Stop();
            _player = new SoundPlayer(new MemoryStream(AlertTone.BuildRising(volume)));
            _player.Play();
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            Log.Write("sound: " + e.Message);
        }
    }

    /// <summary>The tone of the debuff alert: a short "띵동" (<see cref="AlertTone.BuildDingDong"/>), so that it is not mistaken for the target alert (one note).</summary>
    public static void PlayDebuff(int volume = 60)
    {
        try
        {
            _player?.Stop();
            _player = new SoundPlayer(new MemoryStream(AlertTone.BuildDingDong(volume)));
            _player.Play();
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            Log.Write("sound: " + e.Message);
        }
    }

    /// <summary>The short tone of the target alert (one quick note).</summary>
    public static void PlayTarget(int volume = 60)
    {
        try
        {
            _player?.Stop();
            _player = new SoundPlayer(new MemoryStream(AlertTone.BuildShort(volume)));
            _player.Play();
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            Log.Write("sound: " + e.Message);
        }
    }

    /// <summary>Plays the chime (twice for the last-minute alert); a sound problem never stops the app.</summary>
    public static void Play(int times = 1, int volume = 70)
    {
        try
        {
            _player?.Stop();
            _player = new SoundPlayer(new MemoryStream(AlertTone.Build(times, volume)));
            _player.Play();
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            Log.Write("sound: " + e.Message);
        }
    }
}
