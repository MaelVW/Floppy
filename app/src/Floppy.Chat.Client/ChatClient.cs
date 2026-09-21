using System.Collections.Concurrent;
using System.Globalization;
using Floppy.Core.Chat;

namespace Floppy.Chat.Client;

/// <summary>
/// Haelt die Chat-Sitzung ueber die ganze Laufzeit der App und macht aus dem Kern (<see cref="ChatSession"/>)
/// alles, was eine Oberflaeche braucht: Namen, Farben, fertige Verlaufszeilen, Verbindungstext, Hinweise.
/// Das Gegenstueck zum <c>ChatService</c> der Desktop-App, aber ohne Godot - und ohne Kontakte
/// (die kommen spaeter). Gespeichert wird nur die eigene Identitaet (und die Sperrliste), nie der Verlauf.
///
/// Nicht threadsicher: alle Aufrufe aus dem Oberflaechen-Thread; <see cref="Pump"/> regelmaessig (etwa 4 x pro Sekunde).
/// </summary>
public sealed class ChatClient : IDisposable
{
    private readonly ConcurrentQueue<Action> _main = new();
    private readonly object _identityLock = new();
    private readonly string _dataDir;
    private readonly IChatSettings _settings;
    private readonly ISecretProtector? _protector;
    private readonly IChatNetwork? _network;
    private readonly Func<DateTimeOffset> _clock;
    private ChatIdentity? _identity;
    private ChatBanList? _bans;
    private int _connectTicket;
    private int _lastVersion = -1;

    // Stand der Verlaufsanzeige (siehe ReadNewRows)
    private ChatSession? _feedSession;
    private int _feedCount;
    private ChatLine? _feedLast;
    private bool _feedRestart = true;

