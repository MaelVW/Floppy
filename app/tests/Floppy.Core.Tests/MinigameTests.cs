using Floppy.Core.Minigame;

namespace Floppy.Core.Tests;

public class LevelPackTests
{
    [Fact]
    public void Liest_Kopf_Level_und_Kommentare()
    {
        var pack = LevelPack.Parse("""
            ; Kommentar
            title = Test-Lager
            author = Mael

            [level] Eins
            #####
            #@$.#
            #####

            [level]
              ####
              #@$.#
              #####
            """);
        Assert.Equal("Test-Lager", pack.Title);
        Assert.Equal("Mael", pack.Author);
        Assert.Equal(2, pack.Levels.Count);
        Assert.Equal("Eins", pack.Levels[0].Name);
        Assert.Equal("Level 2", pack.Levels[1].Name);
        Assert.Equal("#@$.#", pack.Levels[1].Rows[1]);   // gemeinsame Einrueckung entfernt
        Assert.Empty(pack.Problems);
        Assert.Equal(16, pack.Id.Length);
    }

    [Theory]
    [InlineData("#####\n#$.#\n#####", "kein Spieler")]
    [InlineData("#####\n#@@$.#\n#####", "mehr als ein Spieler")]
    [InlineData("#####\n#@..$#\n#####", "Laufwerke")]
    [InlineData("#####\n#@*#\n#####", "schon geloest")]
    [InlineData("#####\n#@$.X#\n#####", "unbekanntes Zeichen")]
    [InlineData("######\n#@R  #\n######", "rote")]
    [InlineData("######\n#@B  #\n######", "blaue")]
    public void Kaputte_Level_werden_mit_Grund_uebersprungen(string rows, string fragment)
    {
        var pack = LevelPack.Parse("[level] Kaputt\n" + rows + "\n\n[level] Gut\n#####\n#@$.#\n#####");
        Assert.Single(pack.Levels);
        Assert.Contains(pack.Problems, p => p.Contains("Kaputt") && p.Contains(fragment));
    }

    [Fact]
    public void Zu_grosse_Datei_und_zu_grosse_Level_werden_abgelehnt()
    {
        Assert.NotEmpty(LevelPack.Parse(new string(';', LevelPack.MaxFileBytes + 1)).Problems);
        var wide = "[level] Breit\n#" + new string(' ', 45) + "#\n#@$.#";
        Assert.Contains(LevelPack.Parse(wide).Problems, p => p.Contains("zu gross"));
    }

    [Fact]
    public void Id_haengt_nur_von_den_Leveln_ab()
    {
        var a = LevelPack.Parse("title = A\n[level] X\n#####\n#@$.#\n#####");
        var b = LevelPack.Parse("; anderer Kommentar\ntitle = B\n[level] Y\n#####\n#@$.#\n#####");
        var c = LevelPack.Parse("[level] X\n######\n#@ $.#\n######");
        Assert.Equal(a.Id, b.Id);
        Assert.NotEqual(a.Id, c.Id);
    }

    [Fact]
    public void Eingebautes_Paket_ist_gueltig_und_jedes_Level_loesbar()
    {
        var path = BuiltInPackPath();
        if (path is null) return;
        var pack = LevelPack.Load(path);
        Assert.Empty(pack.Problems);
        Assert.True(pack.Levels.Count >= 8);
        foreach (var level in pack.Levels)
            Assert.True(Solver.IsSolvable(level), $"Level \"{level.Name}\" ist nicht loesbar");
    }

    internal static string? BuiltInPackPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "app", "src", "FloppyHub.App", "games", "diskettenlager.txt");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

public class MinigameDiscTests
{
    [Fact]
    public void Minispiel_Diskette_wird_erkannt_und_oeffnet_die_App()
    {
        using var d = new TempDisk();
        d.Write("levels.txt", "[level] A\n#####\n#@$.#\n#####");
        d.GameTxt("minigame=levels.txt");
        var plan = new LaunchPlanner(FloppyOptions.Default).Plan(d.Root).Plan!;
        Assert.Equal(LaunchKind.Game, plan.Kind);
        Assert.EndsWith("levels.txt", plan.Candidates.Single());
        Assert.Equal(GateAction.OpenGame, LaunchGate.Decide(plan, (_, _) => throw new InvalidOperationException()).Action);
    }

