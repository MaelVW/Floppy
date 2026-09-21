namespace Floppy.Core.Chess;

/// <summary>
/// Schachbrett mit vollstaendigen Regeln: erlaubte Zuege (inkl. Rochade, en passant, Umwandlung),
/// Schach/Matt/Patt. Bewusst weggelassen: 50-Zuege-Regel und Stellungswiederholung (seltene
/// Sonderfaelle, fuer eine lockere Partie zwischen zwei Leuten nicht noetig).
/// </summary>
public sealed class ChessBoard
{
    private static readonly (int Df, int Dr)[] KnightOffsets =
        [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];
    private static readonly (int Df, int Dr)[] DiagonalDirs = [(1, 1), (1, -1), (-1, 1), (-1, -1)];
    private static readonly (int Df, int Dr)[] OrthogonalDirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private readonly ChessPiece?[,] _squares = new ChessPiece?[8, 8];
    private readonly List<ChessMove> _history = [];
    private bool _whiteKingMoved, _blackKingMoved;
    private bool _whiteRookAMoved, _whiteRookHMoved, _blackRookAMoved, _blackRookHMoved;

    public ChessBoard() => SetupStandard();

    private ChessBoard(bool _) { }   // fuer Clone(): leeres Brett, wird gleich befuellt

    /// <summary>Nur fuer Tests: leeres Brett, um genaue Stellungen (Patt, Fesselung, ...) direkt aufzubauen.</summary>
    internal static ChessBoard Empty(ChessColor turn = ChessColor.White) => new(true) { Turn = turn };

    /// <summary>Nur fuer Tests: eine Figur direkt setzen (kein Zug, keine Regelpruefung).</summary>
    internal void Place(ChessSquare square, ChessPiece piece) => _squares[square.File, square.Rank] = piece;

    public ChessColor Turn { get; private set; } = ChessColor.White;
    public ChessSquare? EnPassantTarget { get; private set; }
    public IReadOnlyList<ChessMove> History => _history;

    private void SetupStandard()
    {
        ChessPieceKind[] backRank =
            [ChessPieceKind.Rook, ChessPieceKind.Knight, ChessPieceKind.Bishop, ChessPieceKind.Queen,
             ChessPieceKind.King, ChessPieceKind.Bishop, ChessPieceKind.Knight, ChessPieceKind.Rook];
        for (var f = 0; f < 8; f++)
        {
            _squares[f, 0] = new ChessPiece(backRank[f], ChessColor.White);
            _squares[f, 1] = new ChessPiece(ChessPieceKind.Pawn, ChessColor.White);
            _squares[f, 6] = new ChessPiece(ChessPieceKind.Pawn, ChessColor.Black);
            _squares[f, 7] = new ChessPiece(backRank[f], ChessColor.Black);
        }
    }

    public ChessPiece? At(ChessSquare s) => At(s.File, s.Rank);
    public ChessPiece? At(int file, int rank) => file is >= 0 and < 8 && rank is >= 0 and < 8 ? _squares[file, rank] : null;

    public ChessStatus Status
    {
        get
        {
            var inCheck = IsInCheck(Turn);
            var hasMoves = SquaresOf(Turn).Any(sq => LegalMoves(sq).Count > 0);
            if (!hasMoves) return inCheck ? ChessStatus.Checkmate : ChessStatus.Stalemate;
            return inCheck ? ChessStatus.Check : ChessStatus.Ongoing;
        }
    }

    public bool IsInCheck(ChessColor color)
    {
        var king = FindKing(color);
        return king is { } k && IsSquareAttacked(k, Opponent(color));
    }

    /// <summary>Voll legale Zuege (der eigene Koenig steht danach nicht im Schach) von diesem Feld.</summary>
    public IReadOnlyList<ChessMove> LegalMoves(ChessSquare from)
    {
        var piece = At(from);
        if (piece is not { } p || p.Color != Turn) return [];

        var moves = new List<ChessMove>();
        foreach (var to in PseudoLegalTargets(from, p))
        {
            var promotes = p.Kind == ChessPieceKind.Pawn && (to.Rank == 0 || to.Rank == 7);
            var promotions = promotes
                ? new ChessPieceKind?[] { ChessPieceKind.Queen, ChessPieceKind.Rook, ChessPieceKind.Bishop, ChessPieceKind.Knight }
                : [null];
            foreach (var promo in promotions)
            {
                var move = new ChessMove(from, to, promo);
                if (!LeavesOwnKingInCheck(move)) moves.Add(move);
            }
        }
        return moves;
    }

    /// <summary>Zieht, wenn der Zug legal ist. true = ausgefuehrt.</summary>
    public bool TryMove(ChessMove move)
    {
        if (!LegalMoves(move.From).Contains(move)) return false;
        Apply(move);
        return true;
    }

    private bool LeavesOwnKingInCheck(ChessMove move)
    {
        var mover = Turn;
        var copy = Clone();
        copy.Apply(move);
        return copy.IsInCheck(mover);
    }

