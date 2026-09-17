using Floppy.Core.Chat;
using Floppy.Core.Minigame;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>
/// Rangliste im Chatraum: fragt beim Oeffnen alle Teilnehmer nach ihren besten Minispiel-Ergebnissen
/// und zeigt Gesamtpunkte und die einzelnen Level (verglichen ueber die Level-ID).
/// </summary>
public static class LeaderboardDialog
{
    public static void Open(IAppHost host)
    {
        var chat = host.Services.Chat;
        if (chat.Session is not { State: ChatSessionState.Connected } session) return;
        session.RequestScores(DateTimeOffset.UtcNow);   // zu schnell hintereinander: dann eben die letzte Liste

        var d = new RetroDialog(Loc.T("CHAT_SCORES_TITLE"), "trophy", 660);

        var sharedOnly = new CheckBox { Text = Loc.T("CHAT_SCORES_SHARED_ONLY"), FocusMode = Control.FocusModeEnum.None };
        var status = Ui.Dim("", wrap: true);

        var totals = MakeTree(5, 130);
        totals.SetColumnTitle(0, Loc.T("CHAT_SCORES_COL_RANK"));
        totals.SetColumnTitle(1, Loc.T("CHAT_SCORES_COL_PLAYER"));
        totals.SetColumnTitle(2, Loc.T("CHAT_SCORES_COL_POINTS"));
        totals.SetColumnTitle(3, Loc.T("CHAT_SCORES_COL_LEVELS"));
        totals.SetColumnTitle(4, Loc.T("CHAT_SCORES_COL_WINS"));

        var levels = MakeTree(5, 230);
        levels.SetColumnTitle(0, Loc.T("CHAT_SCORES_COL_RANK"));
        levels.SetColumnTitle(1, Loc.T("CHAT_SCORES_COL_PLAYER"));
        levels.SetColumnTitle(2, Loc.T("CHAT_SCORES_COL_POINTS"));
        levels.SetColumnTitle(3, Loc.T("CHAT_SCORES_COL_TIME"));
        levels.SetColumnTitle(4, Loc.T("CHAT_SCORES_COL_MOVES"));

        d.Body.AddChild(Ui.HBox(12, Icons.Rect("trophy", 2f), Ui.VBox(4,
            Ui.Label(chat.RoomLabel, "TitleLabel"),
            Ui.Dim(Loc.T("CHAT_SCORES_RULE"), wrap: true)).Expand()));
        d.Body.AddChild(new GroupBox(Loc.T("CHAT_SCORES_TOTAL"), totals));
        d.Body.AddChild(new GroupBox(Loc.T("CHAT_SCORES_LEVELS"), levels));
        d.Body.AddChild(Ui.HBox(8, sharedOnly, Ui.Spacer(), status));

        var shown = -1;
        void Fill()
        {
            if (chat.Session != session) return;
            var board = session.Leaderboard;
            shown = board.Version;
            var shared = sharedOnly.ButtonPressed;

            totals.Clear();
            var totalsRoot = totals.CreateItem();
            foreach (var t in board.Totals(shared))
            {
                var item = totals.CreateItem(totalsRoot);
                Rank(item, t.Rank, highlight: t.Rank == 1 && board.Totals(shared).Count > 1);
                Player(item, chat.NameOf(t.Fingerprint, t.MemberId), t.Fingerprint == chat.Identity.Fingerprint);
                Number(item, 2, t.Points.ToString("#,0"));
                Number(item, 3, t.Levels.ToString());
                Number(item, 4, t.Wins.ToString());
            }

            levels.Clear();
            var levelsRoot = levels.CreateItem();
            var list = board.Levels(shared);
            foreach (var level in list)
            {
                var header = levels.CreateItem(levelsRoot);
                header.SetText(1, Loc.T("CHAT_SCORES_LEVEL", Loc.LevelName(level.LevelId, level.LevelName), DisplayId(level.LevelId)));
                header.SetIcon(1, Icons.Get(level.IsShared ? "tile_box_goal" : "tile_box"));
                header.SetCustomColor(1, level.IsShared ? Palette.Current.Text : Palette.Current.TextDim);
                for (var c = 0; c < 5; c++) header.SetSelectable(c, false);
                foreach (var e in level.Entries)
                {
                    var item = levels.CreateItem(header);
                    Rank(item, e.Rank, highlight: level.IsShared && e.Rank == 1);
                    Player(item, chat.NameOf(e.Fingerprint, e.MemberId), e.Fingerprint == chat.Identity.Fingerprint);
                    Number(item, 2, e.Score.Points.ToString("#,0"));
                    Number(item, 3, GameScoring.FormatTime(e.Score.Millis));
                    Number(item, 4, e.Score.Moves.ToString());
                }
            }

            status.Text = list.Count > 0 ? "" : board.IsEmpty ? Loc.T("CHAT_SCORES_WAITING") : Loc.T("CHAT_SCORES_EMPTY");
        }

        sharedOnly.Toggled += _ => Fill();
        d.AddButton(Loc.T("CHAT_SCORES_REFRESH"), () =>
        {
            if (session.RequestScores(DateTimeOffset.UtcNow) == ChatResult.TooFast)
                host.SetStatusMessage(Loc.T("CHAT_RESULT_SCORES_TOOFAST"), "warn");
            Fill();
        }, closes: false, icon: "refresh");
        d.AddButton(Loc.T("BTN_OK"), () => { });

        // Ergebnisse kommen nach und nach an
        var poll = new Timer { WaitTime = 0.3, Autostart = true };
        poll.Timeout += () =>
        {
            if (session.Leaderboard.Version != shown) Fill();
            if (session.State == ChatSessionState.Ended && shown >= 0 && session.Leaderboard.IsEmpty)
                status.Text = Loc.T("CHAT_SCORES_EMPTY");
        };
        d.AddChild(poll);

        Fill();
        d.Open(host.DialogLayer);
    }

    private static Tree MakeTree(int columns, float height)
    {
        var tree = new Tree
        {
            Columns = columns,
            HideRoot = true,
            ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row,
            CustomMinimumSize = new Vector2(0, height),
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
        };
        tree.SetColumnExpand(0, false);
        tree.SetColumnCustomMinimumWidth(0, 56);
        tree.SetColumnExpand(1, true);
        for (var c = 2; c < columns; c++)
        {
            tree.SetColumnExpand(c, false);
            tree.SetColumnCustomMinimumWidth(c, 76);
        }
        return tree;
    }

    private static void Rank(TreeItem item, int rank, bool highlight)
    {
        item.SetText(0, $"{rank}.");
        if (highlight) item.SetIcon(0, Icons.Get("trophy"));
    }

    private static void Player(TreeItem item, string name, bool self)
    {
        item.SetText(1, name);
        if (self) item.SetCustomColor(1, Palette.Current.Accent);
    }

    private static void Number(TreeItem item, int column, string text)
    {
        item.SetText(column, text);
        item.SetTextAlignment(column, HorizontalAlignment.Right);
    }

    private static string DisplayId(string id) => id.Length == 12 ? $"{id[..4]}-{id[4..8]}-{id[8..]}" : id;
}
