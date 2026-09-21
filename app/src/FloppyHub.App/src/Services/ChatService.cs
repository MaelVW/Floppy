using System.Collections.Concurrent;
using Floppy.Core;
using Floppy.Core.Chat;
using Floppy.Core.Minigame;
using FloppyHub.App.Core;

namespace FloppyHub.App.Services;

/// <summary>
/// Haelt die Chat-Sitzung ueber die ganze Laufzeit der App (ein Wechsel von Farbschema oder
/// Ansicht baut die Oberflaeche neu, der Chat bleibt verbunden). Gespeichert werden nur die
/// eigene Identitaet und die Kontakte - nie der Verlauf.
/// </summary>
public sealed class ChatService : IDisposable
{
    private readonly AppServices _s;
    private readonly ConcurrentQueue<Action> _main = new();
    private ChatIdentity? _identity;
    private IReadOnlyList<ChatContact>? _contacts;
    private int _connectTicket;
    private int _lastVersion = -1;

    public ChatService(AppServices services)
    {
        _s = services;
        Book = new ChatContactBook(Path.Combine(services.Paths.ChatDir, ChatContactBook.FileName));
        Bans = new ChatBanList(services.ReadOnlyMode ? null : Path.Combine(services.Paths.ChatDir, ChatBanList.FileName));
    }

    /// <summary>Vorschau/Test: anderes Netz statt ntfy + LAN.</summary>
    public IChatNetwork? NetworkOverride { get; set; }

    /// <summary>Vorschau/Test: andere Admins als die fest eingetragenen (nur fuer Bildschirmfotos).</summary>
    public Func<string, bool> AdminCheck { get; set; } = ChatAdmins.IsAdmin;

    public ChatContactBook Book { get; }

    /// <summary>Sperren im offenen Chat (kommen nur als Admin-Nachricht, bleiben ueber Neustarts).</summary>
    public ChatBanList Bans { get; }
    public ChatSession? Session { get; private set; }

    /// <summary>Verschluesselung wird gerade abgeleitet (dauert einen Moment).</summary>
    public bool IsDeriving { get; private set; }

    /// <summary>Anzeigename des Raums ("Offener Chat", Kontaktname oder Pruefzahl).</summary>
    public string RoomLabel { get; private set; } = "";

    /// <summary>Verschluesselung des aktuellen Raums - nur im Speicher, fuer "Als Kontakt speichern".</summary>
    public string? RoomSecret { get; private set; }

    public bool IsInRoom => Session is { State: not ChatSessionState.Ended };

    /// <summary>Offener Chat: Standardraum ohne eigene Verschluesselung, zum Reinschnuppern.</summary>
    public bool IsOpenRoom { get; private set; }

    /// <summary>Etwas hat sich geaendert (im UI-Thread).</summary>
    public event Action? Changed;

    /// <summary>
    /// Eine Nachricht von jemand anderem ist da, die laut Einstellung melden soll (Ton/Taskleiste).
    /// Zweiter Wert: der eigene Name kommt darin vor.
    /// </summary>
    public event Action<ChatLine, bool>? MessageReceived;

    /// <summary>Mein Anzeigename aus den Einstellungen (gesaeubert; Admins duerfen "Admin" im Namen tragen) - null = keiner.</summary>
    public string? MyAlias => ChatProfile.Clean(_s.Settings.ChatAlias, allowReserved: IAmAdmin);

    /// <summary>Meine Namensfarbe: 0 = automatisch, 1 bis 8.</summary>
    public int MyColor => ChatProfile.CleanColor(_s.Settings.ChatColor);

    public ChatIdentity Identity => _identity ??= LoadIdentity();

    public IReadOnlyList<ChatContact> Contacts => _contacts ??= Book.Load();

    private ChatIdentity LoadIdentity()
    {
        if (_s.ReadOnlyMode) return ChatIdentity.CreateNew();
        try
        {
            return ChatIdentity.LoadOrCreate(Path.Combine(_s.Paths.ChatDir, ChatIdentity.FileName));
        }
        catch (Exception ex)
        {
            _s.Log.Write(LogLevel.Warn, $"App: Chat-ID konnte nicht gespeichert werden: {ex.Message}");
            return ChatIdentity.CreateNew();
        }
    }

    // ------------------------------------------------------------------
    // Raum betreten / verlassen
    // ------------------------------------------------------------------

    /// <param name="label">Anzeigename; null = Pruefzahl des Raums.</param>
    public void Connect(string secret, string? label = null)
    {
        if (ChatRoomKey.Problem(secret) != SecretProblem.None) return;
        LeaveCurrent(wait: false);

        var normalized = ChatRoomKey.Normalize(secret);
        var open = normalized == ChatRoomKey.OpenSecret;
        var ticket = ++_connectTicket;
        IsDeriving = true;
        RoomLabel = open ? Loc.T("CHAT_OPEN_ROOM") : label ?? "";
        RoomSecret = open ? null : normalized;
        IsOpenRoom = open;
        Session = null;
        Raise();

        _ = Identity;   // Datei im UI-Thread laden
        Task.Run(() =>
        {
            ChatRoomKey? key = null;
            string? error = null;
            try { key = open ? ChatRoomKey.Open : ChatRoomKey.Derive(normalized); }
            catch (Exception ex) { error = ex.Message; }

            _main.Enqueue(() =>
            {
                if (ticket != _connectTicket) return;   // inzwischen abgebrochen
                IsDeriving = false;
                if (error is not null) Godot.GD.PushWarning($"Chat: Schluessel nicht ableitbar: {error}");
                if (key is not null) StartSession(key);
                Raise();
            });
        });
    }

