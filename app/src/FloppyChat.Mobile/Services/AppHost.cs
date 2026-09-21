using Floppy.Chat.Client;

namespace FloppyChat.Mobile.Services;

/// <summary>
/// Die eine Chat-Sitzung der App samt Einstellungen und Takt. Wird beim Start (BootPage) einmal aufgebaut
/// und lebt, solange die App laeuft: ein Seitenwechsel oder Drehen des Handys trennt den Chat nicht.
/// </summary>
internal static class AppHost
{
    private static IDispatcherTimer? _timer;

    public static ChatSettings Settings { get; } = new();

    /// <summary>Erst nach <see cref="StartAsync"/> benutzen.</summary>
    public static ChatClient Chat { get; private set; } = null!;

    public static bool IsStarted => _timer is not null;

    /// <summary>Chat anlegen, Identitaet laden (im Hintergrund) und den 4-mal-pro-Sekunde-Takt starten (die Sprache setzt <see cref="App"/>).</summary>
    public static async Task StartAsync()
    {
        if (IsStarted) return;

        Chat = new ChatClient(Path.Combine(FileSystem.AppDataDirectory, "chat"), Settings, new SecureStorageProtector());
        await Task.Run(Chat.EnsureIdentity);   // Schluesselbund + Datei: nicht im UI-Thread

        var timer = Application.Current!.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.Tick += (_, _) => Chat.Pump();
        timer.Start();
        _timer = timer;
    }

    /// <summary>Beim Beenden der App: tschuess sagen und kurz warten, bis es raus ist.</summary>
    public static void Shutdown()
    {
        _timer?.Stop();
        Chat?.Shutdown();
    }
}
