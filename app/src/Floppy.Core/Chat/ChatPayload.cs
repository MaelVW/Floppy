using System.Text.Json.Serialization;

namespace Floppy.Core.Chat;

/// <summary>Nachrichtenarten im Chat-Protokoll (kurz, weil jedes Byte beim Dienst zaehlt).</summary>
public static class ChatKinds
{
    /// <summary>Ich bin da (Raum betreten). Alle anderen antworten mit <see cref="Here"/>.</summary>
    public const string Join = "join";

    /// <summary>Antwort auf <see cref="Join"/>, im lokalen Netz auch regelmaessig als Lebenszeichen.</summary>
    public const string Here = "here";

    public const string Leave = "leave";
    public const string Text = "text";

    /// <summary>Vorschlag: ins lokale Netzwerk wechseln (mit Adressen des Vorschlagenden).</summary>
    public const string Propose = "propose";

    /// <summary>Antwort auf einen Vorschlag (nur "nein" geht ueber den Dienst, "ja" = lokal beitreten).</summary>
    public const string Vote = "vote";

    /// <summary>Der Raum ist gewechselt: wer nicht mitkommt, wird nach 10 Sekunden getrennt.</summary>
    public const string Switched = "switched";

    /// <summary>Vorschlag abgelehnt oder abgelaufen - alles bleibt online.</summary>
    public const string Cancel = "cancel";

    /// <summary>Rangliste anfordern.</summary>
    public const string ScoreRequest = "scoreq";

    /// <summary>Eigene besten Minispiel-Ergebnisse (Antwort auf <see cref="ScoreRequest"/>).</summary>
    public const string Scores = "scores";

    /// <summary>Schach-Herausforderung an ein bestimmtes Mitglied.</summary>
    public const string ChessOffer = "chessoffer";

    public const string ChessAccept = "chessaccept";
    public const string ChessDecline = "chessdecline";

    /// <summary>Ein Halbzug (Notation z. B. "e2e4").</summary>
    public const string ChessMove = "chessmove";

    public const string ChessResign = "chessresign";

    /// <summary>Admin: jemanden aus dem offenen Chat sperren (nur mit Admin-Unterschrift gueltig).</summary>
    public const string Ban = "ban";

    /// <summary>Admin: eine Sperre wieder aufheben.</summary>
    public const string Unban = "unban";

    /// <summary>Admin: alle aktuellen Sperren - damit auch Neue im Raum sie kennen.</summary>
    public const string BanList = "banlist";

    /// <summary>
    /// Ein Gesperrter fragt (stumm, nur der Admin antwortet): "Bin ich noch gesperrt?" - so erfaehrt er auch
    /// von einer Aufhebung, die kam, waehrend er nicht im Raum war.
    /// </summary>
    public const string BanCheck = "bancheck";

    public static bool IsKnown(string kind) => kind is Join or Here or Leave or Text or Propose or Vote or Switched or Cancel or ScoreRequest or Scores
        or ChessOffer or ChessAccept or ChessDecline or ChessMove or ChessResign or Ban or Unban or BanList or BanCheck;
}

/// <summary>Eine Sperre (oder deren Aufhebung) im Chat-Protokoll.</summary>
public sealed record ChatBanEntry
{
    /// <summary>Voller Fingerabdruck (SHA-256, 64 Hex-Zeichen) - nie die kurze ID: die laesst sich nachbauen.</summary>
    [JsonPropertyName("f")] public string Fingerprint { get; init; } = "";

    [JsonPropertyName("i")] public string MemberId { get; init; } = "";

    /// <summary>Ende der Sperre (Unix-Millisekunden), 0 = dauerhaft.</summary>
    [JsonPropertyName("u")] public long Until { get; init; }

    /// <summary>Zeitpunkt der Entscheidung (Unix-Millisekunden) - bei Widerspruechen gewinnt die neueste.</summary>
    [JsonPropertyName("t")] public long At { get; init; }

    [JsonPropertyName("r")] public string? Reason { get; init; }
}

/// <summary>Ein Minispiel-Ergebnis fuer die Rangliste im Chatraum.</summary>
public sealed record ChatScore
{
    /// <summary>Spiel, z. B. <c>diskettenlager</c> (spaeter weitere).</summary>
    [JsonPropertyName("g")] public string Game { get; init; } = "";

    /// <summary>Level-ID (aus dem Levelinhalt berechnet) - gleiche ID = gleiches Level, auch bei eigenen Leveln.</summary>
    [JsonPropertyName("l")] public string LevelId { get; init; } = "";

    [JsonPropertyName("n")] public string LevelName { get; init; } = "";
    [JsonPropertyName("m")] public int Moves { get; init; }
    [JsonPropertyName("u")] public int Pushes { get; init; }
    [JsonPropertyName("d")] public long Millis { get; init; }
    [JsonPropertyName("p")] public int Points { get; init; }
}

/// <summary>Inhalt einer Chat-Nachricht (wird unterschrieben und verschluesselt).</summary>
public sealed record ChatPayload
{
    public const int MaxTextLength = 500;
    public const int MaxEndpoints = 8;
    public const int MaxScores = 40;

    /// <summary>Sperren pro Nachricht (die Nachricht muss unter 3072 Bytes bleiben).</summary>
    public const int MaxBans = 6;

    public const int MaxBanReasonLength = 60;

    [JsonPropertyName("k")] public string Kind { get; init; } = "";

