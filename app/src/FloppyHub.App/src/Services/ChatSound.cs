using Godot;

namespace FloppyHub.App.Services;

/// <summary>Der kleine "Ding-Dong" fuer neue Nachrichten - aus Code erzeugt, es gibt keine Audiodatei.</summary>
public static class ChatSound
{
    private static AudioStreamWav? _ping;

    public static AudioStreamWav Ping => _ping ??= Make();

    /// <summary>Spielt den Ton einmal ab; der Spieler raeumt sich danach selbst auf.</summary>
    public static void Play(Node host, float volumeDb = -6f)
    {
        var player = new AudioStreamPlayer { Stream = Ping, VolumeDb = volumeDb };
        host.AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }

    /// <summary>Zwei weiche Toene (A5, dann E6) mit kurzem Anschlag und sanftem Ausklingen, ohne Knacken am Anfang oder Ende.</summary>
    private static AudioStreamWav Make()
    {
        const int rate = 22050;
        const double seconds = 0.55;
        const double secondStart = 0.11;
        var count = (int)(rate * seconds);
        var data = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)rate;
            var second = t >= secondStart;
            var freq = second ? 1318.5 : 880.0;
            var local = second ? t - secondStart : t;
            var envelope = Math.Min(1.0, local / 0.004) * Math.Exp(-local * (second ? 10.0 : 16.0));
            var wave = Math.Sin(2 * Math.PI * freq * local) * 0.35 + Math.Sin(2 * Math.PI * freq * 2 * local) * 0.07;   // ein Hauch Oberton
            var sample = (short)Math.Clamp(wave * envelope * short.MaxValue, short.MinValue, short.MaxValue);
            data[i * 2] = (byte)(sample & 0xFF);
            data[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = data };
    }
}
