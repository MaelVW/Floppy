namespace Floppy.Core.Chess;

public enum ChessColor { White, Black }

public enum ChessPieceKind { Pawn, Knight, Bishop, Rook, Queen, King }

public enum ChessStatus { Ongoing, Check, Checkmate, Stalemate }

public readonly record struct ChessPiece(ChessPieceKind Kind, ChessColor Color);

/// <summary>Ein Feld: File 0-7 (a-h), Rank 0-7 (1-8) - wie in der Schachnotation, nur nullbasiert.</summary>
public readonly record struct ChessSquare(int File, int Rank)
{
    public bool IsValid => File is >= 0 and < 8 && Rank is >= 0 and < 8;

    public static bool TryParse(ReadOnlySpan<char> text, out ChessSquare square)
    {
        square = default;
        if (text.Length != 2) return false;
        var file = char.ToLowerInvariant(text[0]) - 'a';
        var rank = text[1] - '1';
        if (file is < 0 or > 7 || rank is < 0 or > 7) return false;
        square = new ChessSquare(file, rank);
        return true;
    }

    public override string ToString() => $"{(char)('a' + File)}{Rank + 1}";
}

/// <summary>Ein Zug: von-Feld, nach-Feld, optional eine Umwandlung (nur bei einem Bauern auf der letzten Reihe).</summary>
public readonly record struct ChessMove(ChessSquare From, ChessSquare To, ChessPieceKind? Promotion = null)
{
    /// <summary>Kompakte Notation fuer die Chat-Uebertragung, z. B. "e2e4" oder "e7e8q".</summary>
    public string Notation
    {
        get
        {
            var promo = Promotion switch
            {
                ChessPieceKind.Queen => "q",
                ChessPieceKind.Rook => "r",
                ChessPieceKind.Bishop => "b",
                ChessPieceKind.Knight => "n",
                _ => "",
            };
            return From.ToString() + To.ToString() + promo;
        }
    }

    public static bool TryParse(string? text, out ChessMove move)
    {
        move = default;
        if (string.IsNullOrEmpty(text) || text.Length is not (4 or 5)) return false;
        if (!ChessSquare.TryParse(text.AsSpan(0, 2), out var from) || !ChessSquare.TryParse(text.AsSpan(2, 2), out var to)) return false;

        ChessPieceKind? promotion = null;
        if (text.Length == 5)
        {
            promotion = char.ToLowerInvariant(text[4]) switch
            {
                'q' => ChessPieceKind.Queen,
                'r' => ChessPieceKind.Rook,
                'b' => ChessPieceKind.Bishop,
                'n' => ChessPieceKind.Knight,
                _ => (ChessPieceKind?)null,
            };
            if (promotion is null) return false;
        }
        move = new ChessMove(from, to, promotion);
        return true;
    }
}
