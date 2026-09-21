using Floppy.Core.Chess;

namespace Floppy.Core.Tests;

public class ChessNotationTests
{
    [Theory]
    [InlineData("e2e4")]
    [InlineData("e7e8q")]
    [InlineData("a1h8n")]
    public void Round_trips(string text)
    {
        Assert.True(ChessMove.TryParse(text, out var move));
        Assert.Equal(text, move.Notation);
    }

    [Theory]
    [InlineData("")]
    [InlineData("e2")]
    [InlineData("e2e9")]
    [InlineData("i2e4")]
    [InlineData("e2e4x")]
    public void Rejects_invalid_notation(string text) => Assert.False(ChessMove.TryParse(text, out _));
}

public class ChessBoardTests
{
    private static bool Move(ChessBoard b, string notation)
    {
        Assert.True(ChessMove.TryParse(notation, out var move));
        return b.TryMove(move);
    }

    private static ChessSquare Sq(string text)
    {
        Assert.True(ChessSquare.TryParse(text, out var s));
        return s;
    }

    [Fact]
    public void Start_position_has_white_to_move_with_correct_pawn_and_knight_moves()
    {
        var b = new ChessBoard();
        Assert.Equal(ChessColor.White, b.Turn);
        Assert.Equal(ChessStatus.Ongoing, b.Status);
        Assert.Equal(new HashSet<string> { "e3", "e4" }, b.LegalMoves(Sq("e2")).Select(m => m.To.ToString()).ToHashSet());
        Assert.Equal(new HashSet<string> { "a3", "c3" }, b.LegalMoves(Sq("b1")).Select(m => m.To.ToString()).ToHashSet());
    }

    [Fact]
    public void Cannot_capture_own_piece_or_move_before_your_turn()
    {
        var b = new ChessBoard();
        Assert.Empty(b.LegalMoves(Sq("a1")));   // Turm von eigenen Figuren eingeschlossen
        Assert.Empty(b.LegalMoves(Sq("e7")));   // schwarz ist noch nicht am Zug
    }

    [Fact]
    public void Pawn_double_step_is_blocked_by_a_piece_in_the_way()
    {
        var b = ChessBoard.Empty();
        b.Place(Sq("e2"), new ChessPiece(ChessPieceKind.Pawn, ChessColor.White));
        b.Place(Sq("e3"), new ChessPiece(ChessPieceKind.Pawn, ChessColor.Black));
        var moves = b.LegalMoves(Sq("e2"));
        Assert.DoesNotContain(moves, m => m.To.ToString() == "e3");   // besetzt
        Assert.DoesNotContain(moves, m => m.To.ToString() == "e4");   // Weg blockiert
    }

    [Fact]
    public void En_passant_capture_works_only_right_after_the_double_step()
    {
        var b = new ChessBoard();
        Move(b, "e2e4");
        Move(b, "a7a6");
        Move(b, "e4e5");
        Move(b, "d7d5");   // schwarzer Doppelschritt neben dem weissen Bauern
        Assert.Equal(Sq("d6"), b.EnPassantTarget);
        Assert.Contains(b.LegalMoves(Sq("e5")), m => m.To.ToString() == "d6");

        Assert.True(Move(b, "e5d6"));
        Assert.Null(b.At(Sq("d5")));   // der geschlagene Bauer ist weg
        Assert.Equal(new ChessPiece(ChessPieceKind.Pawn, ChessColor.White), b.At(Sq("d6")));
    }

    [Fact]
    public void Castling_kingside_both_sides_when_clear()
    {
        var b = new ChessBoard();
        Move(b, "g1f3"); Move(b, "g8f6");
        Move(b, "g2g3"); Move(b, "g7g6");
        Move(b, "f1g2"); Move(b, "f8g7");
        Assert.True(Move(b, "e1g1"));
        Assert.Equal(new ChessPiece(ChessPieceKind.King, ChessColor.White), b.At(Sq("g1")));
        Assert.Equal(new ChessPiece(ChessPieceKind.Rook, ChessColor.White), b.At(Sq("f1")));
        Assert.True(Move(b, "e8g8"));
        Assert.Equal(new ChessPiece(ChessPieceKind.King, ChessColor.Black), b.At(Sq("g8")));
    }

    [Fact]
    public void Castling_blocked_once_the_king_has_moved()
    {
        var b = new ChessBoard();
        Move(b, "g1f3"); Move(b, "b8a6");
        Move(b, "g2g3"); Move(b, "a6b8");
        Move(b, "f1g2"); Move(b, "b8a6");
        Move(b, "e1f1"); Move(b, "a6b8");   // Koenig einen Schritt und zurueck
        Move(b, "f1e1"); Move(b, "b8a6");
        Assert.DoesNotContain(b.LegalMoves(Sq("e1")), m => m.To.ToString() == "g1");
    }

