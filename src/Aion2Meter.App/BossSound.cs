using System.IO;
using System.Media;
using Aion2Meter.Core;

namespace Aion2Meter.App;

/// <summary>Plays the app's own alert tone (<see cref="AlertTone"/>) at the volume set in the settings.</summary>
internal static class BossSound
{
    private static SoundPlayer? _player;   // kept until the next play: a SoundPlayer that is collected stops playing

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