    private void Apply(ChessMove move)
    {
        var piece = At(move.From)!.Value;
        var isEnPassant = piece.Kind == ChessPieceKind.Pawn && move.To == EnPassantTarget && At(move.To) is null;
        var isCastle = piece.Kind == ChessPieceKind.King && Math.Abs(move.To.File - move.From.File) == 2;

        _squares[move.From.File, move.From.Rank] = null;
        _squares[move.To.File, move.To.Rank] = move.Promotion is { } promo ? new ChessPiece(promo, piece.Color) : piece;
        if (isEnPassant) _squares[move.To.File, move.From.Rank] = null;

        if (isCastle)
        {
            var rank = move.From.Rank;
            if (move.To.File == 6) { _squares[5, rank] = _squares[7, rank]; _squares[7, rank] = null; }
            else { _squares[3, rank] = _squares[0, rank]; _squares[0, rank] = null; }
        }

        if (piece.Kind == ChessPieceKind.King)
        {
            if (piece.Color == ChessColor.White) _whiteKingMoved = true; else _blackKingMoved = true;
        }
        if (move.From == new ChessSquare(0, 0)) _whiteRookAMoved = true;
        if (move.From == new ChessSquare(7, 0)) _whiteRookHMoved = true;
        if (move.From == new ChessSquare(0, 7)) _blackRookAMoved = true;
        if (move.From == new ChessSquare(7, 7)) _blackRookHMoved = true;

        EnPassantTarget = piece.Kind == ChessPieceKind.Pawn && Math.Abs(move.To.Rank - move.From.Rank) == 2
            ? new ChessSquare(move.From.File, (move.From.Rank + move.To.Rank) / 2)
            : null;

        _history.Add(move);
        Turn = Opponent(Turn);
    }

    // ------------------------------------------------------------------
    // Pseudo-legale Zuege (ohne Pruefung, ob der eigene Koenig danach im Schach steht)
    // ------------------------------------------------------------------

    private IEnumerable<ChessSquare> PseudoLegalTargets(ChessSquare from, ChessPiece piece) => piece.Kind switch
    {
        ChessPieceKind.Pawn => PawnTargets(from, piece.Color),
        ChessPieceKind.Knight => SteppedTargets(from, piece.Color, KnightOffsets),
        ChessPieceKind.Bishop => SlidingTargets(from, piece.Color, DiagonalDirs),
        ChessPieceKind.Rook => SlidingTargets(from, piece.Color, OrthogonalDirs),
        ChessPieceKind.Queen => SlidingTargets(from, piece.Color, DiagonalDirs).Concat(SlidingTargets(from, piece.Color, OrthogonalDirs)),
        ChessPieceKind.King => KingTargets(from, piece.Color),
        _ => [],
    };

    private IEnumerable<ChessSquare> PawnTargets(ChessSquare from, ChessColor color)
    {
        var dir = color == ChessColor.White ? 1 : -1;
        var startRank = color == ChessColor.White ? 1 : 6;

        var one = new ChessSquare(from.File, from.Rank + dir);
        if (one.IsValid && At(one) is null)
        {
            yield return one;
            var two = new ChessSquare(from.File, from.Rank + 2 * dir);
            if (from.Rank == startRank && At(two) is null) yield return two;
        }
        foreach (var df in new[] { -1, 1 })
        {
            var cap = new ChessSquare(from.File + df, from.Rank + dir);
            if (!cap.IsValid) continue;
            if (At(cap) is { } occ && occ.Color != color) yield return cap;
            else if (At(cap) is null && EnPassantTarget == cap) yield return cap;
        }
    }

    private IEnumerable<ChessSquare> SteppedTargets(ChessSquare from, ChessColor color, (int Df, int Dr)[] offsets)
    {
        foreach (var (df, dr) in offsets)
        {
            var to = new ChessSquare(from.File + df, from.Rank + dr);
            if (to.IsValid && (At(to) is not { } occ || occ.Color != color)) yield return to;
        }
    }

    private IEnumerable<ChessSquare> SlidingTargets(ChessSquare from, ChessColor color, (int Df, int Dr)[] dirs)
    {
        foreach (var (df, dr) in dirs)
        {
            int f = from.File + df, r = from.Rank + dr;
            while (f is >= 0 and < 8 && r is >= 0 and < 8)
            {
                var to = new ChessSquare(f, r);
                if (At(to) is { } occ)
                {
                    if (occ.Color != color) yield return to;
                    break;
                }
                yield return to;
                f += df; r += dr;
            }
        }
    }