    /// <summary>
    /// Auch wer im Offenen Chat gesperrt ist, wird verbunden (still, siehe <see cref="ChatSession.IsBanned"/>) -
    /// sonst koennte er nie erfahren, dass die Sperre aufgehoben wurde.
    /// </summary>
    public void ConnectOpen()
    {
        if (ChatRoomKey.OpenRoomAvailable) Connect(ChatRoomKey.OpenSecret);
    }

    private void StartSession(ChatRoomKey key)
    {
        var network = NetworkOverride ?? new DefaultChatNetwork(DefaultChatNetwork.ParseServer(_s.Settings.ChatServer));
        if (RoomLabel.Length == 0) RoomLabel = Loc.T("CHAT_ROOM_CHECK", key.Check);
        var session = new ChatSession(Identity, key, network, OwnScores, Bans, fingerprint => AdminCheck(fingerprint));
        Session = session;
        _lastVersion = -1;
        session.SetProfile(MyAlias, MyColor, DateTimeOffset.UtcNow);   // schon der Beitritt traegt Name und Farbe
        session.LineAdded += line => OnLineAdded(session, line);
        session.Start(DateTimeOffset.UtcNow);
    }

    private void OnLineAdded(ChatSession session, ChatLine line)
    {
        if (!ReferenceEquals(session, Session) || line.Kind != ChatLineKind.Theirs) return;
        var mention = ChatProfile.Mentions(line.Text, MyAlias, Identity.Id);
        if (ChatProfile.ShouldNotify(_s.Settings.ChatNotify, mention)) MessageReceived?.Invoke(line, mention);
    }

    /// <summary>Anzeigename/Farbe aus den Einstellungen in den laufenden Chat uebernehmen (die anderen erfahren es sofort).</summary>
    public void ApplyProfile()
    {
        Session?.SetProfile(MyAlias, MyColor, DateTimeOffset.UtcNow);
        Raise();
    }

    /// <summary>Raum verlassen - der Verlauf bleibt sichtbar, bis ein neuer Raum betreten wird.</summary>
    public void Leave()
    {
        LeaveCurrent(wait: false);
        Raise();
    }

    /// <summary>Zurueck zur Eingabe der Verschluesselung (alter Verlauf weg).</summary>
    public void Reset()
    {
        LeaveCurrent(wait: false);
        Session = null;
        RoomSecret = null;
        IsOpenRoom = false;
        RoomLabel = "";
        Raise();
    }

    private void LeaveCurrent(bool wait)
    {
        _connectTicket++;
        IsDeriving = false;
        if (Session is { State: not ChatSessionState.Ended } session) session.Leave(DateTimeOffset.UtcNow, wait);
    }

    /// <summary>Beim Beenden der App: tschuess sagen und kurz warten, bis es raus ist.</summary>
    public void Shutdown() => LeaveCurrent(wait: true);

    public void Dispose() => Shutdown();

    /// <summary>Jeden Frame aus dem UI-Thread.</summary>
    public void Pump()
    {
        var changed = false;
        while (_main.TryDequeue(out var action))
        {
            action();
            changed = true;
        }
        if (Session is { } session)
        {
            session.Pump(DateTimeOffset.UtcNow);
            if (session.Version != _lastVersion)
            {
                _lastVersion = session.Version;
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

    // ------------------------------------------------------------------
    // Kontakte + Namen
    // ------------------------------------------------------------------

    public ChatContact? ContactOf(string? fingerprint) =>
        fingerprint is null ? null : Contacts.FirstOrDefault(c => string.Equals(c.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));

    public bool IsAdmin(string? fingerprint) => fingerprint is not null && AdminCheck(fingerprint);

    /// <summary>Diese Installation ist Admin.</summary>
    public bool IAmAdmin => IsAdmin(Identity.Fingerprint);

    /// <summary>
    /// "Du", Kontaktname (den ich selbst vergeben habe), der Anzeigename des anderen mit ID-Endung ("Tom #1417")
    /// oder die ID-Nummer - bei Admins mit vorangestelltem "(Admin)".
    /// </summary>
    public string NameOf(string? fingerprint, string? memberId)
    {
        string name;
        if (fingerprint is not null && fingerprint == Identity.Fingerprint) name = Loc.T("CHAT_YOU");
        else if (ContactOf(fingerprint) is { } contact) name = contact.Name;
        else if (Session?.ProfileOf(fingerprint).Alias is { } alias) name = $"{alias} {ChatProfile.IdTag(memberId)}";
        else name = Loc.T("CHAT_ID", memberId ?? "?");
        return IsAdmin(fingerprint) ? $"{Loc.T("CHAT_ADMIN_TAG")} {name}" : name;
    }

    /// <summary>Die gewaehlte Namensfarbe (1 bis 8) eines Mitglieds - 0 = automatisch. Meine eigene kommt aus den Einstellungen.</summary>
    public int ChosenColorOf(string? fingerprint) =>
        fingerprint is null ? 0
        : fingerprint == Identity.Fingerprint ? MyColor
        : Session?.ProfileOf(fingerprint).Color ?? 0;

    public void SaveContact(string name, string memberId, string fingerprint, bool withSecret)
    {
        if (_s.ReadOnlyMode) return;
        Book.Save(name, memberId, fingerprint, withSecret && RoomSecret is not null ? RoomSecret : null);
        _contacts = null;
        Raise();
    }

    public void RemoveContact(string fingerprint)
    {
        if (_s.ReadOnlyMode) return;
        Book.Remove(fingerprint);
        _contacts = null;
        Raise();
    }

    public IReadOnlyList<ChatScore> OwnScores() => ScoreBoard.BestForChat(_s.Paths.ScoresDir);

    private void Raise()
    {
        _lastVersion = -1;
        Changed?.Invoke();
    }
}