    [Theory]
    [InlineData("minigame=spiel.exe", "falsche Endung")]
    [InlineData(@"minigame=C:\levels.txt", "relativ")]
    [InlineData(@"minigame=..\..\levels.txt", "aus der Diskette heraus")]
    [InlineData("minigame=fehlt.txt", "fehlt auf der Diskette")]
    public void Minispiel_nur_Textdatei_auf_der_Diskette(string line, string fragment)
    {
        using var d = new TempDisk();
        d.Write("spiel.exe");
        d.GameTxt(line);
        var r = new LaunchPlanner(FloppyOptions.Default).Plan(d.Root);
        Assert.Null(r.Plan);
        Assert.Contains(r.Messages, m => m.Text.Contains(fragment));
    }

    [Fact]
    public void Steam_und_Hub_haben_Vorrang_vor_dem_Minispiel()
    {
        using var d = new TempDisk();
        d.Write("levels.txt", "x");
        d.GameTxt("minigame=levels.txt", "hub=1");
        Assert.Equal(LaunchKind.Hub, new LaunchPlanner(FloppyOptions.Default).Plan(d.Root).Plan!.Kind);
    }

    [Fact]
    public void Motor_weckt_die_App_fuer_das_Spiel()
    {
        using var d = new TempDisk();
        using var data = new TempDisk();
        d.Write("levels.txt", "[level] A\n#####\n#@$.#\n#####");
        d.GameTxt("minigame=levels.txt");
        var calls = new List<string>();
        var engine = new MotorEngine(FloppyOptions.Default, new LogFile(null), new TrustStore(Path.Combine(data.Root, "t.json")),
            new RecordingActions(calls), d.Root);
        engine.Tick();
        Assert.Equal(["app:game"], calls);
    }

    [Fact]
    public void Bespielen_schreibt_minigame_ohne_Argumente()
    {
        var p = ReferenceWriter.Prepare(ReferenceKind.Game, "levels.txt", "-egal", "Lager", now: new DateTime(2026, 9, 16, 20, 0, 0));
        Assert.True(p.IsValid);
        Assert.Equal("# geschrieben von Floppy Hub  2026-09-16 20:00\r\n# Lager\r\nminigame=levels.txt\r\n", p.Text);
        Assert.False(ReferenceWriter.Prepare(ReferenceKind.Game, "levels.exe").IsValid);
    }

    [Fact]
    public void Loeser_erkennt_unloesbare_Level()
    {
        // Diskette steckt in der Ecke fest
        Assert.False(Solver.IsSolvable(new Level("Ecke", ["#####", "#$  #", "# @.#", "#####"])));
        Assert.True(Solver.IsSolvable(new Level("Einfach", ["#####", "#@$.#", "#####"])));
    }

    private sealed class RecordingActions(List<string> calls) : IMotorActions
    {
        public void OpenSteam(string appId) => calls.Add("steam");
        public void StartProgram(string path, string? arguments) => calls.Add("start");
        public bool WakeApp(string command) { calls.Add("app:" + command); return true; }
        public bool AskWithoutApp(string target, string? arguments, string reason) => false;
    }
}

public class SokobanGameTests
{
    private static SokobanGame Game(params string[] rows) => new(new Level("Test", rows));

    [Fact]
    public void Laufen_Schieben_Loesen()
    {
        var g = Game("#######", "#@ $ .#", "#######");
        Assert.Equal(MoveResult.Moved, g.Move(Direction.Right));
        Assert.Equal(MoveResult.Pushed, g.Move(Direction.Right));
        Assert.False(g.IsSolved);
        Assert.Equal(MoveResult.Pushed, g.Move(Direction.Right));
        Assert.True(g.IsSolved);
        Assert.Equal(3, g.Moves);
        Assert.Equal(2, g.Pushes);
        Assert.Equal(MoveResult.Blocked, g.Move(Direction.Left));   // nach dem Sieg keine Zuege mehr
    }

