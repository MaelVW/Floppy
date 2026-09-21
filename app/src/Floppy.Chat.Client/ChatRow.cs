using Floppy.Core.Chat;

namespace Floppy.Chat.Client;

public enum ChatRowKind { Message, Notice, Warning }

/// <summary>
/// Eine Zeile im Verlauf, fertig zum Anzeigen (ohne Typen einer Oberflaeche).
/// <see cref="ColorIndex"/>: 1 bis 8 = Namensfarbe aus <see cref="ChatPalette"/>, 0 = Akzentfarbe (meine eigene ohne Wahl).
/// </summary>
public sealed record ChatRow(
    ChatRowKind Kind,
    string Time,
    string Name,
    int ColorIndex,
    string Text,
    bool IsMine,
    bool IsMention,
    bool IsAdmin,
    ChatLine Source);

/// <summary><see cref="Reset"/>: alles neu zeichnen (anderer Raum, Verlauf gekuerzt), sonst nur <see cref="Rows"/> anhaengen.</summary>
public sealed record FeedChange(bool Reset, IReadOnlyList<ChatRow> Rows);

public enum ChatStatusLevel { Off, Warn, Ok }

/// <summary>Verbindungszeile (Text plus Farbe der Anzeige-LED).</summary>
public readonly record struct ChatStatus(string Text, ChatStatusLevel Level);

/// <summary>Der Wechsel-ins-lokale-Netz-Vorschlag als fertiger Text plus Knopfbeschriftungen (null = kein Knopf).</summary>
public sealed record ProposalBanner(string Text, string? YesLabel, string? NoLabel, bool IsCountdown);

public sealed record MemberRow(string Name, int ColorIndex, bool IsSelf, bool IsAdmin, string Id, string ModeText);
