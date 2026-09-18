using Floppy.Core.Chat;
using Floppy.Core.Chess;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using FloppyHub.App.Services;
using Godot;

namespace FloppyHub.App.Ui.Views;

/// <summary>
/// Schach gegen ein Mitglied im Chatraum: Herausfordern passiert im Chat, gespielt wird hier.
/// Nur eine Partie gleichzeitig, kein Zeitlimit, keine 50-Zuege-/Wiederholungsregel (siehe ChessBoard).
/// Brett immer aus Weiss-Sicht (Reihe 8 oben) - bewusst nicht gedreht, haelt es einfach.
/// </summary>
public partial class ChessView : ViewBase
{
    public override string Key => "chess";

    private static readonly Color LightSquare = new("#e8d3a8");
    private static readonly Color DarkSquare = new("#9c6b3f");
    private static readonly Color SelectColor = new("#ffe07a");
    private static readonly Color TargetColor = new("#7fd88f");

    private readonly Button[,] _cells = new Button[8, 8];
    private readonly StyleBoxFlat[,] _cellStyle = new StyleBoxFlat[8, 8];
    private Label _headline = null!;
    private Label _status = null!;
    private Button _accept = null!;
    private Button _decline = null!;
    private Button _resign = null!;
    private ChessSquare? _selected;

    private ChatService Chat => Host.Services.Chat;

    protected override void Build()
    {
        _headline = Ui.Label("", "BoldLabel");
        _status = Ui.Dim("", wrap: true);
        _accept = Ui.Button(Loc.T("CHESS_BTN_ACCEPT"), "ok", () => Answer(true));
        _decline = Ui.Button(Loc.T("CHESS_BTN_DECLINE"), "error", () => Answer(false));
        _resign = Ui.Button(Loc.T("CHESS_BTN_RESIGN"), "door", Resign);

        var grid = new GridContainer { Columns = 8 };
        for (var rank = 7; rank >= 0; rank--)
        {
            for (var file = 0; file < 8; file++)
            {
                var square = new ChessSquare(file, rank);
                var style = new StyleBoxFlat();
                _cellStyle[file, rank] = style;
                var b = new Button
                {
                    CustomMinimumSize = new Vector2(48, 48),
                    FocusMode = FocusModeEnum.None,
                    IconAlignment = HorizontalAlignment.Center,
                    VerticalIconAlignment = VerticalAlignment.Center,
                    AutoTranslateMode = AutoTranslateModeEnum.Disabled,
                };
                b.AddThemeStyleboxOverride("normal", style);
                b.AddThemeStyleboxOverride("hover", style);
                b.AddThemeStyleboxOverride("pressed", style);
                b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
                b.Pressed += () => OnCellPressed(square);
                _cells[file, rank] = b;
                grid.AddChild(b);
            }
        }

        var header = Ui.VBox(2, _headline, _status).Expand();
        var board = new GroupBox(Loc.T("VIEW_CHESS"), Ui.Margin(grid, 10)) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        AddChild(Ui.VBox(10, Ui.HBox(10, header, _accept, _decline, _resign), board));

        Chat.Changed += OnChessChanged;
    }

    public override void _ExitTree() => Chat.Changed -= OnChessChanged;

    public override void OnShown() => Refresh();

    private void OnChessChanged()
    {
        if (IsVisibleInTree()) Refresh();
    }

    private void Answer(bool yes)
    {
        Chat.Session?.AnswerChessChallenge(yes, DateTimeOffset.UtcNow);
        _selected = null;
        Refresh();
    }

    private void Resign()
    {
        if (Chat.Session?.Chess is null) return;
        RetroDialog.Ask(Host.DialogLayer, Loc.T("CHESS_BTN_RESIGN"), Loc.T("CHESS_RESIGN_TEXT"), Loc.T("CHESS_BTN_RESIGN"), () =>
        {
            Chat.Session?.ResignChess(DateTimeOffset.UtcNow);
            Refresh();
        }, "warn");
    }

    private void OnCellPressed(ChessSquare square)
    {
        if (Chat.Session?.Chess is not { Stage: ChessGameStage.Active } game || !game.MyTurn)
        {
            _selected = null;
            Refresh();
            return;
        }

        if (_selected is { } from)
        {
            var candidates = game.Board.LegalMoves(from).Where(m => m.To == square).ToList();
            _selected = null;
            if (candidates.Count == 1) { PlayMove(candidates[0]); return; }
            if (candidates.Count > 1) { AskPromotion(from, square); return; }
        }
        if (game.Board.At(square) is { } p && p.Color == game.MyColor) _selected = square;
        Refresh();
    }