    [Fact]
    public void Castling_blocked_while_passing_through_an_attacked_square()
    {
        // Schwarzer Turm auf f8 -> f-Linie leer bis f1: das Rochade-Durchgangsfeld f1 ist bedroht.
        var b = ChessBoard.Empty();
        b.Place(Sq("e1"), new ChessPiece(ChessPieceKind.King, ChessColor.White));
        b.Place(Sq("h1"), new ChessPiece(ChessPieceKind.Rook, ChessColor.White));
        b.Place(Sq("f8"), new ChessPiece(ChessPieceKind.Rook, ChessColor.Black));
        b.Place(Sq("e8"), new ChessPiece(ChessPieceKind.King, ChessColor.Black));
        Assert.DoesNotContain(b.LegalMoves(Sq("e1")), m => m.To.ToString() == "g1");
    }

    [Fact]
    public void Promotion_offers_all_four_pieces_and_replaces_the_pawn()
    {
        var b = ChessBoard.Empty();
        b.Place(Sq("a7"), new ChessPiece(ChessPieceKind.Pawn, ChessColor.White));
        b.Place(Sq("a1"), new ChessPiece(ChessPieceKind.King, ChessColor.White));
        b.Place(Sq("h8"), new ChessPiece(ChessPieceKind.King, ChessColor.Black));
        var kinds = b.LegalMoves(Sq("a7")).Where(m => m.To.ToString() == "a8").Select(m => m.Promotion).ToHashSet();
        Assert.Equal(new HashSet<ChessPieceKind?> { ChessPieceKind.Queen, ChessPieceKind.Rook, ChessPieceKind.Bishop, ChessPieceKind.Knight }, kinds);
        Assert.True(Move(b, "a7a8n"));
        Assert.Equal(new ChessPiece(ChessPieceKind.Knight, ChessColor.White), b.At(Sq("a8")));
    }

    [Fact]
    public void Pinned_piece_cannot_move_off_the_pin_line()
    {
        // Weisser Koenig e1, weisser Laeufer e2 (gefesselt), schwarzer Turm e8: der Laeufer kann gar nicht ziehen.
        var b = ChessBoard.Empty();
        b.Place(Sq("e1"), new ChessPiece(ChessPieceKind.King, ChessColor.White));
        b.Place(Sq("e2"), new ChessPiece(ChessPieceKind.Bishop, ChessColor.White));
        b.Place(Sq("e8"), new ChessPiece(ChessPieceKind.Rook, ChessColor.Black));
        b.Place(Sq("h8"), new ChessPiece(ChessPieceKind.King, ChessColor.Black));
        Assert.Empty(b.LegalMoves(Sq("e2")));
        Assert.False(b.IsInCheck(ChessColor.White));   // der Laeufer deckt noch
    }

    [Fact]
    public void King_cannot_move_into_an_attacked_square()
    {
        var b = ChessBoard.Empty();
        b.Place(Sq("e1"), new ChessPiece(ChessPieceKind.King, ChessColor.White));
        b.Place(Sq("d8"), new ChessPiece(ChessPieceKind.Rook, ChessColor.Black));
        b.Place(Sq("h8"), new ChessPiece(ChessPieceKind.King, ChessColor.Black));
        Assert.False(b.IsInCheck(ChessColor.White));   // e1 steht nicht auf der d-Linie
        var moves = b.LegalMoves(Sq("e1")).Select(m => m.To.ToString()).ToHashSet();
        Assert.DoesNotContain("d1", moves);
        Assert.DoesNotContain("d2", moves);
        Assert.Contains("f1", moves);
        Assert.Contains("e2", moves);
    }

    [Fact]
    public void Fools_mate_ends_in_checkmate()
    {
        var b = new ChessBoard();
        Assert.True(Move(b, "f2f3"));
        Assert.True(Move(b, "e7e5"));
        Assert.True(Move(b, "g2g4"));
        Assert.True(Move(b, "d8h4"));
        Assert.Equal(ChessStatus.Checkmate, b.Status);
        Assert.True(b.IsInCheck(ChessColor.White));
        Assert.Empty(b.LegalMoves(Sq("e1")));
    }

    [Fact]
    public void Queen_too_close_gives_stalemate_not_checkmate()
    {
        // Klassisches "zu nahe" Patt: schwarzer Koenig in der Ecke, von der eigenen Bedraengnis erstickt.
        var b = ChessBoard.Empty(ChessColor.Black);
        b.Place(Sq("h8"), new ChessPiece(ChessPieceKind.King, ChessColor.Black));
        b.Place(Sq("f7"), new ChessPiece(ChessPieceKind.King, ChessColor.White));
        b.Place(Sq("g6"), new ChessPiece(ChessPieceKind.Queen, ChessColor.White));
        Assert.False(b.IsInCheck(ChessColor.Black));
        Assert.Equal(ChessStatus.Stalemate, b.Status);
        Assert.Empty(b.LegalMoves(Sq("h8")));
    }
}
