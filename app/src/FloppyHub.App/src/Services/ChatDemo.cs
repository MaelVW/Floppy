using Floppy.Core.Chat;

namespace FloppyHub.App.Services;

/// <summary>
/// Vorschau des Chats ohne Netzwerk (fuer Bildschirmfotos und Tests): zwei Mitspieler im
/// Speicher, die schreiben, vorschlagen und Ranglisten-Ergebnisse schicken.
/// </summary>
public sealed class ChatDemo
{
    public const string Secret = "Schulhof-Diskette-42";

    private readonly InMemoryChatHub _hub = new();
    private readonly List<ChatSession> _bots = [];

    public ChatSession? Tom { get; private set; }
    public ChatSession? Lea { get; private set; }

    public void Attach(ChatService chat) => chat.NetworkOverride = new InMemoryChatNetwork(_hub);

    public async Task StartBotsAsync()
    {
        var key = await Task.Run(() => ChatRoomKey.Derive(Secret)).ConfigureAwait(false);
        ChatScore Score(string id, string name, int moves, int pushes, long ms, int points) =>
            new() { Game = "diskettenlager", LevelId = id, LevelName = name, Moves = moves, Pushes = pushes, Millis = ms, Points = points };

        Tom = new ChatSession(ChatIdentity.CreateNew(), key, new InMemoryChatNetwork(_hub), () =>
        [
            Score("5D1A0C7E22B4", "Erste Diskette", 3, 1, 2400, 976),
            Score("9F3B61C0DA17", "Zwei Laufwerke", 19, 4, 21000, 1838),
            Score("C4E2197A5B30", "Tims Turm", 44, 9, 71000, 2593),
        ]);
        Lea = new ChatSession(ChatIdentity.CreateNew(), key, new InMemoryChatNetwork(_hub), () =>
        [
            Score("5D1A0C7E22B4", "Erste Diskette", 3, 1, 1900, 978),
            Score("9F3B61C0DA17", "Zwei Laufwerke", 23, 5, 18000, 1834),
        ]);
        _bots.Add(Tom);
        _bots.Add(Lea);
    }

    public void StartBots(DateTimeOffset now)
    {
        foreach (var bot in _bots) bot.Start(now);
    }

    public void Pump()
    {
        foreach (var bot in _bots) bot.Pump(DateTimeOffset.UtcNow);
    }
}