    /// <param name="dataDir">Ordner fuer Identitaet und Sperrliste (dauerhaft, nur fuer diese App).</param>
    /// <param name="protector">Schutz fuer den privaten Schluessel (Handy: Keystore/Keychain); null = Windows-Datenschutz.</param>
    /// <param name="network">Anderes Netz statt ntfy + lokal (Tests, Vorschau); null = die echten Wege.</param>
    /// <param name="clock">Uhr (Tests); null = <see cref="DateTimeOffset.UtcNow"/>.</param>
    public ChatClient(string dataDir, IChatSettings settings, ISecretProtector? protector = null, IChatNetwork? network = null, Func<DateTimeOffset>? clock = null)
    {
        _dataDir = dataDir;
        _settings = settings;
        _protector = protector;
        _network = network;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Wer Admin ist (Vorgabe: die fest eingetragenen). Nur fuer Tests anders.</summary>
    public Func<string, bool> AdminCheck { get; set; } = ChatAdmins.IsAdmin;

    public ChatSession? Session { get; private set; }

    /// <summary>Verschluesselung wird gerade abgeleitet (dauert einen Moment).</summary>
    public bool IsDeriving { get; private set; }

    /// <summary>Anzeigename des Raums ("Offener Chat", oder die Pruefzahl).</summary>
    public string RoomLabel { get; private set; } = "";

    /// <summary>Verschluesselung des aktuellen Raums - nur im Speicher.</summary>
    public string? RoomSecret { get; private set; }

    /// <summary>Offener Chat: Standardraum ohne eigene Verschluesselung.</summary>
    public bool IsOpenRoom { get; private set; }

    public bool IsInRoom => Session is { State: not ChatSessionState.Ended };

    /// <summary>Es gibt etwas anzuzeigen: Raum wird betreten, laeuft oder ist gerade zu Ende (Verlauf sichtbar).</summary>
    public bool IsActive => IsDeriving || Session is not null;

    /// <summary>Meldung, falls die Identitaet nicht dauerhaft gespeichert werden konnte (dann gilt sie nur fuer diese Sitzung).</summary>
    public string? IdentityWarning { get; private set; }

    /// <summary>Etwas hat sich geaendert (im Aufrufer-Thread von <see cref="Pump"/>).</summary>
    public event Action? Changed;

    /// <summary>
    /// Eine Nachricht von jemand anderem ist da, die laut Einstellung melden soll (Ton/Benachrichtigung).
    /// Zweiter Wert: der eigene Name kommt darin vor.
    /// </summary>
    public event Action<ChatLine, bool>? MessageReceived;

    public ChatIdentity Identity
    {
        get
        {
            lock (_identityLock) return _identity ??= LoadIdentity();
        }
    }

    /// <summary>Identitaet laden (kann dauern - deshalb vor dem ersten Bild in einem Hintergrund-Thread aufrufen).</summary>
    public void EnsureIdentity() => _ = Identity;

    public ChatBanList Bans => _bans ??= new ChatBanList(Path.Combine(_dataDir, ChatBanList.FileName));

    /// <summary>Mein Anzeigename aus den Einstellungen (gesaeubert) - null = keiner.</summary>
    public string? MyAlias => ChatProfile.Clean(_settings.Alias, allowReserved: IAmAdmin);

    /// <summary>Meine Namensfarbe: 0 = automatisch, 1 bis 8.</summary>
    public int MyColor => ChatProfile.CleanColor(_settings.Color);

    public bool IsAdmin(string? fingerprint) => fingerprint is not null && AdminCheck(fingerprint);

    public bool IAmAdmin => IsAdmin(Identity.Fingerprint);

    private ChatIdentity LoadIdentity()
    {
        try
        {
            return ChatIdentity.LoadOrCreate(Path.Combine(_dataDir, ChatIdentity.FileName), _protector);
        }
        catch (Exception ex)
        {
            IdentityWarning = ex.Message;
            return ChatIdentity.CreateNew();
        }
    }

    // ------------------------------------------------------------------
    // Raum betreten / verlassen
    // ------------------------------------------------------------------

    /// <summary>Raum mit dieser Verschluesselung betreten (ungueltige werden ignoriert - vorher <see cref="ChatRoomKey.Problem"/> pruefen).</summary>
    public void Connect(string secret)
    {
        if (ChatRoomKey.Problem(secret) != SecretProblem.None) return;
        LeaveCurrent(wait: false);

        var normalized = ChatRoomKey.Normalize(secret);
        var open = normalized == ChatRoomKey.OpenSecret;
        var ticket = ++_connectTicket;
        IsDeriving = true;
        RoomLabel = open ? Loc.T("CHAT_OPEN_ROOM") : "";
        RoomSecret = open ? null : normalized;
        IsOpenRoom = open;
        Session = null;
        Raise();

        _ = Identity;
        Task.Run(() =>
        {
            ChatRoomKey? key = null;
            try { key = open ? ChatRoomKey.Open : ChatRoomKey.Derive(normalized); }
            catch (Exception) { /* key bleibt null: unten wird der Raum wieder verlassen */ }

            _main.Enqueue(() =>
            {
                if (ticket != _connectTicket) return;   // inzwischen abgebrochen
                IsDeriving = false;
                if (key is not null) StartSession(key);
                else Reset();
            });
        });
    }

    public void ConnectOpen()
    {
        if (ChatRoomKey.OpenRoomAvailable) Connect(ChatRoomKey.OpenSecret);
    }

    private void StartSession(ChatRoomKey key)
    {
        var network = _network ?? new DefaultChatNetwork(DefaultChatNetwork.ParseServer(_settings.Server));
        if (RoomLabel.Length == 0) RoomLabel = Loc.T("CHAT_ROOM_CHECK", key.Check);
        var session = new ChatSession(Identity, key, network, null, Bans, fingerprint => AdminCheck(fingerprint));
        Session = session;
        _lastVersion = -1;
        session.SetProfile(MyAlias, MyColor, _clock());   // schon der Beitritt traegt Name und Farbe
        session.LineAdded += line => OnLineAdded(session, line);
        session.Start(_clock());
    }

    private void OnLineAdded(ChatSession session, ChatLine line)
    {
        if (!ReferenceEquals(session, Session) || line.Kind != ChatLineKind.Theirs) return;
        var mention = ChatProfile.Mentions(line.Text, MyAlias, Identity.Id);
        if (ChatProfile.ShouldNotify(_settings.Notify, mention)) MessageReceived?.Invoke(line, mention);
    }

    /// <summary>Anzeigename/Farbe aus den Einstellungen in den laufenden Chat uebernehmen (die anderen erfahren es sofort).</summary>
    public void ApplyProfile()
    {
        Session?.SetProfile(MyAlias, MyColor, _clock());
        Raise();
    }

    /// <summary>Raum verlassen - der Verlauf bleibt sichtbar, bis <see cref="Reset"/> oder ein neuer Raum.</summary>
    public void Leave()
    {
        LeaveCurrent(wait: false);
        Raise();
    }

    /// <summary>Alles vergessen (Verlauf weg), zurueck zur Raumwahl.</summary>
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
        if (Session is { State: not ChatSessionState.Ended } session) session.Leave(_clock(), wait);
    }

