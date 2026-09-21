using System.Collections.Concurrent;
using System.Security.Cryptography;
using Floppy.Core.Chess;

namespace Floppy.Core.Chat;

public enum ChatSessionState { Connecting, Connected, Ended }

public enum ChatLineKind
{
    /// <summary>Hinweis der App (Text = Schluessel, siehe <see cref="ChatNotice"/>).</summary>
    Notice,

    /// <summary>Warnung der App (Text = Schluessel).</summary>
    Warning,

    Mine,
    Theirs,
}

public enum ChatResult { Ok, Empty, TooLong, NotConnected, TooFast, Alone, NoNetwork, Busy, AlreadyLocal, NotFound, WrongTurn, NotAllowed, Banned }

/// <summary>Schluessel fuer Hinweise im Chatverlauf (die App uebersetzt sie).</summary>
public static class ChatNotice
{
    public const string Connecting = "connecting";
    public const string Online = "online";
    public const string Reconnecting = "reconnecting";
    public const string Reconnected = "reconnected";
    public const string Limited = "limited";
    public const string SendFailed = "send_failed";
    public const string FoundLocal = "found_local";
    public const string Joined = "joined";
    public const string Left = "left";
    public const string Lost = "lost";
    public const string StayedOnline = "stayed_online";
    public const string ProposalReceived = "proposal";
    public const string ProposalSent = "proposal_sent";
    public const string VoteNo = "vote_no";
    public const string VoteUnreachable = "vote_unreachable";
    public const string ProposalRejectedMine = "proposal_rejected_mine";
    public const string ProposalTimeoutMine = "proposal_timeout_mine";
    public const string ProposalRejected = "proposal_rejected";
    public const string ProposalTimeout = "proposal_timeout";
    public const string ProposalExpired = "proposal_expired";
    public const string ProposalGone = "proposal_gone";
    public const string YouDeclined = "you_declined";
    public const string JoiningLocal = "joining_local";
    public const string LocalUnreachable = "local_unreachable";
    public const string LocalJoined = "local_joined";
    public const string SwitchedByYou = "switched_you";
    public const string SwitchCountdown = "switch_countdown";
    public const string LocalAlone = "local_alone";
    public const string LocalPeers = "local_peers";
    public const string Excluded = "excluded";
    public const string ScoresRequested = "scores_requested";
    public const string ChessChallenged = "chess_challenged";
    public const string ChessChallengeSent = "chess_challenge_sent";
    public const string ChessAccepted = "chess_accepted";
    public const string ChessYouDeclined = "chess_you_declined";
    public const string ChessDeclined = "chess_declined";
    public const string ChessWon = "chess_won";
    public const string ChessLost = "chess_lost";
    public const string ChessStalemate = "chess_stalemate";
    public const string ChessYouResigned = "chess_you_resigned";
    public const string ChessOpponentResigned = "chess_opponent_resigned";
    public const string ChessOpponentLeft = "chess_opponent_left";

    /// <summary>Text = Schluessel, Args[0] = Grund (bei <see cref="BannedReason"/>).</summary>
    public const string Banned = "banned";
    public const string BannedReason = "banned_reason";
    public const string YouBanned = "you_banned";
    public const string YouBannedReason = "you_banned_reason";
    public const string Unbanned = "unbanned";
    public const string YouUnbanned = "you_unbanned";
}

/// <param name="Text">Nachricht - oder bei Hinweisen der Schluessel aus <see cref="ChatNotice"/>.</param>
public sealed record ChatLine(DateTimeOffset Time, ChatLineKind Kind, string Text, string? Fingerprint = null, string? MemberId = null, string[]? Args = null);

public sealed class ChatMember
{
    public required string Fingerprint { get; init; }
    public required string Id { get; init; }
    public bool IsSelf { get; init; }
    public ChatMode Mode { get; internal set; }
    public DateTimeOffset LastSeen { get; internal set; }
}

public enum ProposalStage
{
    /// <summary>Eigener Vorschlag, warte auf Antworten.</summary>
    Waiting,

    /// <summary>Jemand anderes fragt - Haken oder Kreuz?</summary>
    Asking,

    /// <summary>Haken gedrueckt, verbinde mit dem lokalen Raum.</summary>
    Joining,

    /// <summary>Abgelehnt (Hinweis bleibt, bis der Vorschlag endet).</summary>
    Declined,

    /// <summary>Der Raum ist gewechselt: wer nicht mitkommt, fliegt nach Ablauf raus.</summary>
    Countdown,
}

public sealed class ChatProposal
{
    public required string Id { get; init; }
    public required string ProposerFingerprint { get; init; }
    public required string ProposerId { get; init; }
    public bool IsMine { get; init; }
    public IReadOnlyList<string> Endpoints { get; internal set; } = [];

    /// <summary>WLAN-Name des Vorschlagenden, falls ermittelbar - nur eine Empfehlung fuer die anderen.</summary>
    public string? Wifi { get; init; }
    public ProposalStage Stage { get; internal set; }
    public DateTimeOffset Deadline { get; internal set; }
    internal HashSet<string> Asked { get; init; } = [];
    internal HashSet<string> Yes { get; } = [];
    internal HashSet<string> No { get; } = [];
    public int YesCount => Yes.Count;
    public int NoCount => No.Count;
    public int AskedCount => Asked.Count;
}