    [Fact]
    public void Waende_und_zwei_Disketten_blockieren()
    {
        var g = Game("#######", "#@$$ .#", "#######");
        Assert.Equal(MoveResult.Blocked, g.Move(Direction.Right));
        Assert.Equal(MoveResult.Blocked, g.Move(Direction.Up));
        Assert.Equal(0, g.Moves);
        Assert.Equal(Direction.Up, g.Facing);
    }

    [Fact]
    public void Rueckgaengig_und_Neustart()
    {
        var g = Game("#######", "#@ $ .#", "#######");
        g.Move(Direction.Right);
        g.Move(Direction.Right);
        Assert.True(g.HasBox(4, 1));
        Assert.True(g.Undo());
        Assert.True(g.HasBox(3, 1));
        Assert.Equal((2, 1), g.Player);
        Assert.Equal(1, g.Moves);
        Assert.Equal(0, g.Pushes);

        g.Move(Direction.Right);
        g.Reset();
        Assert.Equal((1, 1), g.Player);
        Assert.False(g.CanUndo);
    }

    [Fact]
    public void Innen_und_aussen_und_Rand_als_Wand()
    {
        var g = Game("  ####", "###  #", "#@$. #", "######");
        Assert.True(g.IsInside(4, 2));
        Assert.False(g.IsInside(0, 0));
        Assert.True(g.IsWall(-1, 0));
        Assert.True(g.IsWall(10, 10));
        Assert.True(g.IsGoal(3, 2));
    }

    [Fact]
    public void Farbige_Diskette_passt_nur_ins_gleichfarbige_Laufwerk()
    {
        var g = Game("########", "#@R  r #", "########");
        Assert.Equal(DiskKind.Red, g.Box(2, 1)!.Value.Kind);
        Assert.Equal(DiskKind.Red, g.GoalKind(5, 1));
        g.Move(Direction.Right);
        g.Move(Direction.Right);
        g.Move(Direction.Right);
        Assert.True(g.IsSolved);
    }

    [Fact]
    public void Falsche_Farbe_zaehlt_nicht_als_geloest()
    {
        var g = Game("########", "#@R  b #", "########");   // rote Diskette, blaues Laufwerk
        for (var i = 0; i < 3; i++) g.Move(Direction.Right);
        Assert.False(g.IsSolved);
        Assert.Equal(0, g.BoxesOnGoal);
    }

    [Fact]
    public void Diskette_mit_Reichweite_blockiert_nach_dem_Aufbrauchen()
    {
        var g = Game("########", "#@1   .#", "########");   // Reichweite 1
        Assert.Equal(1, g.Box(2, 1)!.Value.RangeLeft);
        Assert.Equal(MoveResult.Pushed, g.Move(Direction.Right));
        Assert.Equal(0, g.Box(3, 1)!.Value.RangeLeft);
        Assert.Equal(MoveResult.Blocked, g.Move(Direction.Right));   // Reichweite aufgebraucht, steht wie eine Wand

        Assert.True(g.Undo());
        Assert.Equal(1, g.Box(2, 1)!.Value.RangeLeft);   // Reichweite kommt beim Rueckgaengig zurueck
    }
}

public class ScoreBoardTests
{
    [Fact]
    public void Nur_bessere_Versuche_zaehlen()
    {
        var b = new ScoreBoard("abc");
        Assert.True(b.Submit(new Score(1, 20, 5, "Mael", "2026-09-16")));
        Assert.False(b.Submit(new Score(1, 25, 1, "Tom", "2026-09-16")));
        Assert.False(b.Submit(new Score(1, 20, 5, "Tom", "2026-09-16")));
        Assert.True(b.Submit(new Score(1, 20, 4, "Tom", "2026-09-17")));
        Assert.True(b.Submit(new Score(1, 18, 9, "Lea", "2026-09-17")));
        Assert.Equal("Lea", b.Best(1)!.Name);
    }