    /// <summary>Beim Beenden der App: tschuess sagen und kurz warten, bis es raus ist.</summary>
    public void Shutdown() => LeaveCurrent(wait: true);

    public void Dispose()
    {
        Shutdown();
        lock (_identityLock)
        {
            _identity?.Dispose();
            _identity = null;
        }
    }

    /// <summary>Regelmaessig aus dem Oberflaechen-Thread. true = es hat sich etwas geaendert (<see cref="Changed"/> kam).</summary>
    public bool Pump()
    {
        var changed = false;
        while (_main.TryDequeue(out var action))
        {
            action();
            changed = true;
        }
        if (Session is { } session)
        {
            session.Pump(_clock());
            if (session.Version != _lastVersion)
            {
                _lastVersion = session.Version;
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
        return changed;
    }

    private void Raise()
    {
        _lastVersion = -1;
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------
    // Aktionen
    // ------------------------------------------------------------------

    public ChatResult Send(string text) => Session is { } s ? s.SendText(text, _clock()) : ChatResult.NotConnected;

    public ChatResult ProposeLocal() => Session is { } s ? s.ProposeLocal(_clock()) : ChatResult.NotConnected;

    /// <summary>Antwort auf den Wechsel-Vorschlag. Nach dem Wechsel "nein" = Raum jetzt verlassen.</summary>
    public void AnswerProposal(bool yes)
    {
        if (Session is not { Proposal: { } p } session) return;
        if (p.Stage == ProposalStage.Countdown && !yes)
        {
            Leave();
            return;
        }
        session.AnswerProposal(yes, _clock());
        Raise();
    }

    // ------------------------------------------------------------------
    // Namen + Farben
    // ------------------------------------------------------------------

    /// <summary>
    /// "Du", der Anzeigename des anderen mit ID-Endung ("Tom #1417") oder die ID-Nummer -
    /// bei Admins mit vorangestelltem "(Admin)".
    /// </summary>
    public string NameOf(string? fingerprint, string? memberId)
    {
        string name;
        if (fingerprint is not null && fingerprint == Identity.Fingerprint) name = Loc.T("CHAT_YOU");
        else if (Session?.ProfileOf(fingerprint).Alias is { } alias) name = $"{alias} {ChatProfile.IdTag(memberId)}";
        else name = Loc.T("CHAT_ID", memberId ?? "?");
        return IsAdmin(fingerprint) ? $"{Loc.T("CHAT_ADMIN_TAG")} {name}" : name;
    }

    /// <summary>
    /// Namensfarbe (1 bis 8): die gewaehlte, sonst eine aus dem Fingerabdruck. 0 = meine eigene ohne Wahl
    /// (die Oberflaeche nimmt dafuer ihre Akzentfarbe).
    /// </summary>
    public int ColorIndexOf(string? fingerprint)
    {
        if (fingerprint is null) return 0;
        var mine = fingerprint == Identity.Fingerprint;
        var chosen = mine ? MyColor : Session?.ProfileOf(fingerprint).Color ?? 0;
        if (chosen > 0) return chosen;
        return mine ? 0 : ChatPalette.AutoIndex(fingerprint);
    }

    // ------------------------------------------------------------------
    // Anzeige
    // ------------------------------------------------------------------

    /// <summary>
    /// Neue Verlaufszeilen seit dem letzten Aufruf. <see cref="FeedChange.Reset"/> = die Oberflaeche muss alles neu
    /// aufbauen (anderer Raum, Verlauf vorn gekuerzt) - dann steht der ganze Verlauf in <see cref="FeedChange.Rows"/>.
    /// </summary>
    public FeedChange ReadNewRows()
    {
        var session = Session;
        IReadOnlyList<ChatLine> lines = session?.Lines ?? [];

        var canAppend = !_feedRestart && ReferenceEquals(session, _feedSession) && _feedCount <= lines.Count &&
                        (_feedCount == 0 || ReferenceEquals(lines[_feedCount - 1], _feedLast));
        _feedRestart = false;
        var from = canAppend ? _feedCount : 0;

        var rows = new List<ChatRow>(Math.Max(0, lines.Count - from));
        for (var i = from; i < lines.Count; i++) rows.Add(ToRow(lines[i]));

        _feedSession = session;
        _feedCount = lines.Count;
        _feedLast = lines.Count > 0 ? lines[^1] : null;
        return new FeedChange(!canAppend, rows);
    }

    /// <summary>Der naechste <see cref="ReadNewRows"/>-Aufruf liefert wieder den ganzen Verlauf (neue Anzeige, Farbwechsel).</summary>
    public void RestartFeed() => _feedRestart = true;

    public ChatRow ToRow(ChatLine line)
    {
        var time = line.Time.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        switch (line.Kind)
        {
            case ChatLineKind.Mine or ChatLineKind.Theirs:
                var mine = line.Kind == ChatLineKind.Mine;
                var mention = !mine && _settings.HighlightMentions && ChatProfile.Mentions(line.Text, MyAlias, Identity.Id);
                return new ChatRow(ChatRowKind.Message, time, NameOf(line.Fingerprint, line.MemberId), ColorIndexOf(line.Fingerprint),
                    line.Text, mine, mention, IsAdmin(line.Fingerprint), line);
            case ChatLineKind.Warning:
                return new ChatRow(ChatRowKind.Warning, time, "", 0, NoticeText(line), false, false, false, line);
            default:
                return new ChatRow(ChatRowKind.Notice, time, "", 0, NoticeText(line), false, false, false, line);
        }
    }

    private string NoticeText(ChatLine line)
    {
        var args = new List<object?> { NameOf(line.Fingerprint, line.MemberId) };
        if (line.Args is not null) args.AddRange(line.Args);
        return Loc.T("CHAT_N_" + line.Text.ToUpperInvariant(), args.ToArray());
    }

    public ChatStatus GetStatus()
    {
        if (IsDeriving) return new(Loc.T("CHAT_STATE_DERIVING"), ChatStatusLevel.Warn);
        if (Session is not { } s) return new(Loc.T("CHAT_STATE_OFFLINE"), ChatStatusLevel.Off);
        if (s.State == ChatSessionState.Ended) return new(Loc.T("CHAT_STATE_ENDED"), ChatStatusLevel.Off);
        if (s.State == ChatSessionState.Connecting) return new(Loc.T("CHAT_STATE_CONNECTING"), ChatStatusLevel.Warn);
        if (s.Mode == ChatMode.Local)
            return s.LocalPeers > 0
                ? new(Loc.T("CHAT_STATE_LOCAL", s.LocalPeers), ChatStatusLevel.Ok)
                : new(Loc.T("CHAT_STATE_LOCAL_WAITING"), ChatStatusLevel.Warn);
        return s.Link == ChatLinkState.Reconnecting
            ? new(Loc.T("CHAT_STATE_RECONNECTING"), ChatStatusLevel.Warn)
            : new(Loc.T("CHAT_STATE_ONLINE", s.ServiceName), ChatStatusLevel.Ok);
    }

    /// <summary>Hinweis unter dem Eingabefeld (Offener Chat, Sperre); Text leer = keiner.</summary>
    public (string Text, bool Warn) GetHint()
    {
        if (Session is { IsBanned: true, SelfBan: { } ban }) return (BannedHint(ban), true);
        return IsOpenRoom ? (Loc.T("CHAT_OPEN_HINT"), true) : ("", false);
    }

    private static string BannedHint(ChatBan ban)
    {
        var until = ban.Until == 0
            ? Loc.T("CHAT_BAN_PERMANENT")
            : DateTimeOffset.FromUnixTimeMilliseconds(ban.Until).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return ban.Reason.Length > 0 ? Loc.T("CHAT_BANNED_HINT_REASON", ban.Reason, until) : Loc.T("CHAT_BANNED_HINT", until);
    }

    /// <summary>Kann ich gerade schreiben?</summary>
    public bool CanWrite => Session is { State: ChatSessionState.Connected, IsBanned: false };

    /// <summary>Kann ich den Wechsel ins lokale Netz vorschlagen?</summary>
    public bool CanProposeLocal => Session is { State: ChatSessionState.Connected, Mode: ChatMode.Online, Proposal: null };

    public ProposalBanner? GetBanner()
    {
        if (Session is not { Proposal: { } p } session || session.State == ChatSessionState.Ended) return null;

        var seconds = Math.Max(0, (int)Math.Ceiling((p.Deadline - _clock()).TotalSeconds));
        var name = NameOf(p.ProposerFingerprint, p.ProposerId);
        return p.Stage switch
        {
            ProposalStage.Waiting => new(Loc.T("CHAT_BANNER_WAITING", p.YesCount, p.NoCount, p.AskedCount, seconds), null, null, false),
            ProposalStage.Asking => new(
                p.Wifi is { } wifi ? Loc.T("CHAT_BANNER_ASKING_WIFI", name, seconds, wifi) : Loc.T("CHAT_BANNER_ASKING", name, seconds),
                Loc.T("CHAT_BTN_APPROVE"), Loc.T("CHAT_BTN_DECLINE"), false),
            ProposalStage.Joining => new(Loc.T("CHAT_BANNER_JOINING"), null, null, false),
            ProposalStage.Declined => new(Loc.T("CHAT_BANNER_DECLINED"), null, null, false),
            _ => new(Loc.T("CHAT_BANNER_COUNTDOWN", seconds), Loc.T("CHAT_BTN_FOLLOW"), Loc.T("CHAT_BTN_LEAVE_NOW"), true),
        };
    }

    public IReadOnlyList<MemberRow> GetMembers()
    {
        if (!IsInRoom) return [];
        return Session!.Members
            .Select(m => new MemberRow(NameOf(m.Fingerprint, m.Id), ColorIndexOf(m.Fingerprint), m.IsSelf, IsAdmin(m.Fingerprint), m.Id,
                m.IsSelf ? "" : Loc.T(m.Mode == ChatMode.Local ? "CHAT_MODE_LOCAL" : "CHAT_MODE_ONLINE")))
            .ToList();
    }

    /// <summary>Text zu einem Ergebnis (null = nichts zu melden).</summary>
    public static string? ResultText(ChatResult result) => result switch
    {
        ChatResult.Ok or ChatResult.Empty => null,
        ChatResult.TooFast => Loc.T("CHAT_RESULT_TOOFAST", ChatSession.OnlineMessagesPerMinute),
        ChatResult.TooLong => Loc.T("CHAT_RESULT_TOOLONG"),
        ChatResult.Alone => Loc.T("CHAT_RESULT_ALONE"),
        ChatResult.NoNetwork => Loc.T("CHAT_RESULT_NONETWORK"),
        ChatResult.Busy => Loc.T("CHAT_RESULT_BUSY"),
        ChatResult.NotFound => Loc.T("CHAT_RESULT_NOTFOUND"),
        ChatResult.WrongTurn => Loc.T("CHAT_RESULT_WRONGTURN"),
        ChatResult.NotAllowed => Loc.T("CHAT_RESULT_NOTALLOWED"),
        ChatResult.Banned => Loc.T("CHAT_RESULT_BANNED"),
        _ => Loc.T("CHAT_RESULT_NOTCONNECTED"),
    };

    /// <summary>Text zu einem Problem mit der eingegebenen Verschluesselung (null = keins).</summary>
    public static string? SecretProblemText(SecretProblem problem) =>
        problem == SecretProblem.None ? null : Loc.T("CHAT_SECRET_" + problem.ToString().ToUpperInvariant());
}
