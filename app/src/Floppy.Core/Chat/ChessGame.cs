using Floppy.Core.Chess;

namespace Floppy.Core.Chat;

public enum ChessGameStage
{
    /// <summary>Herausforderung raus oder rein - noch keine Antwort.</summary>
    Offering,

    /// <summary>Angenommen, wird gespielt.</summary>
    Active,

    /// <summary>Vorbei (Matt, Patt, abgelehnt oder aufgegeben) - Text steht in <see cref="ChessGame.EndReason"/>.</summary>
    Ended,
}

/// <summary>Eine Schachpartie gegen ein Mitglied im selben Chatraum.</summary>
public sealed class ChessGame
{
    public required string Id { get; init; }
    public required string OpponentFingerprint { get; init; }
    public required string OpponentId { get; init; }

    /// <summary>true = ich habe herausgefordert.</summary>
    public required bool IsMine { get; init; }

    public ChessGameStage Stage { get; internal set; }
    public ChessColor MyColor { get; internal set; }
    public ChessBoard Board { get; } = new();

    /// <summary><see cref="ChatNotice"/>-Schluessel, warum die Partie zu Ende ist.</summary>
    public string? EndReason { get; internal set; }

    public bool MyTurn => Stage == ChessGameStage.Active && Board.Turn == MyColor;
}