    [Fact]
    public void Speichern_Laden_und_fremdes_Paket_ignorieren()
    {
        using var d = new TempDisk();
        var path = Path.Combine(d.Root, ScoreBoard.DiscFileName);
        var b = new ScoreBoard("pack1");
        b.Submit(new Score(1, 12, 3, "Ma;el\nX", "2026-09-16"));
        b.Submit(new Score(3, 40, 10, "", "2026-09-16"));
        b.Save(path);

        var back = ScoreBoard.Load(path, "pack1");
        Assert.Equal("MaelX", back.Best(1)!.Name);      // ; und Steuerzeichen entfernt
        Assert.Equal("Spieler", back.Best(3)!.Name);
        Assert.Empty(ScoreBoard.Load(path, "anderes").All);
    }

    [Fact]
    public void Kaputte_Datei_ergibt_leere_Liste()
    {
        using var d = new TempDisk();
        var path = d.Write("scores.txt", "pack = x\n1;abc;3;Mael;heute\n;;;;\nMuell");
        Assert.Empty(ScoreBoard.Load(path, "x").All);
    }

    [Fact]
    public void Zusammenfuehren()
    {
        var disc = new ScoreBoard("p");
        disc.Submit(new Score(1, 30, 5, "Tom", "a"));
        var local = new ScoreBoard("p");
        local.Submit(new Score(1, 25, 5, "Mael", "b"));
        local.Submit(new Score(2, 50, 5, "Mael", "b"));
        disc.Merge(local);
        Assert.Equal("Mael", disc.Best(1)!.Name);
        Assert.NotNull(disc.Best(2));
    }
}

public class LevelIdAndScoringTests
{
    [Fact]
    public void Level_id_depends_only_on_layout()
    {
        var a = new Level("Eins", ["#####", "#@$.#", "#####"]);
        var b = new Level("Ganz anders benannt", ["", "  #####  ", "  #@$.#", "  #####", ""]);
        var c = new Level("Eins", ["######", "#@ $.#", "######"]);
        var d = new Level("Eins", ["#####", "#@$.#", "#---#", "#####"]);
        var e = new Level("Eins", ["#####", "#@$.#", "#   #", "#####"]);

        Assert.Matches("^[0-9A-F]{12}$", a.Id);
        Assert.Equal(a.Id, b.Id);
        Assert.NotEqual(a.Id, c.Id);
        Assert.Equal(d.Id, e.Id);   // "-" ist Boden
        Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$", a.DisplayId);
        Assert.Equal(1, a.Boxes);
    }

    [Fact]
    public void Same_level_in_different_packs_has_same_id()
    {
        var packA = LevelPack.Parse("title = A\n[level] X\n#####\n#@$.#\n#####\n\n[level] Y\n######\n#@ $.#\n######");
        var packB = LevelPack.Parse("title = B\n[level] Umbenannt\n  #####\n  #@$.#\n  #####");
        Assert.Equal(packA.Levels[0].Id, packB.Levels[0].Id);
        Assert.NotEqual(packA.Id, packB.Id);
    }

    [Fact]
    public void Points_reward_speed_and_efficiency()
    {
        var fast = GameScoring.Points(1, 3, 1, TimeSpan.FromSeconds(2));
        Assert.Equal(1000 - 15 - 5 - 4, fast);
        Assert.True(fast > GameScoring.Points(1, 3, 1, TimeSpan.FromSeconds(30)));
        Assert.True(fast > GameScoring.Points(1, 9, 1, TimeSpan.FromSeconds(2)));
        Assert.True(GameScoring.Points(3, 50, 10, TimeSpan.FromSeconds(60)) > GameScoring.Points(1, 3, 1, TimeSpan.FromSeconds(2)));
        Assert.Equal(100, GameScoring.Points(1, 5000, 3000, TimeSpan.FromHours(3)));   // nie unter 10 %
        Assert.Equal("1:07", GameScoring.FormatTime(67_400));
        Assert.Equal("1:02:07", GameScoring.FormatTime(3_727_000));
    }