    private void AskPromotion(ChessSquare from, ChessSquare to)
    {
        var d = new RetroDialog(Loc.T("CHESS_PROMOTE_TITLE"), "chess", 360);
        d.Body.AddChild(Ui.Dim(Loc.T("CHESS_PROMOTE_TEXT"), wrap: true));
        var row = Ui.HBox(8);
        foreach (var kind in new[] { ChessPieceKind.Queen, ChessPieceKind.Rook, ChessPieceKind.Bishop, ChessPieceKind.Knight })
        {
            var b = Ui.Button("", IconName(new ChessPiece(kind, ChessColor.White)), () => PlayMove(new ChessMove(from, to, kind)));
            row.AddChild(b);
        }
        d.Body.AddChild(row);
        d.Open(Host.DialogLayer);
    }

    private void PlayMove(ChessMove move)
    {
        var result = Chat.Session?.MakeChessMove(move, DateTimeOffset.UtcNow) ?? ChatResult.NotConnected;
        if (result != ChatResult.Ok) Host.SetStatusMessage(Loc.T("CHESS_MOVE_FAILED"), "warn");
        Refresh();
    }

    private void Refresh()
    {
        var game = Chat.Session?.Chess;
        _accept.Visible = game is { IsMine: false, Stage: ChessGameStage.Offering };
        _decline.Visible = _accept.Visible;
        _resign.Visible = game is { Stage: ChessGameStage.Active or ChessGameStage.Offering };

        if (game is null)
        {
            _headline.Text = Loc.T("CHESS_NONE_TITLE");
            _status.Text = Loc.T("CHESS_NONE_TEXT");
            DrawBoard(null);
            return;
        }

        var name = Chat.NameOf(game.OpponentFingerprint, game.OpponentId);
        _headline.Text = Loc.T("CHESS_VS", name);
        _status.Text = game.Stage switch
        {
            ChessGameStage.Offering when game.IsMine => Loc.T("CHESS_WAITING", name),
            ChessGameStage.Offering => Loc.T("CHESS_INCOMING", name),
            ChessGameStage.Active => game.MyTurn ? Loc.T("CHESS_YOUR_TURN") : Loc.T("CHESS_THEIR_TURN", name),
            _ => game.EndReason switch
            {
                ChatNotice.ChessWon => Loc.T("CHESS_YOU_WON"),
                ChatNotice.ChessLost => Loc.T("CHESS_YOU_LOST"),
                ChatNotice.ChessStalemate => Loc.T("CHESS_DRAW"),
                ChatNotice.ChessYouResigned => Loc.T("CHESS_YOU_RESIGNED"),
                ChatNotice.ChessOpponentResigned => Loc.T("CHESS_OPPONENT_RESIGNED"),
                ChatNotice.ChessOpponentLeft => Loc.T("CHESS_OPPONENT_LEFT"),
                _ => "",
            },
        };
        DrawBoard(game);
    }

    private void DrawBoard(ChessGame? game)
    {
        var board = game?.Board;
        var legal = board is not null && _selected is { } from ? board.LegalMoves(from).Select(m => m.To).ToHashSet() : [];
        for (var file = 0; file < 8; file++)
        {
            for (var rank = 0; rank < 8; rank++)
            {
                var square = new ChessSquare(file, rank);
                var btn = _cells[file, rank];
                var piece = board?.At(square);
                btn.Icon = piece is { } p ? Icons.Get(IconName(p), 1.15f) : null;

                var baseColor = (file + rank) % 2 == 0 ? DarkSquare : LightSquare;
                _cellStyle[file, rank].BgColor = square == _selected ? SelectColor : legal.Contains(square) ? TargetColor : baseColor;
            }
        }
    }

    /// <summary>Icon-Name in <see cref="IconForge"/> - eine Silhouette pro Figur, per body-Farbe fuer Weiss/Schwarz.</summary>
    private static string IconName(ChessPiece piece) => (piece.Kind, piece.Color) switch
    {
        (ChessPieceKind.King, ChessColor.White) => "chess_wk",
        (ChessPieceKind.Queen, ChessColor.White) => "chess_wq",
        (ChessPieceKind.Rook, ChessColor.White) => "chess_wr",
        (ChessPieceKind.Bishop, ChessColor.White) => "chess_wb",
        (ChessPieceKind.Knight, ChessColor.White) => "chess_wn",
        (ChessPieceKind.Pawn, ChessColor.White) => "chess_wp",
        (ChessPieceKind.King, ChessColor.Black) => "chess_bk",
        (ChessPieceKind.Queen, ChessColor.Black) => "chess_bq",
        (ChessPieceKind.Rook, ChessColor.Black) => "chess_br",
        (ChessPieceKind.Bishop, ChessColor.Black) => "chess_bb",
        (ChessPieceKind.Knight, ChessColor.Black) => "chess_bn",
        _ => "chess_bp",
    };
}