/// <summary>
/// Ein Chatraum. Nichts wird gespeichert: Verlauf, Teilnehmer und Rangliste leben nur im Speicher.
///
/// Ablauf: <see cref="Start"/> sucht kurz im lokalen Netz nach demselben Raum, sonst geht es
/// online (Gratis-Dienst). Online kann jeder den Wechsel ins lokale Netz vorschlagen: Wer den
/// Haken drueckt, kommt mit. Sobald einer mitkommt, ist der Raum gewechselt - wer nicht mitkommt,
/// wird nach <see cref="CountdownSeconds"/> Sekunden getrennt. Lehnen alle ab (oder antwortet
/// niemand), bleibt alles online.
///
/// Nicht threadsicher: alle Aufrufe aus einem Thread (UI). Netzwerk-Ereignisse landen in einer
/// Warteschlange und werden in <see cref="Pump"/> verarbeitet.
/// </summary>
public sealed class ChatSession : IDisposable
{
    public const int ProposalSeconds = 30;
    public const int CountdownSeconds = 10;
    public const int MaxLines = 500;
    public const int MaxMembers = 64;
    public const int OnlineMessagesPerMinute = 15;
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1.2);
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(10);
    /// <summary>Wie oft ein Gesperrter (stumm) nachfragt, ob die Sperre noch gilt.</summary>
    private static readonly TimeSpan BanCheckEvery = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LocalHeartbeat = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LocalMemberTimeout = TimeSpan.FromSeconds(50);
    private static readonly TimeSpan FlushOnClose = TimeSpan.FromSeconds(2);

    private readonly IChatNetwork _network;
    private readonly Func<IReadOnlyList<ChatScore>> _scores;
    private readonly ChatBanList _bans;
    private readonly Func<string, bool> _isAdmin;
    private readonly ConcurrentQueue<Action<DateTimeOffset>> _inbox = new();
    private readonly List<ChatLine> _lines = [];
    private readonly Dictionary<string, ChatMember> _members = new();
    private readonly HashSet<string> _seen = [];
    private readonly Queue<string> _seenOrder = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _incoming = new();
    private readonly Queue<DateTimeOffset> _sentOnline = new();
    private readonly Dictionary<string, DateTimeOffset> _scoreAnswers = new();
    private readonly List<(DateTimeOffset Due, Action<DateTimeOffset> Run)> _timers = [];
    private IChatTransport? _online;
    private ILocalChatTransport? _local;
    private DateTimeOffset _nextHeartbeat;
    private DateTimeOffset? _pruneOnlineAt;
    private DateTimeOffset _lastScoreRequest = DateTimeOffset.MinValue;
    private DateTimeOffset _lastLimitWarning = DateTimeOffset.MinValue;
    private bool _hereScheduled;
    private bool _banSyncScheduled;
    private bool _banCheckScheduled;
    private readonly Dictionary<string, DateTimeOffset> _banCheckAnswers = new();
    private bool _hadLocalPeers;
    private int _version;

    /// <param name="bans">Gemerkte Sperren (null = nur fuer diese Sitzung).</param>
    /// <param name="isAdmin">Wer Admin ist (Vorgabe: <see cref="ChatAdmins.IsAdmin"/>) - nur fuer Tests und die Vorschau anders.</param>
    public ChatSession(ChatIdentity me, ChatRoomKey room, IChatNetwork network, Func<IReadOnlyList<ChatScore>>? scores = null,
        ChatBanList? bans = null, Func<string, bool>? isAdmin = null)
    {
        Me = me;
        Room = room;
        _network = network;
        _scores = scores ?? (() => []);
        _bans = bans ?? new ChatBanList();
        _isAdmin = isAdmin ?? ChatAdmins.IsAdmin;
        _members[me.Fingerprint] = new ChatMember { Fingerprint = me.Fingerprint, Id = me.Id, IsSelf = true };
    }

    public ChatIdentity Me { get; }
    public ChatRoomKey Room { get; }

    /// <summary>Diese Installation ist Admin (kann im offenen Chat sperren).</summary>
    public bool IsAdmin => _isAdmin(Me.Fingerprint);

    public bool IsAdminMember(string fingerprint) => _isAdmin(fingerprint);

    /// <summary>Sperren gibt es nur im offenen Chat - private Raeume regeln ihre Mitglieder selbst.</summary>
    public bool CanModerate => IsAdmin && Room.IsOpen;

    public ChatBanList Bans => _bans;

    /// <summary>
    /// Ich bin im Offenen Chat gesperrt. Ich bleibe trotzdem verbunden - nur so erfahre ich, wenn die Sperre
    /// aufgehoben wird oder ablaeuft -, sehe aber nichts und kann nichts schreiben.
    /// </summary>
    public bool IsBanned { get; private set; }

    /// <summary>Die Sperre, die gerade fuer mich gilt (null = nicht gesperrt).</summary>
    public ChatBan? SelfBan { get; private set; }

    public string ServiceName => _network.ServiceName;
    public ChatSessionState State { get; private set; } = ChatSessionState.Connecting;
    public ChatMode Mode { get; private set; } = ChatMode.Online;
    public ChatLinkState Link { get; private set; } = ChatLinkState.Connecting;

    /// <summary>Warum der Raum zu ist (<see cref="ChatNotice.Excluded"/>, <see cref="ChatNotice.Left"/>).</summary>
    public string? EndReason { get; private set; }

    public IReadOnlyList<ChatLine> Lines => _lines;
    public IReadOnlyList<ChatMember> Members => _members.Values.OrderByDescending(m => m.IsSelf).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
    public ChatProposal? Proposal { get; private set; }
    public ChessGame? Chess { get; private set; }
    public ChatLeaderboard Leaderboard { get; } = new();
    public int LocalPeers => _local?.PeerCount ?? 0;

    /// <summary>Zaehlt bei jeder Aenderung hoch (die Oberflaeche zeichnet dann neu).</summary>
    public int Version => _version + Leaderboard.Version;

    // ==================================================================
    // Bedienung
    // ==================================================================

    public void Start(DateTimeOffset now)
    {
        Notice(now, ChatNotice.Connecting);
        if (RefreshSelfBan(now))
        {
            // Aus der gemerkten Sperrliste: stumm online dabeisein (ohne lokale Suche), bis sie ablaeuft oder aufgehoben wird
            WarnSelfBanned(now);
            StartOnline();
            return;
        }
        _ = ProbeThenConnect();
    }

    private async Task ProbeThenConnect()
    {
        IReadOnlyList<string> found;
        try { found = await _network.ProbeLocalAsync(Room, ProbeTimeout).ConfigureAwait(false); }
        catch { found = []; }

        _inbox.Enqueue(now =>
        {
            if (State == ChatSessionState.Ended) return;
            if (found.Count == 0)
            {
                StartOnline();
                return;
            }
            Notice(now, ChatNotice.FoundLocal);
            StartLocal(found, null, _ => StartOnline());
        });
    }

    public ChatResult SendText(string text, DateTimeOffset now)
    {
        var clean = CleanText(text);
        if (clean.Length == 0) return ChatResult.Empty;
        if (clean.Length > ChatPayload.MaxTextLength) return ChatResult.TooLong;
        if (IsBanned) return ChatResult.Banned;
        if (State != ChatSessionState.Connected) return ChatResult.NotConnected;
        if (Mode == ChatMode.Online && OnlineBudgetUsed(now) >= OnlineMessagesPerMinute) return ChatResult.TooFast;

        // viele Emojis sind in JSON laenger als sie aussehen: dann ist die Nachricht zu gross
        if (!TrySend(now, new ChatPayload { Kind = ChatKinds.Text, Text = clean }, Mode)) return ChatResult.TooLong;
        AddLine(new ChatLine(now, ChatLineKind.Mine, clean, Me.Fingerprint, Me.Id));
        return ChatResult.Ok;
    }

    /// <summary>Wechsel ins lokale Netzwerk vorschlagen.</summary>
    public ChatResult ProposeLocal(DateTimeOffset now)
    {
        if (State != ChatSessionState.Connected) return ChatResult.NotConnected;
        if (Mode == ChatMode.Local) return ChatResult.AlreadyLocal;
        if (Proposal is not null) return ChatResult.Busy;
        var others = _members.Values.Where(m => !m.IsSelf).Select(m => m.Fingerprint).ToHashSet();
        if (others.Count == 0) return ChatResult.Alone;

        ILocalChatTransport local;
        try
        {
            local = CreateLocal();
            local.Start();
        }
        catch (Exception)
        {
            _local?.Dispose();
            _local = null;
            return ChatResult.NoNetwork;
        }
        if (local.Endpoints.Count == 0)
        {
            DropLocal();
            return ChatResult.NoNetwork;
        }

        var wifi = WifiInfo.CurrentSsid();
        Proposal = new ChatProposal
        {
            Id = ChatFrame.NewId(),
            ProposerFingerprint = Me.Fingerprint,
            ProposerId = Me.Id,
            IsMine = true,
            Endpoints = local.Endpoints.Take(ChatPayload.MaxEndpoints).ToList(),
            Wifi = wifi,
            Stage = ProposalStage.Waiting,
            Deadline = now.AddSeconds(ProposalSeconds),
            Asked = others,
        };
        Send(now, new ChatPayload { Kind = ChatKinds.Propose, Proposal = Proposal.Id, Endpoints = [.. Proposal.Endpoints], Wifi = wifi }, ChatMode.Online);
        Notice(now, ChatNotice.ProposalSent);
        return ChatResult.Ok;
    }

    /// <summary>Haken (mitkommen) oder Kreuz (online bleiben).</summary>
    public void AnswerProposal(bool yes, DateTimeOffset now)
    {
        if (Proposal is not { IsMine: false } p || p.Stage is ProposalStage.Joining || State == ChatSessionState.Ended) return;
        var afterSwitch = p.Stage == ProposalStage.Countdown;

        if (!yes)
        {
            if (p.Stage == ProposalStage.Declined) return;
            if (!afterSwitch)
            {
                Send(now, new ChatPayload { Kind = ChatKinds.Vote, Proposal = p.Id, Yes = false }, ChatMode.Online);
                p.Stage = ProposalStage.Declined;
            }
            Notice(now, ChatNotice.YouDeclined);
            return;
        }

        p.Stage = ProposalStage.Joining;
        Notice(now, ChatNotice.JoiningLocal);
        StartLocal(p.Endpoints, p.Id, failedAt =>
        {
            Warning(failedAt, ChatNotice.LocalUnreachable);
            if (Proposal != p) return;
            if (afterSwitch)
            {
                p.Stage = ProposalStage.Countdown;
            }
            else
            {
                Send(failedAt, new ChatPayload { Kind = ChatKinds.Vote, Proposal = p.Id, Yes = false, Reason = "unreachable" }, ChatMode.Online);
                p.Stage = ProposalStage.Declined;
            }
        });
    }

    // ------------------------------------------------------------------
    // Schach
    // ------------------------------------------------------------------

    /// <summary>Ein Mitglied (per ID) zum Schach herausfordern.</summary>
    public ChatResult ChallengeChess(string opponentId, DateTimeOffset now)
    {
        if (State != ChatSessionState.Connected) return ChatResult.NotConnected;
        if (Chess is { Stage: not ChessGameStage.Ended }) return ChatResult.Busy;
        var target = _members.Values.FirstOrDefault(m => !m.IsSelf && m.Id == opponentId);
        if (target is null) return ChatResult.NotFound;

        var id = ChatFrame.NewId();
        Chess = new ChessGame { Id = id, OpponentFingerprint = target.Fingerprint, OpponentId = target.Id, IsMine = true, Stage = ChessGameStage.Offering };
        Send(now, new ChatPayload { Kind = ChatKinds.ChessOffer, ChessMatch = id, ChessOpponent = target.Id }, Mode);
        Notice(now, ChatNotice.ChessChallengeSent, target.Fingerprint, target.Id);
        return ChatResult.Ok;
    }

    /// <summary>Auf eine hereinkommende Herausforderung antworten.</summary>
    public void AnswerChessChallenge(bool yes, DateTimeOffset now)
    {
        if (Chess is not { IsMine: false, Stage: ChessGameStage.Offering } c) return;
        if (yes)
        {
            c.Stage = ChessGameStage.Active;
            c.MyColor = ChessColor.Black;   // wer herausfordert, faengt an (Weiss)
            Send(now, new ChatPayload { Kind = ChatKinds.ChessAccept, ChessMatch = c.Id }, Mode);
            Notice(now, ChatNotice.ChessAccepted, c.OpponentFingerprint, c.OpponentId);
        }
        else
        {
            Send(now, new ChatPayload { Kind = ChatKinds.ChessDecline, ChessMatch = c.Id }, Mode);
            Notice(now, ChatNotice.ChessYouDeclined, c.OpponentFingerprint, c.OpponentId);
            Chess = null;
        }
    }

    /// <summary>Einen Zug spielen (Notation z. B. "e2e4"), wenn man selbst am Zug ist.</summary>
    public ChatResult MakeChessMove(ChessMove move, DateTimeOffset now)
    {
        if (Chess is not { Stage: ChessGameStage.Active } c) return ChatResult.NotConnected;
        if (!c.MyTurn) return ChatResult.WrongTurn;
        if (!c.Board.TryMove(move)) return ChatResult.WrongTurn;

        Send(now, new ChatPayload { Kind = ChatKinds.ChessMove, ChessMatch = c.Id, ChessMove = move.Notation }, Mode);
        Changed();
        EndChessIfOver(c, now);
        return ChatResult.Ok;
    }

    /// <summary>Die laufende oder angebotene Partie aufgeben/zurueckziehen.</summary>
    public void ResignChess(DateTimeOffset now)
    {
        if (Chess is not { Stage: ChessGameStage.Active or ChessGameStage.Offering } c) return;
        Send(now, new ChatPayload { Kind = ChatKinds.ChessResign, ChessMatch = c.Id }, Mode);
        c.Stage = ChessGameStage.Ended;
        c.EndReason = ChatNotice.ChessYouResigned;
        Notice(now, ChatNotice.ChessYouResigned, c.OpponentFingerprint, c.OpponentId);
        Changed();
    }

    private void EndChessIfOver(ChessGame c, DateTimeOffset now)
    {
        switch (c.Board.Status)
        {
            case ChessStatus.Checkmate:
                c.Stage = ChessGameStage.Ended;
                var iWon = c.Board.Turn != c.MyColor;   // wer am Zug ist (und keinen Zug mehr hat) hat verloren
                c.EndReason = iWon ? ChatNotice.ChessWon : ChatNotice.ChessLost;
                Notice(now, c.EndReason, c.OpponentFingerprint, c.OpponentId);
                break;
            case ChessStatus.Stalemate:
                c.Stage = ChessGameStage.Ended;
                c.EndReason = ChatNotice.ChessStalemate;
                Notice(now, c.EndReason, c.OpponentFingerprint, c.OpponentId);
                break;
        }
    }

    /// <summary>Rangliste anfordern: alle schicken ihre besten Ergebnisse.</summary>
    public ChatResult RequestScores(DateTimeOffset now)
    {
        if (State != ChatSessionState.Connected) return ChatResult.NotConnected;
        if (IsBanned) return ChatResult.Banned;
        var cooldown = TimeSpan.FromSeconds(Mode == ChatMode.Online ? 30 : 5);
        if (now - _lastScoreRequest < cooldown) return ChatResult.TooFast;
        _lastScoreRequest = now;

        Leaderboard.Clear();
        Leaderboard.Add(Me.Fingerprint, Me.Id, OwnScores());
        Send(now, new ChatPayload { Kind = ChatKinds.ScoreRequest, Request = ChatFrame.NewId() }, Mode);
        Notice(now, ChatNotice.ScoresRequested);
        return ChatResult.Ok;
    }

    // ------------------------------------------------------------------
    // Moderation (nur Admin, nur offener Chat)
    // ------------------------------------------------------------------

    /// <summary>
    /// Jemanden aus dem offenen Chat sperren. Wirkt bei allen, die diese Version haben: ihre
    /// Nachrichten werden nicht mehr angezeigt, bisherige ausgeblendet. <paramref name="duration"/> null = dauerhaft.
    /// </summary>
    public ChatResult BanMember(string fingerprint, string? reason, TimeSpan? duration, DateTimeOffset now)
    {
        if (!CanModerate || fingerprint == Me.Fingerprint || _isAdmin(fingerprint)) return ChatResult.NotAllowed;
        if (State != ChatSessionState.Connected || Mode != ChatMode.Online) return ChatResult.NotConnected;

        var known = _members.GetValueOrDefault(fingerprint)?.Id ?? (_bans.IsBanned(fingerprint, now, out var old) ? old!.MemberId : "");
        var entry = new ChatBanEntry
        {
            Fingerprint = fingerprint.ToUpperInvariant(),
            MemberId = known,
            Until = duration is { } d ? now.Add(d).ToUnixTimeMilliseconds() : 0,
            At = now.ToUnixTimeMilliseconds(),
            Reason = CleanReason(reason),
        };
        if (!TrySend(now, new ChatPayload { Kind = ChatKinds.Ban, Bans = [entry] }, ChatMode.Online)) return ChatResult.TooLong;
        ApplyBan(now, entry, announce: true);
        return ChatResult.Ok;
    }

    /// <summary>Eine Sperre wieder aufheben.</summary>
    public ChatResult UnbanMember(string fingerprint, DateTimeOffset now)
    {
        if (!CanModerate) return ChatResult.NotAllowed;
        if (State != ChatSessionState.Connected || Mode != ChatMode.Online) return ChatResult.NotConnected;

        var memberId = _bans.IsBanned(fingerprint, now, out var ban) ? ban!.MemberId : "";
        var entry = new ChatBanEntry { Fingerprint = fingerprint.ToUpperInvariant(), MemberId = memberId, At = now.ToUnixTimeMilliseconds() };
        if (!TrySend(now, new ChatPayload { Kind = ChatKinds.Unban, Bans = [entry] }, ChatMode.Online)) return ChatResult.TooLong;
        ApplyUnban(now, entry, announce: true);
        return ChatResult.Ok;
    }

    private static string? CleanReason(string? reason)
    {
        var clean = CleanText(reason);
        if (clean.Length > ChatPayload.MaxBanReasonLength) clean = clean[..ChatPayload.MaxBanReasonLength].TrimEnd();
        if (clean.Length > 0 && char.IsHighSurrogate(clean[^1])) clean = clean[..^1];   // kein halbes Emoji (JSON lehnt es ab)
        return clean.Length == 0 ? null : clean;
    }

    /// <summary>Raum verlassen (sagt den anderen tschuess).</summary>
    /// <param name="wait">true = kurz warten, bis die letzte Nachricht raus ist (beim Beenden der App).</param>
    public void Leave(DateTimeOffset now, bool wait = false) => End(now, ChatNotice.Left, wait);

    public void Dispose()
    {
        if (State != ChatSessionState.Ended) End(DateTimeOffset.UtcNow, ChatNotice.Left, wait: false);
    }

    /// <summary>Netzwerk-Ereignisse und Zeitablaeufe verarbeiten. true = etwas hat sich geaendert.</summary>
    public bool Pump(DateTimeOffset now)
    {
        var before = Version;
        while (_inbox.TryDequeue(out var action)) action(now);
        if (IsBanned && State != ChatSessionState.Ended && !_bans.IsBanned(Me.Fingerprint, now, out _)) LiftSelfBan(now);   // Sperre abgelaufen
        if (State == ChatSessionState.Ended) return Version != before;

        for (var i = _timers.Count - 1; i >= 0; i--)
        {
            if (_timers[i].Due > now) continue;
            var run = _timers[i].Run;
            _timers.RemoveAt(i);
            run(now);
            if (State == ChatSessionState.Ended) return true;
        }

        if (Proposal is { } p && now >= p.Deadline)
        {
            switch (p.Stage)
            {
                case ProposalStage.Waiting when p.YesCount == 0:
                    CancelMyProposal(now, "timeout");
                    break;
                case ProposalStage.Asking:
                    Proposal = null;
                    Notice(now, ChatNotice.ProposalExpired);
                    break;
                case ProposalStage.Declined:
                    Proposal = null;
                    Changed();
                    break;
                case ProposalStage.Countdown when Mode == ChatMode.Online:
                    End(now, ChatNotice.Excluded, wait: false);
                    return true;
            }
        }

        if (Mode == ChatMode.Local && State == ChatSessionState.Connected)
        {
            if (now >= _nextHeartbeat)
            {
                _nextHeartbeat = now + LocalHeartbeat;
                Send(now, new ChatPayload { Kind = ChatKinds.Here, Mode = "local" }, ChatMode.Local);
            }
            foreach (var m in _members.Values.Where(m => !m.IsSelf && m.Mode == ChatMode.Local && now - m.LastSeen > LocalMemberTimeout).ToList())
            {
                _members.Remove(m.Fingerprint);
                Notice(now, ChatNotice.Lost, m.Fingerprint, m.Id);
            }
            if (_pruneOnlineAt is { } prune && now >= prune)
            {
                _pruneOnlineAt = null;
                foreach (var m in _members.Values.Where(m => !m.IsSelf && m.Mode == ChatMode.Online).ToList())
                {
                    _members.Remove(m.Fingerprint);
                    Notice(now, ChatNotice.StayedOnline, m.Fingerprint, m.Id);
                }
            }
        }
        return Version != before;
    }

    // ==================================================================
    // Wege
    // ==================================================================

    private void StartOnline()
    {
        if (_online is not null || State == ChatSessionState.Ended) return;
        var online = _network.CreateOnline(Room);
        _online = online;
        online.FrameReceived += (frame, origin) => _inbox.Enqueue(now =>
        {
            if (_online == online) HandleFrame(now, frame, origin, ChatMode.Online);
        });
        online.StateChanged += (state, detail) => _inbox.Enqueue(now =>
        {
            if (_online == online) OnOnlineState(now, state, detail);
        });
        online.Start();
    }

    private ILocalChatTransport CreateLocal()
    {
        DropLocal();
        var local = _network.CreateLocal(Room);
        _local = local;
        local.FrameReceived += (frame, origin) => _inbox.Enqueue(now =>
        {
            if (_local == local) HandleFrame(now, frame, origin, ChatMode.Local);
        });
        local.StateChanged += (state, _) => _inbox.Enqueue(now =>
        {
            if (_local == local) OnLocalState(now, state);
        });
        return local;
    }

    /// <summary>Lokal beitreten; <paramref name="onFail"/> laeuft (im UI-Thread), wenn niemand erreichbar ist.</summary>
    private void StartLocal(IReadOnlyList<string> endpoints, string? proposalId, Action<DateTimeOffset> onFail)
    {
        ILocalChatTransport local;
        try
        {
            local = CreateLocal();
            local.Start();
        }
        catch (Exception)
        {
            DropLocal();
            _inbox.Enqueue(onFail);
            return;
        }

        _ = Task.Run(async () =>
        {
            bool ok;
            try { ok = await local.ConnectAsync(endpoints, ConnectTimeout).ConfigureAwait(false); }
            catch { ok = false; }
            _inbox.Enqueue(now =>
            {
                if (_local != local || State == ChatSessionState.Ended) return;
                if (ok)
                {
                    EnterLocal(now, proposalId);
                }
                else
                {
                    DropLocal();
                    onFail(now);
                }
            });
        });
    }

    private void DropLocal()
    {
        var local = _local;
        _local = null;
        local?.Dispose();
    }

    private void EnterLocal(DateTimeOffset now, string? proposalId)
    {
        var wasConnected = State == ChatSessionState.Connected;
        Mode = ChatMode.Local;
        State = ChatSessionState.Connected;
        Link = LocalPeers > 0 ? ChatLinkState.Connected : ChatLinkState.Connecting;
        _hadLocalPeers = LocalPeers > 0;
        CloseOnline(wait: false);

        if (Proposal is { IsMine: false }) Proposal = null;
        _hereScheduled = false;
        if (wasConnected) _pruneOnlineAt = now.AddSeconds(CountdownSeconds + 2);   // wer nicht mitkommt, faellt raus
        _nextHeartbeat = now + LocalHeartbeat;

        Notice(now, ChatNotice.LocalJoined);
        Send(now, new ChatPayload { Kind = ChatKinds.Join, Mode = "local", Proposal = proposalId }, ChatMode.Local);
    }

    private void CloseOnline(bool wait)
    {
        var online = _online;
        _online = null;
        if (online is null) return;
        if (wait) online.Close(FlushOnClose);
        else _ = Task.Run(() => online.Close(FlushOnClose));
    }

    private void OnOnlineState(DateTimeOffset now, ChatLinkState state, string? detail)
    {
        switch (state)
        {
            case ChatLinkState.Connected:
                var wasReconnecting = Link == ChatLinkState.Reconnecting;
                Link = ChatLinkState.Connected;
                if (State == ChatSessionState.Connecting)
                {
                    State = ChatSessionState.Connected;
                    Mode = ChatMode.Online;
                    Notice(now, ChatNotice.Online, null, null, ServiceName);
                    if (IsBanned)
                    {
                        AskIfStillBanned(now);   // still da sein, nur zuhoeren und nachfragen
                    }
                    else
                    {
                        Send(now, new ChatPayload { Kind = ChatKinds.Join, Mode = "online" }, ChatMode.Online);
                        ScheduleBanSync(now);
                    }
                }
                else if (wasReconnecting)
                {
                    Notice(now, ChatNotice.Reconnected);
                    if (IsBanned) AskIfStillBanned(now);
                    else Send(now, new ChatPayload { Kind = ChatKinds.Here, Mode = "online" }, ChatMode.Online);
                }
                Changed();
                break;

            case ChatLinkState.Reconnecting:
                if (Link == ChatLinkState.Connected) Warning(now, ChatNotice.Reconnecting);
                Link = ChatLinkState.Reconnecting;
                Changed();
                break;

            case ChatLinkState.Limited:
                if (now - _lastLimitWarning < TimeSpan.FromSeconds(20)) break;
                _lastLimitWarning = now;
                Warning(now, detail == "send-failed" ? ChatNotice.SendFailed : ChatNotice.Limited, null, null, ServiceName);
                break;
        }
    }

    private void OnLocalState(DateTimeOffset now, ChatLinkState state)
    {
        if (Mode != ChatMode.Local || State != ChatSessionState.Connected) return;   // z. B. eigener Vorschlag laeuft noch
        if (state == ChatLinkState.Connected)
        {
            Link = ChatLinkState.Connected;
            if (!_hadLocalPeers) Notice(now, ChatNotice.LocalPeers, null, null, LocalPeers.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _hadLocalPeers = true;
            Send(now, new ChatPayload { Kind = ChatKinds.Here, Mode = "local" }, ChatMode.Local);
        }
        else if (state == ChatLinkState.Connecting)
        {
            Link = ChatLinkState.Connecting;
            if (_hadLocalPeers) Warning(now, ChatNotice.LocalAlone);
            _hadLocalPeers = false;
        }
        Changed();
    }

    private void End(DateTimeOffset now, string reason, bool wait)
    {
        if (State == ChatSessionState.Ended) return;
        if (State == ChatSessionState.Connected)
        {
            var leave = new ChatPayload { Kind = ChatKinds.Leave };
            if (_online is not null) Send(now, leave, ChatMode.Online);
            if (_local is not null) Send(now, leave, ChatMode.Local);
        }
        State = ChatSessionState.Ended;
        Link = ChatLinkState.Closed;
        EndReason = reason;
        Proposal = null;
        _timers.Clear();
        CloseOnline(wait);

        var local = _local;
        _local = null;
        if (local is not null)
        {
            if (wait) local.Close(FlushOnClose);
            else _ = Task.Run(() => local.Close(FlushOnClose));
        }
        if (reason == ChatNotice.Excluded) Warning(now, ChatNotice.Excluded);
        Changed();
    }

    // ==================================================================
    // Empfang
    // ==================================================================

    private void HandleFrame(DateTimeOffset now, byte[] frame, object? origin, ChatMode via)
    {
        if (State == ChatSessionState.Ended) return;
        if (!ChatFrame.TryOpen(Room, frame, out var envelope) || envelope is null)
        {
            if (via == ChatMode.Local) _local?.Reject(origin);
            return;
        }

        var p = envelope.Payload;
        if (!Remember(p.Id)) return;                                       // schon gesehen
        if (Math.Abs(now.ToUnixTimeMilliseconds() - p.Time) > MaxClockSkew.TotalMilliseconds) return;
        if (IsBanned && p.Kind is not (ChatKinds.Ban or ChatKinds.Unban or ChatKinds.BanList)) return;   // ich bin gesperrt: nur Admin-Meldungen zaehlen
        // Gesperrte anderer werden nicht angezeigt/weitergereicht - nur ihre stille Nachfrage sieht der Admin
        if (IsBlocked(envelope.SenderFingerprint, now) && !(CanModerate && p.Kind == ChatKinds.BanCheck)) return;
        if (via == ChatMode.Local) _local?.Relay(frame, origin);           // an die anderen weiterreichen
        if (envelope.SenderFingerprint == Me.Fingerprint) return;          // eigenes Echo
        if (IsFlooding(envelope.SenderFingerprint, now)) return;

        Dispatch(now, envelope, via);
    }

    private void Dispatch(DateTimeOffset now, ChatEnvelope e, ChatMode via)
    {
        var p = e.Payload;
        var fp = e.SenderFingerprint;

        if (p.Kind is ChatKinds.Ban or ChatKinds.Unban or ChatKinds.BanList or ChatKinds.BanCheck)
        {
            HandleModeration(now, e);
            return;
        }

        if (p.Kind == ChatKinds.Leave)
        {
            if (_members.Remove(fp)) Notice(now, ChatNotice.Left, fp, e.SenderId);
            if (Proposal is { } open)
            {
                if (!open.IsMine && open.ProposerFingerprint == fp && open.Stage is ProposalStage.Asking or ProposalStage.Declined)
                {
                    Proposal = null;
                    Notice(now, ChatNotice.ProposalGone, fp, e.SenderId);
                }
                else if (open.IsMine && open.Stage == ProposalStage.Waiting)
                {
                    CheckAllDeclined(now);
                }
            }
            if (Chess is { Stage: ChessGameStage.Active or ChessGameStage.Offering } game && game.OpponentFingerprint == fp)
            {
                game.Stage = ChessGameStage.Ended;
                game.EndReason = ChatNotice.ChessOpponentLeft;
                Notice(now, ChatNotice.ChessOpponentLeft, fp, e.SenderId);
            }
            return;
        }

        var isNew = Touch(now, e, via);
        if (isNew is null) return;   // Raum voll

        switch (p.Kind)
        {
            case ChatKinds.Join:
                if (isNew == true) Notice(now, ChatNotice.Joined, fp, e.SenderId);
                ScheduleHere(now, via);
                if (isNew == true) ScheduleBanSync(now);
                if (via == ChatMode.Local && Proposal is { IsMine: true, Stage: ProposalStage.Waiting } mine && p.Proposal == mine.Id)
                {
                    mine.Yes.Add(fp);
                    if (mine.YesCount == 1) CompleteMySwitch(now, mine);
                }
                break;

            case ChatKinds.Here:
                if (isNew == true && via == ChatMode.Local && Mode == ChatMode.Local && _pruneOnlineAt is null)
                    Notice(now, ChatNotice.Joined, fp, e.SenderId);
                break;

            case ChatKinds.Text:
                AddLine(new ChatLine(now, ChatLineKind.Theirs, CleanText(p.Text), fp, e.SenderId));
                break;

            case ChatKinds.Propose:
                if (via != ChatMode.Online || Mode != ChatMode.Online || Proposal is not null) break;
                var endpoints = ValidEndpoints(p.Endpoints);
                if (endpoints.Count == 0) break;
                Proposal = new ChatProposal
                {
                    Id = p.Proposal!,
                    ProposerFingerprint = fp,
                    ProposerId = e.SenderId,
                    Endpoints = endpoints,
                    Wifi = p.Wifi,
                    Stage = ProposalStage.Asking,
                    Deadline = now.AddSeconds(ProposalSeconds),
                };
                Notice(now, ChatNotice.ProposalReceived, fp, e.SenderId);
                break;

            case ChatKinds.Vote:
                if (Proposal is not { IsMine: true, Stage: ProposalStage.Waiting } own || p.Proposal != own.Id) break;
                if (p.Yes == true) break;   // "ja" zaehlt erst, wenn derjenige lokal beitritt
                if (own.No.Add(fp))
                    Notice(now, p.Reason == "unreachable" ? ChatNotice.VoteUnreachable : ChatNotice.VoteNo, fp, e.SenderId);
                CheckAllDeclined(now);
                break;

            case ChatKinds.Switched:
                if (Mode != ChatMode.Online || via != ChatMode.Online) break;
                if (Proposal is { } current && (current.IsMine || current.Stage == ProposalStage.Joining)) break;
                var targets = ValidEndpoints(p.Endpoints);
                if (Proposal is null || Proposal.Id != p.Proposal)
                {
                    Proposal = new ChatProposal
                    {
                        Id = p.Proposal!,
                        ProposerFingerprint = fp,
                        ProposerId = e.SenderId,
                    };
                }
                if (targets.Count > 0) Proposal.Endpoints = targets;
                Proposal.Stage = ProposalStage.Countdown;
                Proposal.Deadline = now.AddSeconds(CountdownSeconds);
                Warning(now, ChatNotice.SwitchCountdown, fp, e.SenderId);
                break;

            case ChatKinds.Cancel:
                if (Proposal is not { IsMine: false } theirs || theirs.Id != p.Proposal || theirs.Stage == ProposalStage.Countdown) break;
                Proposal = null;
                Notice(now, p.Reason == "timeout" ? ChatNotice.ProposalTimeout : ChatNotice.ProposalRejected, fp, e.SenderId);
                break;

            case ChatKinds.ChessOffer:
                if (p.ChessOpponent != Me.Id || p.ChessMatch is not { } offerId) break;
                if (Chess is { Stage: not ChessGameStage.Ended }) break;   // schon eine Partie am Laufen/Anfragen
                Chess = new ChessGame { Id = offerId, OpponentFingerprint = fp, OpponentId = e.SenderId, IsMine = false, Stage = ChessGameStage.Offering };
                Notice(now, ChatNotice.ChessChallenged, fp, e.SenderId);
                break;

            case ChatKinds.ChessAccept:
                if (Chess is not { IsMine: true, Stage: ChessGameStage.Offering } myGame || myGame.Id != p.ChessMatch || fp != myGame.OpponentFingerprint) break;
                myGame.Stage = ChessGameStage.Active;
                myGame.MyColor = ChessColor.White;   // wer herausfordert, faengt an
                Notice(now, ChatNotice.ChessAccepted, fp, e.SenderId);
                break;

            case ChatKinds.ChessDecline:
                if (Chess is not { IsMine: true, Stage: ChessGameStage.Offering } declined || declined.Id != p.ChessMatch || fp != declined.OpponentFingerprint) break;
                Notice(now, ChatNotice.ChessDeclined, fp, e.SenderId);
                Chess = null;
                break;

            case ChatKinds.ChessMove:
                if (Chess is not { Stage: ChessGameStage.Active } active || active.Id != p.ChessMatch || fp != active.OpponentFingerprint) break;
                if (active.MyTurn) break;   // waere ich am Zug, kann es nicht ihr Zug gewesen sein
                if (!ChessMove.TryParse(p.ChessMove, out var incoming) || !active.Board.TryMove(incoming)) break;
                Changed();
                EndChessIfOver(active, now);
                break;

            case ChatKinds.ChessResign:
                if (Chess is not { Stage: ChessGameStage.Active or ChessGameStage.Offering } resigned || resigned.Id != p.ChessMatch || fp != resigned.OpponentFingerprint) break;
                resigned.Stage = ChessGameStage.Ended;
                resigned.EndReason = ChatNotice.ChessOpponentResigned;
                Notice(now, ChatNotice.ChessOpponentResigned, fp, e.SenderId);
                break;

            case ChatKinds.ScoreRequest:
                if (_scoreAnswers.TryGetValue(fp, out var last) && now - last < TimeSpan.FromSeconds(20)) break;
                _scoreAnswers[fp] = now;
                var request = p.Request!;
                After(now, via == ChatMode.Online ? RandomDelay(300, 2000) : RandomDelay(20, 300), at => SendScores(at, request, via));
                break;

            case ChatKinds.Scores:
                Leaderboard.Add(fp, e.SenderId, p.Scores ?? []);
                break;
        }
    }

    /// <returns>true = neu im Raum, false = schon bekannt, null = Raum voll.</returns>
    private bool? Touch(DateTimeOffset now, ChatEnvelope e, ChatMode via)
    {
        if (!_members.TryGetValue(e.SenderFingerprint, out var member))
        {
            if (_members.Count >= MaxMembers) return null;
            _members[e.SenderFingerprint] = new ChatMember { Fingerprint = e.SenderFingerprint, Id = e.SenderId, Mode = via, LastSeen = now };
            Changed();
            return true;
        }
        member.LastSeen = now;
        if (member.Mode != via)
        {
            member.Mode = via;
            Changed();
        }
        return false;
    }

    private void CompleteMySwitch(DateTimeOffset now, ChatProposal mine)
    {
        Send(now, new ChatPayload { Kind = ChatKinds.Switched, Proposal = mine.Id, Endpoints = [.. mine.Endpoints] }, ChatMode.Online);
        Proposal = null;
        Notice(now, ChatNotice.SwitchedByYou);
        EnterLocal(now, mine.Id);
    }

    private void CheckAllDeclined(DateTimeOffset now)
    {
        if (Proposal is not { IsMine: true, Stage: ProposalStage.Waiting } mine) return;
        var stillHere = mine.Asked.Where(_members.ContainsKey).ToList();
        if (stillHere.Count == 0 || stillHere.All(mine.No.Contains)) CancelMyProposal(now, "rejected");
    }

    private void CancelMyProposal(DateTimeOffset now, string reason)
    {
        if (Proposal is not { IsMine: true } mine) return;
        Send(now, new ChatPayload { Kind = ChatKinds.Cancel, Proposal = mine.Id, Reason = reason }, ChatMode.Online);
        Proposal = null;
        DropLocal();
        Notice(now, reason == "timeout" ? ChatNotice.ProposalTimeoutMine : ChatNotice.ProposalRejectedMine);
    }

    private void ScheduleHere(DateTimeOffset now, ChatMode via)
    {
        if (via != Mode) return;
        if (via == ChatMode.Local)
        {
            Send(now, new ChatPayload { Kind = ChatKinds.Here, Mode = "local" }, ChatMode.Local);
            return;
        }
        if (_hereScheduled) return;   // eine Antwort reicht fuer mehrere Beitritte kurz hintereinander
        _hereScheduled = true;
        After(now, RandomDelay(400, 2500), at =>
        {
            _hereScheduled = false;
            if (Mode == ChatMode.Online && State == ChatSessionState.Connected)
                Send(at, new ChatPayload { Kind = ChatKinds.Here, Mode = "online" }, ChatMode.Online);
        });
    }

    private void SendScores(DateTimeOffset now, string request, ChatMode via)
    {
        if (State != ChatSessionState.Connected || via != Mode) return;
        var scores = OwnScores().OrderByDescending(s => s.Points).ToList();
        if (scores.Count == 0) return;

        var sent = 0;
        for (var part = 0; part < 3 && sent < scores.Count; part++)
        {
            for (var take = Math.Min(ChatPayload.MaxScores, scores.Count - sent); take > 0; take -= 5)
            {
                var payload = new ChatPayload { Kind = ChatKinds.Scores, Request = request, Scores = [.. scores.Skip(sent).Take(take)] };
                if (!TrySend(now, payload, via)) continue;
                sent += take;
                break;
            }
        }
        Leaderboard.Add(Me.Fingerprint, Me.Id, scores);
    }

    private IReadOnlyList<ChatScore> OwnScores()
    {
        try { return _scores().Where(ChatLeaderboard.IsValid).ToList(); }
        catch { return []; }
    }

    // ==================================================================
    // Sperren
    // ==================================================================

    private bool IsBlocked(string fingerprint, DateTimeOffset now) =>
        Room.IsOpen && fingerprint != Me.Fingerprint && !_isAdmin(fingerprint) && _bans.IsBanned(fingerprint, now, out _);

    /// <summary>Sperren, Aufheben und Abgleich zaehlen nur von Admins - alles andere wird stillschweigend ignoriert.</summary>
    private void HandleModeration(DateTimeOffset now, ChatEnvelope e)
    {
        if (!Room.IsOpen) return;
        if (e.Payload.Kind == ChatKinds.BanCheck)
        {
            AnswerBanCheck(now, e.SenderFingerprint);   // die Nachfrage darf von jedem kommen, beantwortet wird sie nur vom Admin
            return;
        }
        if (!_isAdmin(e.SenderFingerprint)) return;
        foreach (var entry in e.Payload.Bans ?? [])
        {
            switch (e.Payload.Kind)
            {
                case ChatKinds.Ban: ApplyBan(now, entry, announce: true); break;
                case ChatKinds.BanList: ApplyBan(now, entry, announce: false); break;
                case ChatKinds.Unban: ApplyUnban(now, entry, announce: true); break;
            }
        }
    }

    private void ApplyBan(DateTimeOffset now, ChatBanEntry entry, bool announce)
    {
        var fp = entry.Fingerprint;
        if (_isAdmin(fp)) return;                                                   // Admins kann man nicht sperren
        if (entry.Until != 0 && entry.Until <= now.ToUnixTimeMilliseconds()) return; // schon abgelaufen
        var reason = entry.Reason ?? "";
        if (!_bans.Apply(new ChatBan(fp, entry.MemberId, entry.Until, entry.At, reason, false))) return;

        if (string.Equals(fp, Me.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            if (State != ChatSessionState.Ended) EnterSelfBan(now);
            return;
        }

        var wasMember = _members.Remove(fp);
        var hidden = _lines.RemoveAll(l => l.Kind == ChatLineKind.Theirs && string.Equals(l.Fingerprint, fp, StringComparison.OrdinalIgnoreCase));
        Leaderboard.Remove(fp);
        if (Chess is { Stage: ChessGameStage.Active or ChessGameStage.Offering } game && string.Equals(game.OpponentFingerprint, fp, StringComparison.OrdinalIgnoreCase))
        {
            game.Stage = ChessGameStage.Ended;
            game.EndReason = ChatNotice.ChessOpponentLeft;
        }
        if (Proposal is { IsMine: false, Stage: ProposalStage.Asking or ProposalStage.Declined } open &&
            string.Equals(open.ProposerFingerprint, fp, StringComparison.OrdinalIgnoreCase))
            Proposal = null;

        if (announce && (wasMember || hidden > 0))   // wer gar nicht hier war, braucht keine Meldung
        {
            if (reason.Length > 0) Notice(now, ChatNotice.BannedReason, fp, entry.MemberId, reason);
            else Notice(now, ChatNotice.Banned, fp, entry.MemberId);
        }
        Changed();
    }

    private void ApplyUnban(DateTimeOffset now, ChatBanEntry entry, bool announce)
    {
        if (!_bans.Lift(entry.Fingerprint, entry.MemberId, entry.At)) return;
        if (string.Equals(entry.Fingerprint, Me.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            if (IsBanned) LiftSelfBan(now);
            return;
        }
        if (announce) Notice(now, ChatNotice.Unbanned, entry.Fingerprint, entry.MemberId);
        Changed();
    }

    // ------------------------------------------------------------------
    // Wenn ich selbst gesperrt bin
    // ------------------------------------------------------------------

    /// <summary>Gilt fuer mich (im Offenen Chat, als Nicht-Admin) eine Sperre aus der gemerkten Liste?</summary>
    private bool RefreshSelfBan(DateTimeOffset now)
    {
        ChatBan? ban = null;
        var banned = Room.IsOpen && !IsAdmin && _bans.IsBanned(Me.Fingerprint, now, out ban);
        IsBanned = banned;
        SelfBan = banned ? ban : null;
        return banned;
    }

    private void WarnSelfBanned(DateTimeOffset now)
    {
        if (SelfBan is { Reason.Length: > 0 } ban) Warning(now, ChatNotice.YouBannedReason, null, null, ban.Reason);
        else Warning(now, ChatNotice.YouBanned);
    }

    /// <summary>
    /// Eine Sperre erreicht mich, waehrend ich im Raum bin: ab jetzt still - nichts mehr anzeigen, niemanden
    /// mehr kennen (die anderen haben mich schon entfernt), nichts mehr schicken. Verbunden bleiben aber,
    /// damit ich die Aufhebung mitbekomme.
    /// </summary>
    private void EnterSelfBan(DateTimeOffset now)
    {
        var wasBanned = IsBanned;
        if (!RefreshSelfBan(now)) return;   // schon wieder abgelaufen
        WarnSelfBanned(now);
        if (!wasBanned)
        {
            _lines.RemoveAll(l => l.Kind == ChatLineKind.Theirs);
            foreach (var other in _members.Values.Where(m => !m.IsSelf).ToList()) _members.Remove(other.Fingerprint);
            Proposal = null;
            Chess = null;
            Leaderboard.Clear();
            AskIfStillBanned(now);
        }
        Changed();
    }

    /// <summary>Sperre aufgehoben oder abgelaufen: wieder ganz normal im Raum.</summary>
    private void LiftSelfBan(DateTimeOffset now)
    {
        IsBanned = false;
        SelfBan = null;
        if (State == ChatSessionState.Ended) return;
        Notice(now, ChatNotice.YouUnbanned);
        if (State == ChatSessionState.Connected && Mode == ChatMode.Online)
            Send(now, new ChatPayload { Kind = ChatKinds.Join, Mode = "online" }, ChatMode.Online);   // wieder bekannt machen
        Changed();
    }

    /// <summary>
    /// Stille Nachfrage an den Admin ("gilt meine Sperre noch?") - jetzt und danach regelmaessig. Nur so erfahre ich
    /// von einer Aufhebung, die kam, als ich nicht im Raum war (die Aufhebung selbst wird nirgends aufbewahrt).
    /// </summary>
    private void AskIfStillBanned(DateTimeOffset now)
    {
        if (State != ChatSessionState.Connected || Mode != ChatMode.Online) return;
        Send(now, new ChatPayload { Kind = ChatKinds.BanCheck }, ChatMode.Online);
        if (_banCheckScheduled) return;
        _banCheckScheduled = true;
        After(now, BanCheckEvery, at =>
        {
            _banCheckScheduled = false;
            if (IsBanned) AskIfStillBanned(at);
        });
    }

    /// <summary>
    /// Admin: auf die Nachfrage eines Gesperrten antworten - aufgehoben -> Aufhebung nochmal schicken, weiter gesperrt
    /// -> Sperre nochmal schicken. Unbekannte werden nicht beantwortet (auch nicht gemerkt: kein Speicherverbrauch durch Fremde).
    /// </summary>
    private void AnswerBanCheck(DateTimeOffset now, string fingerprint)
    {
        if (!CanModerate || State != ChatSessionState.Connected || Mode != ChatMode.Online || _isAdmin(fingerprint)) return;
        if (_bans.Get(fingerprint) is not { } known) return;
        if (_banCheckAnswers.TryGetValue(fingerprint, out var last) && now - last < TimeSpan.FromSeconds(30)) return;
        _banCheckAnswers[fingerprint] = now;

        var entry = new ChatBanEntry
        {
            Fingerprint = known.Fingerprint, MemberId = known.MemberId, Until = known.Until, At = known.At,
            Reason = known.Reason.Length > 0 ? known.Reason : null,
        };
        if (known.Lifted) TrySend(now, new ChatPayload { Kind = ChatKinds.Unban, Bans = [entry with { Until = 0, Reason = null }] }, ChatMode.Online);
        else if (known.IsActive(now)) TrySend(now, new ChatPayload { Kind = ChatKinds.Ban, Bans = [entry] }, ChatMode.Online);
    }

    /// <summary>
    /// Der Admin schickt seine Sperrliste, wenn jemand den Raum betritt (gebuendelt, mit kleiner
    /// Verzoegerung): Chatnachrichten werden nirgends aufbewahrt, ein Neuer haette sonst nie von
    /// einer frueheren Sperre erfahren. Nur online - im lokalen Netz gibt es keinen offenen Chat.
    /// </summary>
    private void ScheduleBanSync(DateTimeOffset now)
    {
        if (_banSyncScheduled || !CanModerate || Mode != ChatMode.Online) return;
        _banSyncScheduled = true;
        After(now, RandomDelay(1500, 4000), at =>
        {
            _banSyncScheduled = false;
            SendBanList(at);
        });
    }

    private void SendBanList(DateTimeOffset now)
    {
        if (State != ChatSessionState.Connected || Mode != ChatMode.Online || !CanModerate) return;
        var active = _bans.Active(now).OrderByDescending(b => b.At).Take(4 * ChatPayload.MaxBans).ToList();   // die neuesten zuerst
        for (var i = 0; i < active.Count; i += ChatPayload.MaxBans)
        {
            var entries = active.Skip(i).Take(ChatPayload.MaxBans)
                .Select(b => new ChatBanEntry { Fingerprint = b.Fingerprint, MemberId = b.MemberId, Until = b.Until, At = b.At, Reason = b.Reason.Length > 0 ? b.Reason : null })
                .ToArray();
            TrySend(now, new ChatPayload { Kind = ChatKinds.BanList, Bans = entries }, ChatMode.Online);
        }
    }

    // ==================================================================
    // Helfer
    // ==================================================================

    private void Send(DateTimeOffset now, ChatPayload payload, ChatMode via) => TrySend(now, payload, via);

    private bool TrySend(DateTimeOffset now, ChatPayload payload, ChatMode via)
    {
        var transport = via == ChatMode.Online ? _online : _local;
        if (transport is null) return false;
        if (IsBanned && payload.Kind is not (ChatKinds.BanCheck or ChatKinds.Leave)) return false;   // gesperrt: nichts schicken ausser der stillen Nachfrage
        payload = payload with { Id = ChatFrame.NewId(), Time = now.ToUnixTimeMilliseconds() };
        byte[] frame;
        try { frame = ChatFrame.Seal(Room, Me, payload); }
        catch (ChatFrameTooLargeException) { return false; }
        Remember(payload.Id);
        transport.Send(frame);
        if (via == ChatMode.Online) _sentOnline.Enqueue(now);
        return true;
    }

    private int OnlineBudgetUsed(DateTimeOffset now)
    {
        while (_sentOnline.Count > 0 && now - _sentOnline.Peek() > TimeSpan.FromMinutes(1)) _sentOnline.Dequeue();
        return _sentOnline.Count;
    }

    private bool IsFlooding(string fingerprint, DateTimeOffset now)
    {
        if (!_incoming.TryGetValue(fingerprint, out var times)) _incoming[fingerprint] = times = new Queue<DateTimeOffset>();
        while (times.Count > 0 && now - times.Peek() > TimeSpan.FromSeconds(10)) times.Dequeue();
        if (times.Count >= 30) return true;
        times.Enqueue(now);
        return false;
    }

    private bool Remember(string id)
    {
        if (!_seen.Add(id)) return false;
        _seenOrder.Enqueue(id);
        while (_seenOrder.Count > 4096) _seen.Remove(_seenOrder.Dequeue());
        return true;
    }

    private void After(DateTimeOffset now, TimeSpan delay, Action<DateTimeOffset> run) => _timers.Add((now + delay, run));

    private static TimeSpan RandomDelay(int minMs, int maxMs) => TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(minMs, maxMs));

    private static List<string> ValidEndpoints(string[]? endpoints) =>
        (endpoints ?? []).Where(e => LanTransport.TryParseEndpoint(e, out _, out _)).Distinct().Take(ChatPayload.MaxEndpoints).ToList();

    /// <summary>Eine Zeile, ohne Steuerzeichen, Leerraum zusammengefasst.</summary>
    public static string CleanText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var chars = text.Select(c => char.IsControl(c) || char.IsWhiteSpace(c) ? ' ' : c).ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private void AddLine(ChatLine line)
    {
        _lines.Add(line);
        if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
        Changed();
    }

    private void Notice(DateTimeOffset now, string key, string? fingerprint = null, string? memberId = null, params string[] args) =>
        AddLine(new ChatLine(now, ChatLineKind.Notice, key, fingerprint, memberId, args));

    private void Warning(DateTimeOffset now, string key, string? fingerprint = null, string? memberId = null, params string[] args) =>
        AddLine(new ChatLine(now, ChatLineKind.Warning, key, fingerprint, memberId, args));

    private void Changed() => _version++;
}