    [Fact]
    public void Records_rank_by_points_and_keep_old_files_readable()
    {
        var b = new ScoreBoard("p");
        Assert.True(b.Submit(new Score(1, 20, 5, "Alt", "2026-09-15")));                                   // alter Eintrag ohne Punkte
        Assert.True(b.Submit(new Score(1, 30, 9, "Neu", "2026-09-16", 40_000, 790, "0123456789AB", "Eins")));
        Assert.False(b.Submit(new Score(1, 10, 2, "Mehr Zuege", "2026-09-16", 90_000, 700, "0123456789AB", "Eins")));
        Assert.True(b.Submit(new Score(1, 30, 9, "Schneller", "2026-09-16", 30_000, 790, "0123456789AB", "Eins")));

        using var d = new TempDisk();
        var path = Path.Combine(d.Root, "p.txt");
        b.Save(path);
        var back = ScoreBoard.Load(path, "p").Best(1)!;
        Assert.Equal(("Schneller", 30_000L, 790, "0123456789AB", "Eins"), (back.Name, back.Millis, back.Points, back.LevelId, back.LevelName));

        File.WriteAllText(path, "pack = p\n2;14;3;Mael;2026-09-01\n");   // Format von gestern
        var old = ScoreBoard.Load(path, "p").Best(2)!;
        Assert.Equal((14, 0L, 0), (old.Moves, old.Millis, old.Points));
    }

    [Fact]
    public void Best_scores_for_chat_come_from_all_local_boards()
    {
        using var d = new TempDisk();
        var a = new ScoreBoard("packA");
        a.Submit(new Score(1, 5, 1, "Mael", "2026-09-16", 4000, 960, "AAAAAAAAAAAA", "Erste"));
        a.Submit(new Score(2, 9, 2, "Mael", "2026-09-10"));   // ohne Punkte: zaehlt nicht
        a.Save(Path.Combine(d.Root, "packA.txt"));
        var b = new ScoreBoard("packB");
        b.Submit(new Score(3, 4, 1, "Mael", "2026-09-16", 3000, 971, "AAAAAAAAAAAA", "Erste (Kopie)"));
        b.Submit(new Score(1, 40, 8, "Mael", "2026-09-16", 50000, 1700, "BBBBBBBBBBBB", "Zwei"));
        b.Save(Path.Combine(d.Root, "packB.txt"));

        var scores = ScoreBoard.BestForChat(d.Root);
        Assert.Equal(2, scores.Count);
        var first = scores.Single(s => s.LevelId == "AAAAAAAAAAAA");
        Assert.Equal(971, first.Points);
        Assert.Equal(GameScoring.GameId, first.Game);
        Assert.All(scores, s => Assert.True(Chat.ChatLeaderboard.IsValid(s)));
        Assert.Empty(ScoreBoard.BestForChat(Path.Combine(d.Root, "gibtsnicht")));
    }
}

public class MessageCodeTests
{
    [Fact]
    public void Level_problems_have_codes_for_translation()
    {
        Assert.Null(LevelPack.Check(["#####", "#@$.#", "#####"]));
        Assert.Equal("NO_PLAYER", LevelPack.Check(["#####", "#$. #", "#####"])!.Code);
        var mismatch = LevelPack.Check(["######", "#@$$.#", "######"])!;
        Assert.Equal(("COUNT_MISMATCH", "2", "1"), (mismatch.Code, mismatch.Args[0], mismatch.Args[1]));
        Assert.Equal("UNKNOWN_CHAR", LevelPack.Check(["#X#"])!.Code);
        Assert.Equal("kein Spieler (@)", LevelPack.Validate(["#####", "#$. #", "#####"]));
    }

