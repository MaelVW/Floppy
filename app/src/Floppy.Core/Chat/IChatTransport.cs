namespace Floppy.Core.Chat;

public enum ChatMode
{
    /// <summary>Ueber den Gratis-Dienst (ntfy.sh) - funktioniert ueberall mit Internet.</summary>
    Online,

    /// <summary>Direkt von PC zu PC im selben Netzwerk - ohne Dienst, ohne Limit.</summary>
    Local,
}

public enum ChatLinkState
{
    Connecting,
    Connected,

    /// <summary>Kurz weg, wird automatisch neu verbunden.</summary>
    Reconnecting,

    /// <summary>Dienst lehnt ab (Tageslimit / zu viele Nachrichten).</summary>
    Limited,

    Failed,
    Closed,
}

/// <summary>
/// Ein Weg fuer verschluesselte Nachrichten-Pakete (siehe <see cref="ChatFrame"/>). Der Weg
/// sieht nur Datensalat; Pruefen und Entschluesseln macht die <see cref="ChatSession"/>.
/// Ereignisse kommen aus Hintergrund-Threads.
/// </summary>
public interface IChatTransport : IDisposable
{
    ChatMode Mode { get; }

    event Action<byte[], object?>? FrameReceived;
    event Action<ChatLinkState, string?>? StateChanged;

    void Start();

    /// <summary>Senden (kehrt sofort zurueck, Reihenfolge bleibt erhalten).</summary>
    void Send(byte[] frame);

    /// <summary>Lokal: gepruefte Nachricht an alle anderen weiterreichen. Online: nichts.</summary>
    void Relay(byte[] frame, object? origin);

    /// <summary>Lokal: Verbindung trennen, die Unsinn schickt. Online: nichts.</summary>
    void Reject(object? origin);

    /// <summary>Noch ausstehende Nachrichten kurz zu Ende senden, dann schliessen.</summary>
    void Close(TimeSpan flush);
}

/// <summary>Der Weg im lokalen Netzwerk: jeder PC lauscht und verbindet sich mit den anderen.</summary>
public interface ILocalChatTransport : IChatTransport
{
    /// <summary>Eigene Adressen (<c>192.168.1.20:45817</c>), gueltig nach <see cref="IChatTransport.Start"/>.</summary>
    IReadOnlyList<string> Endpoints { get; }

    int PeerCount { get; }

    /// <summary>Mit mindestens einer der Adressen verbinden. true = geklappt.</summary>
    Task<bool> ConnectAsync(IReadOnlyList<string> endpoints, TimeSpan timeout);
}

/// <summary>Erzeugt die Wege (austauschbar fuer Tests oder einen eigenen Server).</summary>
public interface IChatNetwork
{
    /// <summary>Anzeigename des Dienstes, z. B. <c>ntfy.sh</c>.</summary>
    string ServiceName { get; }

    IChatTransport CreateOnline(ChatRoomKey room);

    ILocalChatTransport CreateLocal(ChatRoomKey room);

    /// <summary>Gibt es im lokalen Netz schon diesen Raum? Liefert gefundene Adressen (oder leer).</summary>
    Task<IReadOnlyList<string>> ProbeLocalAsync(ChatRoomKey room, TimeSpan timeout);
}