    private IEnumerable<ChessSquare> KingTargets(ChessSquare from, ChessColor color)
    {
        for (var df = -1; df <= 1; df++)
            for (var dr = -1; dr <= 1; dr++)
            {
                if (df == 0 && dr == 0) continue;
                var to = new ChessSquare(from.File + df, from.Rank + dr);
                if (to.IsValid && (At(to) is not { } occ || occ.Color != color)) yield return to;
            }

        if (IsInCheck(color)) yield break;   // im Schach keine Rochade
        var rank = color == ChessColor.White ? 0 : 7;
        var kingMoved = color == ChessColor.White ? _whiteKingMoved : _blackKingMoved;
        if (kingMoved || from != new ChessSquare(4, rank)) yield break;
        var opponent = Opponent(color);

        var rookHMoved = color == ChessColor.White ? _whiteRookHMoved : _blackRookHMoved;
        if (!rookHMoved && At(5, rank) is null && At(6, rank) is null && At(7, rank) is { Kind: ChessPieceKind.Rook }
            && !IsSquareAttacked(new ChessSquare(5, rank), opponent) && !IsSquareAttacked(new ChessSquare(6, rank), opponent))
            yield return new ChessSquare(6, rank);

        var rookAMoved = color == ChessColor.White ? _whiteRookAMoved : _blackRookAMoved;
        if (!rookAMoved && At(3, rank) is null && At(2, rank) is null && At(1, rank) is null && At(0, rank) is { Kind: ChessPieceKind.Rook }
            && !IsSquareAttacked(new ChessSquare(3, rank), opponent) && !IsSquareAttacked(new ChessSquare(2, rank), opponent))
            yield return new ChessSquare(2, rank);
    }

    // ------------------------------------------------------------------
    // Angriffs-Erkennung (fuer Schach + Rochade) - unabhaengig von den Zuggeneratoren oben,
    // damit sich beide nicht gegenseitig aufrufen.
    // ------------------------------------------------------------------

    public bool IsSquareAttacked(ChessSquare square, ChessColor byColor)
    {
        var pawnDir = byColor == ChessColor.White ? -1 : 1;
        foreach (var df in new[] { -1, 1 })
        {
            var s = new ChessSquare(square.File + df, square.Rank + pawnDir);
            if (s.IsValid && At(s) is { Kind: ChessPieceKind.Pawn } p && p.Color == byColor) return true;
        }
        foreach (var (df, dr) in KnightOffsets)
        {
            var s = new ChessSquare(square.File + df, square.Rank + dr);
            if (s.IsValid && At(s) is { Kind: ChessPieceKind.Knight } p && p.Color == byColor) return true;
        }
        for (var df = -1; df <= 1; df++)
            for (var dr = -1; dr <= 1; dr++)
            {
                if (df == 0 && dr == 0) continue;
                var s = new ChessSquare(square.File + df, square.Rank + dr);
                if (s.IsValid && At(s) is { Kind: ChessPieceKind.King } p && p.Color == byColor) return true;
            }
        foreach (var (df, dr) in DiagonalDirs)
            if (RayHits(square, df, dr, byColor, ChessPieceKind.Bishop, ChessPieceKind.Queen)) return true;
        foreach (var (df, dr) in OrthogonalDirs)
            if (RayHits(square, df, dr, byColor, ChessPieceKind.Rook, ChessPieceKind.Queen)) return true;
        return false;
    }

    private bool RayHits(ChessSquare from, int df, int dr, ChessColor byColor, ChessPieceKind a, ChessPieceKind b)
    {
        int f = from.File + df, r = from.Rank + dr;
        while (f is >= 0 and < 8 && r is >= 0 and < 8)
        {
            if (At(f, r) is { } occ) return occ.Color == byColor && occ.Kind is var k && (k == a || k == b);
            f += df; r += dr;
        }
        return false;
    }

    // ------------------------------------------------------------------

    private IEnumerable<ChessSquare> SquaresOf(ChessColor color)
    {
        for (var f = 0; f < 8; f++)
            for (var r = 0; r < 8; r++)
                if (_squares[f, r] is { } p && p.Color == color) yield return new ChessSquare(f, r);
    }

    private ChessSquare? FindKing(ChessColor color) =>
        SquaresOf(color).Where(sq => _squares[sq.File, sq.Rank]!.Value.Kind == ChessPieceKind.King).Cast<ChessSquare?>().FirstOrDefault();

    private static ChessColor Opponent(ChessColor c) => c == ChessColor.White ? ChessColor.Black : ChessColor.White;

    private ChessBoard Clone()
    {
        var copy = new ChessBoard(true);
        Array.Copy(_squares, copy._squares, _squares.Length);
        copy.Turn = Turn;
        copy.EnPassantTarget = EnPassantTarget;
        copy._whiteKingMoved = _whiteKingMoved;
        copy._blackKingMoved = _blackKingMoved;
        copy._whiteRookAMoved = _whiteRookAMoved;
        copy._whiteRookHMoved = _whiteRookHMoved;
        copy._blackRookAMoved = _blackRookAMoved;
        copy._blackRookHMoved = _blackRookHMoved;
        return copy;
    }
}