    [Fact]
    public void Planner_and_writer_messages_carry_codes_and_arguments()
    {
        using var d = new TempDisk();
        d.GameTxt(@"pcrun=C:\Windows\System32\cmd.exe");
        var blocked = new LaunchPlanner(FloppyOptions.Default).Plan(d.Root).Messages.Single(m => m.Level == MessageLevel.Error);
        Assert.Equal("PCRUN_BLOCKED", blocked.Code);
        Assert.EndsWith("cmd.exe", blocked.Args![1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gesperrten Systemordner", blocked.Text);   // Log bleibt deutsch (wie V1)

        var steam = ReferenceWriter.Prepare(ReferenceKind.Steam, "kein spiel");
        Assert.Equal(("STEAM_UNRECOGNIZED", "kein spiel"), (steam.Messages[0].Code, steam.Messages[0].Args![0]));
    }
}

public class LevelEditorTests
{
    [Fact]
    public void New_draft_has_walls_and_player_but_is_not_playable_yet()
    {
        var draft = new LevelDraft(7, 5, "Mein Level");
        Assert.Equal('#', draft.Get(0, 0));
        Assert.Equal('@', draft.Get(3, 2));
        Assert.Contains("keine Diskette", draft.Problem());
    }

    [Fact]
    public void Painting_builds_a_valid_level()
    {
        var draft = new LevelDraft(7, 5, "Mein Level");
        Assert.True(draft.Paint(2, 2, EditorTool.Box));
        Assert.True(draft.Paint(5, 2, EditorTool.Goal));
        Assert.Null(draft.Problem());

        Assert.True(draft.Paint(1, 2, EditorTool.Player));   // nur ein Spieler
        Assert.Equal(' ', draft.Get(3, 2));
        Assert.True(draft.Paint(2, 2, EditorTool.Erase));
        Assert.True(draft.Paint(5, 2, EditorTool.Box));       // Diskette aufs Laufwerk
        Assert.Equal('*', draft.Get(5, 2));
        Assert.Contains("schon geloest", draft.Problem());
        Assert.True(draft.Paint(5, 2, EditorTool.Floor));     // Diskette weg, Laufwerk bleibt
        Assert.Equal('.', draft.Get(5, 2));
        Assert.False(draft.Paint(5, 2, EditorTool.Floor));
        Assert.True(draft.Paint(1, 2, EditorTool.Goal));      // Spieler auf Laufwerk
        Assert.Equal('+', draft.Get(1, 2));
        Assert.Contains("keine Diskette", draft.Problem());
        Assert.True(draft.Paint(1, 2, EditorTool.Erase));
        Assert.False(draft.Paint(-1, 99, EditorTool.Wall));

        var level = new LevelDraft(7, 5) { Name = "" };
        level.Paint(2, 2, EditorTool.Box);
        level.Paint(4, 2, EditorTool.Goal);
        Assert.Equal("Eigenes Level", level.ToLevel().Name);
        Assert.True(Solver.IsSolvable(level.ToLevel()));
    }

    [Fact]
    public void Resize_keeps_content_and_limits()
    {
        var draft = new LevelDraft(5, 5);
        draft.Resize(8, 3);
        Assert.Equal((8, 3), (draft.Width, draft.Height));
        Assert.Equal('#', draft.Get(0, 0));
        draft.Resize(500, 1);
        Assert.Equal((LevelPack.MaxWidth, LevelDraft.MinSize), (draft.Width, draft.Height));
    }

    [Fact]
    public void Draft_roundtrip_keeps_id()
    {
        var original = LevelPack.Parse("[level] Stapelware\n  ####\n###  #\n#@$. #\n######").Levels[0];
        var draft = LevelDraft.FromLevel(original);
        Assert.Equal("Stapelware", draft.Name);
        Assert.Equal(original.Id, draft.ToLevel().Id);
    }

    [Fact]
    public void Custom_levels_are_saved_replaced_and_deleted()
    {
        using var d = new TempDisk();
        var file = Path.Combine(d.Root, "levels", CustomLevels.FileName);
        var one = new Level("Eins", ["#####", "#@$.#", "#####"]);
        // Zeile nur aus Boden mitten im Level darf das Level beim Lesen nicht abschneiden
        var two = new Level("Zwei", ["#######", "#@ $ .#", "#     #", "#######"]);
        var hollow = new Level("Hohl", ["#####", "#@$.#", "     ", "#####"]);

        Assert.Equal(0, CustomLevels.Save(file, one, "Mael"));
        Assert.Equal(1, CustomLevels.Save(file, two, "Mael"));
        Assert.Equal(2, CustomLevels.Save(file, hollow, "Mael"));
        var pack = CustomLevels.Load(file);
        Assert.Empty(pack.Problems);
        Assert.Equal(["Eins", "Zwei", "Hohl"], pack.Levels.Select(l => l.Name));
        Assert.Equal(two.Id, pack.Levels[1].Id);
        Assert.Equal(hollow.Id, pack.Levels[2].Id);
        Assert.Equal("Mael", pack.Author);

        var changed = new Level("Eins", ["######", "#@ $.#", "######"]);
        Assert.Equal(0, CustomLevels.Save(file, changed, "Mael"));
        Assert.Equal(changed.Id, CustomLevels.Load(file).Levels[0].Id);

        Assert.Equal(0, CustomLevels.Save(file, changed with { Name = "Eins neu" }, "Mael", replaceName: "Eins"));
        Assert.Equal("Eins neu", CustomLevels.Load(file).Levels[0].Name);

        Assert.True(CustomLevels.Delete(file, "zwei"));
        Assert.Equal(2, CustomLevels.Load(file).Levels.Count);
        Assert.Throws<ArgumentException>(() => CustomLevels.Save(file, new Level("Kaputt", ["###", "#@#", "###"]), "Mael"));
    }
}

/// <summary>Breitensuche ueber Schub-Zustaende - prueft, ob ein Level loesbar ist.</summary>
internal static class Solver
{
    public static bool IsSolvable(Level level, int maxStates = 2_000_000)
    {
        var g = new SokobanGame(level);
        int w = g.Width, h = g.Height;
        var goals = new HashSet<int>();
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (g.IsGoal(x, y)) goals.Add(y * w + x);

        bool Wall(int i) => g.IsWall(i % w, i / w);
        bool DeadCorner(int i)
        {
            if (goals.Contains(i)) return false;
            int x = i % w, y = i / w;
            var up = g.IsWall(x, y - 1); var down = g.IsWall(x, y + 1);
            var left = g.IsWall(x - 1, y); var right = g.IsWall(x + 1, y);
            return (up || down) && (left || right);
        }

        var start = (Player: g.Player.Y * w + g.Player.X, Boxes: g.Boxes.Select(b => b.Y * w + b.X).OrderBy(i => i).ToArray());
        var seen = new HashSet<string>();
        var queue = new Queue<(int Player, int[] Boxes)>();
        queue.Enqueue(start);
        int[] deltas = [-w, w, -1, 1];

        while (queue.Count > 0 && seen.Count < maxStates)
        {
            var (player, boxes) = queue.Dequeue();
            var boxSet = boxes.ToHashSet();
            if (boxes.All(goals.Contains)) return true;

            // Erreichbare Felder des Spielers
            var reach = new HashSet<int> { player };
            var fill = new Queue<int>([player]);
            while (fill.Count > 0)
            {
                var c = fill.Dequeue();
                foreach (var d in deltas)
                {
                    var n = c + d;
                    if ((d == -1 && c % w == 0) || (d == 1 && c % w == w - 1)) continue;
                    if (n < 0 || n >= w * h || Wall(n) || boxSet.Contains(n) || !reach.Add(n)) continue;
                    fill.Enqueue(n);
                }
            }

            var key = reach.Min() + "|" + string.Join(",", boxes);
            if (!seen.Add(key)) continue;

            foreach (var box in boxes)
            {
                foreach (var d in deltas)
                {
                    var from = box - d;
                    var to = box + d;
                    if ((d == -1 && box % w == 0) || (d == 1 && box % w == w - 1)) continue;
                    if (from < 0 || to < 0 || from >= w * h || to >= w * h) continue;
                    if (!reach.Contains(from) || Wall(to) || boxSet.Contains(to) || DeadCorner(to)) continue;
                    var next = boxes.Select(b => b == box ? to : b).OrderBy(i => i).ToArray();
                    queue.Enqueue((box, next));
                }
            }
        }
        return false;
    }
}