    /// <summary>Zufaellige Nachrichten-ID (gegen doppelte und wiederholte Nachrichten).</summary>
    [JsonPropertyName("i")] public string Id { get; init; } = "";

    /// <summary>Sendezeit (Unix-Millisekunden).</summary>
    [JsonPropertyName("t")] public long Time { get; init; }

    [JsonPropertyName("x")] public string? Text { get; init; }

    /// <summary>ID des Wechsel-Vorschlags.</summary>
    [JsonPropertyName("p")] public string? Proposal { get; init; }

    /// <summary>Adressen im lokalen Netz (<c>192.168.1.20:45817</c>).</summary>
    [JsonPropertyName("e")] public string[]? Endpoints { get; init; }

    [JsonPropertyName("y")] public bool? Yes { get; init; }

    /// <summary>Grund (z. B. <c>unreachable</c>, <c>rejected</c>, <c>timeout</c>).</summary>
    [JsonPropertyName("r")] public string? Reason { get; init; }

    /// <summary><c>online</c> oder <c>local</c>.</summary>
    [JsonPropertyName("n")] public string? Mode { get; init; }

    /// <summary>ID einer Ranglisten-Anfrage.</summary>
    [JsonPropertyName("q")] public string? Request { get; init; }

    [JsonPropertyName("s")] public ChatScore[]? Scores { get; init; }

    /// <summary>WLAN-Name des Vorschlagenden (nur eine Empfehlung, kann fehlen) - nur bei <see cref="ChatKinds.Propose"/>.</summary>
    [JsonPropertyName("w")] public string? Wifi { get; init; }

    /// <summary>ID der Schachpartie - bei allen <c>Chess*</c>-Nachrichten dabei.</summary>
    [JsonPropertyName("m")] public string? ChessMatch { get; init; }

    /// <summary>Ziel-ID (die installationseigene ID des Herausgeforderten) - nur bei <see cref="ChatKinds.ChessOffer"/>.</summary>
    [JsonPropertyName("o")] public string? ChessOpponent { get; init; }

    /// <summary>Zugnotation, z. B. <c>e2e4</c> oder <c>e7e8q</c> - nur bei <see cref="ChatKinds.ChessMove"/>.</summary>
    [JsonPropertyName("v")] public string? ChessMove { get; init; }

    /// <summary>Sperren - nur bei <see cref="ChatKinds.Ban"/>, <see cref="ChatKinds.Unban"/> und <see cref="ChatKinds.BanList"/>.</summary>
    [JsonPropertyName("b")] public ChatBanEntry[]? Bans { get; init; }

    private static bool IsValidBan(ChatBanEntry? b) =>
        b is { Fingerprint: { Length: 64 } fingerprint, MemberId: { Length: <= 16 } memberId } &&
        fingerprint.All(char.IsAsciiHexDigit) && !memberId.Any(char.IsControl) &&
        b.Until >= 0 && b.At > 0 &&
        (b.Reason is null || (b.Reason.Length <= MaxBanReasonLength && !b.Reason.Any(char.IsControl)));

    /// <summary>Formal gueltig? (Fremde Nachrichten werden vor dem Anzeigen geprueft.)</summary>
    public bool IsWellFormed()
    {
        if (!ChatKinds.IsKnown(Kind)) return false;
        if (Id.Length is < 8 or > 32 || !Id.All(char.IsAsciiHexDigit)) return false;
        if (Text is { Length: > MaxTextLength }) return false;
        if (Proposal is { Length: > 32 } || Request is { Length: > 32 } || Reason is { Length: > 32 } || Mode is { Length: > 16 }) return false;
        if (Wifi is { Length: > 32 }) return false;
        if (ChessMatch is { Length: > 32 } || ChessOpponent is { Length: > 16 } || ChessMove is { Length: > 8 }) return false;
        if (Endpoints is { Length: > MaxEndpoints } || Endpoints?.Any(e => e is null || e.Length > 64) == true) return false;
        if (Scores is { Length: > MaxScores }) return false;
        if (Bans is { Length: > MaxBans } || Bans?.Any(b => !IsValidBan(b)) == true) return false;
        if (Scores?.Any(s => s is null || s.Game.Length > 24 || s.LevelId.Length > 24 || s.LevelName.Length > 40 ||
                             s.Moves < 0 || s.Pushes < 0 || s.Millis < 0 || s.Points < 0) == true) return false;
        return Kind switch
        {
            ChatKinds.Text => !string.IsNullOrWhiteSpace(Text),
            ChatKinds.Propose or ChatKinds.Switched => Proposal is not null && Endpoints is { Length: > 0 },
            ChatKinds.Vote => Proposal is not null && Yes is not null,
            ChatKinds.Cancel => Proposal is not null,
            ChatKinds.ScoreRequest => Request is not null,
            ChatKinds.Scores => Request is not null && Scores is not null,
            ChatKinds.ChessOffer => ChessMatch is not null && ChessOpponent is not null,
            ChatKinds.ChessAccept or ChatKinds.ChessDecline or ChatKinds.ChessResign => ChessMatch is not null,
            ChatKinds.ChessMove => ChessMatch is not null && ChessMove is not null,
            ChatKinds.Ban or ChatKinds.Unban => Bans is { Length: 1 },
            ChatKinds.BanList => Bans is { Length: > 0 },
            _ => true,
        };
    }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChatPayload))]
internal sealed partial class ChatJsonContext : JsonSerializerContext;
