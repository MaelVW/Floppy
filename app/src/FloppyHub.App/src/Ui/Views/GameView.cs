using System.Diagnostics;
using Floppy.Core;
using Floppy.Core.Minigame;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using FloppyHub.App.Services;
using Godot;

namespace FloppyHub.App.Ui.Views;

/// <summary>
/// Minispiel "Diskettenlager": Disketten in Laufwerke schieben. Levelpakete kommen von
/// der Diskette (minigame=levels.txt), sind eingebaut oder selbst gebaut (Level-Editor).
/// Punkte gibt es fuer Zeit + Effizienz. Rekorde liegen lokal und - wenn moeglich - auch auf
/// der Diskette, sodass sie mit ihr weiterwandern.
/// </summary>
public partial class GameView : ViewBase
{
    public override string Key => "game";

    public const string BuiltInPath = "res://games/diskettenlager.txt";

    /// <summary>Bestenliste der eigenen Level (fester Name: das Paket aendert sich beim Bearbeiten).</summary>
    private const string CustomScoreKey = "eigene-level";

    private sealed record PackSource(string Label, LevelPack Pack, string? DiscFile, string ScoreKey, bool Custom = false);

    private readonly List<PackSource> _sources = [];
    private readonly Stopwatch _clock = new();
    private PackSource? _current;
    private ScoreBoard _scores = new("");
    private int _levelIndex;
    private Level? _testLevel;

    private HBoxContainer _packRow = null!;
    private HBoxContainer _counters = null!;
    private OptionButton _packSelect = null!;
    private Button _editorButton = null!;
    private Tree _levels = null!;
    private Label _packInfo = null!;
    private Label _record = null!;
    private Label _levelId = null!;
    private GroupBox _left = null!;
    private HBoxContainer _playArea = null!;
    private GroupBox _boardBox = null!;
    private SokobanBoard _board = null!;
    private LcdCounter _moves = null!;
    private LcdCounter _pushes = null!;
    private LcdCounter _levelNumber = null!;
    private LcdCounter _time = null!;
    private Button _prev = null!;
    private Button _next = null!;
    private Button _undo = null!;
    private Button _backToEditor = null!;
    private LevelEditorPanel _editor = null!;
    private bool _levelsChanged;

    public static string BuiltInPackText()
    {
        using var file = Godot.FileAccess.Open(BuiltInPath, Godot.FileAccess.ModeFlags.Read);
        return file?.GetAsText() ?? "";
    }

    private string CustomFile => System.IO.Path.Combine(Host.Services.Paths.LevelsDir, CustomLevels.FileName);

    protected override void Build()
    {
        _packSelect = new OptionButton { CustomMinimumSize = new Vector2(240, 0), ClipText = true };
        _packSelect.ItemSelected += i => SelectSource((int)i);
        var reload = Ui.Button(Loc.T("GAME_RELOAD_DISC"), "refresh", () => LoadSources(preferDisc: true));
        var makeDisc = Ui.Button(Loc.T("BTN_WRITE_DISC"), "write", () => Host.OpenWriteGame(_current?.Custom == true ? CustomFile : null));
        makeDisc.TooltipText = Loc.T("GAME_MAKE_DISC");
        _packRow = Ui.HBox(8, Ui.Label(Loc.T("GAME_PACK")), _packSelect, reload, makeDisc);

        _editorButton = Ui.Button(Loc.T("GAME_BTN_EDITOR"), "pencil", () => ShowEditor(!_editor.Visible));

        _moves = new LcdCounter(3);
        _pushes = new LcdCounter(3);
        _levelNumber = new LcdCounter(2);
        _time = new LcdCounter(4) { TimeMode = true };
        VBoxContainer Counter(string key, LcdCounter lcd)
        {
            var caption = Ui.Dim(Loc.T(key));
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            return Ui.VBox(1, caption, lcd);
        }
        _counters = Ui.HBox(8, Counter("GAME_LEVEL", _levelNumber), Counter("GAME_TIME", _time), Counter("GAME_MOVES", _moves), Counter("GAME_PUSHES", _pushes));

        _counters.Alignment = BoxContainer.AlignmentMode.Center;
        AddChild(Ui.HBox(8, _packRow, Ui.Spacer(), _editorButton));

        // ---- links: Levelliste ----
        _levels = new Tree
        {
            Columns = 3,
            HideRoot = true,
            ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row,
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
        }.Expand(vertical: true);
        _levels.SetColumnTitle(0, "#");
        _levels.SetColumnTitle(1, Loc.T("GAME_COL_LEVEL"));
        _levels.SetColumnTitle(2, Loc.T("GAME_COL_RECORD"));
        _levels.SetColumnExpand(0, false);
        _levels.SetColumnCustomMinimumWidth(0, 34);
        _levels.SetColumnExpand(1, true);
        _levels.SetColumnExpand(2, false);
        _levels.SetColumnCustomMinimumWidth(2, 64);
        _levels.ItemSelected += () =>
        {
            var index = _levels.GetSelected()?.GetMetadata(0).AsInt32() ?? -1;
            if (index >= 0 && index != _levelIndex) SelectLevel(index);
        };

        _packInfo = Ui.Dim("", wrap: true);
        _record = Ui.Label("", wrap: true);
        _levelId = Ui.Dim("");
        _levelId.TooltipText = Loc.T("GAME_LEVEL_ID_TIP");
        _levelId.MouseFilter = MouseFilterEnum.Pass;
        _left = new GroupBox(Loc.T("GAME_LEVELS"), Ui.VBox(6, _levels, _record, _levelId, _packInfo));
        _left.CustomMinimumSize = new Vector2(250, 0);

        // ---- Mitte: Spielfeld ----
        _board = new SokobanBoard();
        _board.Changed += OnBoardChanged;
        _board.Restarted += ResetClock;
        _board.Solved += OnSolved;

        _prev = Ui.Button("« " + Loc.T("GAME_PREV"), null, () => SelectLevel(_levelIndex - 1));
        _next = Ui.Button(Loc.T("GAME_NEXT") + " »", null, () => SelectLevel(_levelIndex + 1));
        _undo = Ui.Button(Loc.T("GAME_UNDO"), null, () => _board.UndoStep());
        var restart = Ui.Button(Loc.T("GAME_RESTART"), "refresh", () => _board.Restart());
        _backToEditor = Ui.Button("« " + Loc.T("EDITOR_BACK"), "pencil", EndTest);
        _backToEditor.Visible = false;
        foreach (var b in new[] { _prev, _next, _undo, restart, _backToEditor }) b.FocusMode = FocusModeEnum.None;

        _boardBox = new GroupBox("", Ui.VBox(8,
            _counters,
            _board,
            Ui.HBox(6, _backToEditor, _prev, restart, _undo, _next, Ui.Spacer(), Ui.Dim(Loc.T("GAME_KEYS")))));
        _boardBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _boardBox.SizeFlagsVertical = SizeFlags.ExpandFill;

        _playArea = Ui.HBox(10, _left, _boardBox).Expand(vertical: true);
        AddChild(_playArea);

        // ---- Level-Editor ----
        _editor = new LevelEditorPanel { Visible = false };
        _editor.Init(Host);
        _editor.TestRequested += StartTest;
        _editor.LevelsChanged += () => _levelsChanged = true;
        AddChild(_editor);
    }

    public override void OnShown()
    {
        if (_current is null) LoadSources(preferDisc: true);
        if (_editor.Visible) _editor.OnShown();
        else _board.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void OpenEditor()
    {
        if (!_editor.Visible) ShowEditor(true);
    }

    /// <summary>Motor meldet eine Minispiel-Diskette.</summary>
    public void OpenDiscPack()
    {
        if (_editor.Visible || _testLevel is not null) ShowEditor(false);
        LoadSources(preferDisc: true);
    }

    // ------------------------------------------------------------------
    // Pakete
    // ------------------------------------------------------------------

    private void LoadSources(bool preferDisc, bool preferCustom = false)
    {
        var previous = _current;
        _sources.Clear();
        var builtIn = LevelPack.Parse(BuiltInPackText());
        _sources.Add(new PackSource(Loc.T("GAME_PACK_BUILTIN"), builtIn, null, builtIn.Id));

        var custom = CustomLevels.Load(CustomFile);
        if (custom.Levels.Count > 0)
            _sources.Add(new PackSource(Loc.T("GAME_PACK_CUSTOM", custom.Levels.Count), custom, null, CustomScoreKey, Custom: true));

        var disc = DiscState.Read(Host.Services);
        if (disc.Plan is { Kind: LaunchKind.Game } plan)
        {
            var pack = LevelPack.Load(plan.Candidates[0]);
            var root = disc.Root.Length <= 3 ? disc.Root.TrimEnd('\\') : Loc.T("DISC_TESTFOLDER");
            _sources.Add(new PackSource(Loc.T("GAME_PACK_DISC", root, pack.Title), pack, plan.Candidates[0], pack.Id));
        }

        _packSelect.Clear();
        foreach (var s in _sources) _packSelect.AddItem(s.Label);
        var discIndex = _sources.FindIndex(s => s.DiscFile is not null);
        var customIndex = _sources.FindIndex(s => s.Custom);
        if (discIndex < 0)
        {
            _packSelect.AddItem(Loc.T("GAME_PACK_NO_DISC"));
            _packSelect.SetItemDisabled(_packSelect.ItemCount - 1, true);
        }

        var index = preferDisc && discIndex >= 0 ? discIndex
            : preferCustom && customIndex >= 0 ? customIndex
            : previous?.DiscFile is not null && discIndex >= 0 ? discIndex
            : previous?.Custom == true && customIndex >= 0 ? customIndex
            : 0;
        _packSelect.Select(index);
        SelectSource(index);
    }

    private void SelectSource(int index)
    {
        if (index < 0 || index >= _sources.Count) return;
        _current = _sources[index];
        _scores = LoadScores(_current);

        var pack = _current.Pack;
        var info = new List<string>();
        if (pack.Author.Length > 0) info.Add(Loc.T("GAME_BY", pack.Author));
        info.Add(Loc.T("GAME_LEVEL_COUNT", pack.Levels.Count));
        if (pack.Problems.Count > 0) info.Add(Loc.T("GAME_PROBLEMS", pack.Problems.Count));
        _packInfo.Text = string.Join(" · ", info);
        _packInfo.TooltipText = string.Join("\n", pack.Problems);
        _packInfo.MouseFilter = MouseFilterEnum.Pass;

        _levelIndex = -1;
        FillLevels();
        if (pack.Levels.Count == 0)
        {
            _boardBox.Title = Loc.T("GAME_NO_LEVELS");
            return;
        }
        // beim ersten noch nicht geschafften Level weitermachen
        var firstOpen = Enumerable.Range(0, pack.Levels.Count).FirstOrDefault(i => _scores.Best(i + 1, pack.Levels[i].Id) is null, 0);
        SelectLevel(firstOpen);
    }

    private void FillLevels()
    {
        _levels.Clear();
        var root = _levels.CreateItem();
        var pack = _current?.Pack;
        if (pack is null) return;
        for (var i = 0; i < pack.Levels.Count; i++)
        {
            var item = _levels.CreateItem(root);
            var best = _scores.Best(i + 1, pack.Levels[i].Id);
            item.SetText(0, (i + 1).ToString());
            item.SetText(1, Loc.LevelName(pack.Levels[i].Id, pack.Levels[i].Name));
            item.SetIcon(1, Icons.Get(best is null ? "tile_box" : "tile_box_goal"));
            item.SetText(2, best is null ? "–" : best.Points > 0 ? best.Points.ToString("#,0") : "✓");
            item.SetTextAlignment(2, HorizontalAlignment.Right);
            item.SetTooltipText(1, Loc.T("GAME_LEVEL_ID", pack.Levels[i].DisplayId));
            item.SetMetadata(0, i);
            if (i == _levelIndex) item.Select(0);
        }
    }

    private void SelectLevel(int index)
    {
        var pack = _current?.Pack;
        if (pack is null || index < 0 || index >= pack.Levels.Count) return;
        _levelIndex = index;
        var level = pack.Levels[index];
        _board.Load(level);
        _boardBox.Title = Loc.T("GAME_LEVEL_TITLE", index + 1, Loc.LevelName(level.Id, level.Name));
        _levelNumber.Value = index + 1;
        _prev.Disabled = index == 0;
        _next.Disabled = index >= pack.Levels.Count - 1;
        _levelId.Text = Loc.T("GAME_LEVEL_ID", level.DisplayId);

        var root = _levels.GetRoot();
        var item = root?.GetChild(index);
        item?.Select(0);
        ShowRecord();
    }

    private void ShowRecord()
    {
        var level = _current?.Pack.Levels.ElementAtOrDefault(_levelIndex);
        var best = level is null ? null : _scores.Best(_levelIndex + 1, level.Id);
        _record.Text = best is null ? Loc.T("GAME_NO_RECORD") : RecordText(best);
    }

    private static string RecordText(Score s) => Loc.T("GAME_RECORD", s.Points.ToString("#,0"), s.Moves, s.Pushes,
        s.Millis > 0 ? GameScoring.FormatTime(s.Millis) : "–", s.Name, s.Date);

    // ------------------------------------------------------------------
    // Stoppuhr
    // ------------------------------------------------------------------

    private void OnBoardChanged()
    {
        UpdateCounters();
        // Die Uhr laeuft ab dem ersten Zug (und nach einer Pause mit dem naechsten Zug weiter)
        if (_board.Game is { Moves: > 0, IsSolved: false } && !_clock.IsRunning && IsVisibleInTree()) _clock.Start();
    }

    private void ResetClock()
    {
        _clock.Reset();
        _time.Value = 0;
    }

    public override void _Process(double delta)
    {
        if (_clock.IsRunning) _time.Value = (int)_clock.Elapsed.TotalSeconds;
    }

    public override void _Notification(int what)
    {
        base._Notification(what);
        // Pause, wenn man die Ansicht wechselt oder das Fenster verlaesst
        if (what == NotificationVisibilityChanged && !IsVisibleInTree()) _clock.Stop();
        else if (what == NotificationApplicationFocusOut) _clock.Stop();
    }

    private void UpdateCounters()
    {
        var g = _board.Game;
        _moves.Value = g?.Moves ?? 0;
        _pushes.Value = g?.Pushes ?? 0;
        _undo.Disabled = g is null || !g.CanUndo || g.IsSolved;
    }

    // ------------------------------------------------------------------
    // Editor
    // ------------------------------------------------------------------

    private void ShowEditor(bool on)
    {
        _clock.Stop();
        if (_testLevel is not null) LeaveTestMode();
        _editor.Visible = on;
        _playArea.Visible = !on;
        _packRow.Visible = !on;
        _counters.Visible = !on;
        _editorButton.Text = Loc.T(on ? "GAME_BTN_PLAY" : "GAME_BTN_EDITOR");
        _editorButton.Icon = Icons.Get(on ? "tile_player" : "pencil");
        if (on)
        {
            _editor.OnShown();
            return;
        }
        if (_levelsChanged)
        {
            _levelsChanged = false;
            LoadSources(preferDisc: false, preferCustom: true);
        }
        else if (_current is not null)
        {
            SelectLevel(Math.Max(0, _levelIndex));
        }
        _board.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void StartTest(Level level)
    {
        _testLevel = level;
        _editor.Visible = false;
        _playArea.Visible = true;
        _left.Visible = false;
        _packRow.Visible = false;
        _editorButton.Visible = false;
        _counters.Visible = true;
        _prev.Visible = _next.Visible = false;
        _backToEditor.Visible = true;
        _board.Load(level);
        _levelNumber.Value = 0;
        _boardBox.Title = Loc.T("EDITOR_TESTING", level.Name);
    }

    private void LeaveTestMode()
    {
        _testLevel = null;
        _left.Visible = true;
        _editorButton.Visible = true;
        _prev.Visible = _next.Visible = true;
        _backToEditor.Visible = false;
    }

    private void EndTest()
    {
        if (_testLevel is null) return;
        ShowEditor(true);
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (_testLevel is not null && e is InputEventKey { Pressed: true, Keycode: Godot.Key.Escape } && IsVisibleInTree() && Host.DialogLayer.GetChildCount() == 0)
        {
            GetViewport().SetInputAsHandled();
            EndTest();
        }
    }

    // ------------------------------------------------------------------
    // Geschafft + Rekorde
    // ------------------------------------------------------------------

    private void OnSolved()
    {
        _clock.Stop();
        var elapsed = _clock.Elapsed;
        _time.Value = (int)elapsed.TotalSeconds;
        UpdateCounters();

        var g = _board.Game!;
        if (_testLevel is not null)
        {
            var t = new RetroDialog(Loc.T("GAME_SOLVED_TITLE"), "tile_box_goal", 420);
            t.Body.AddChild(Ui.HBox(12, Icons.Rect("tile_box_goal", 3f), Ui.VBox(4,
                Ui.Label(Loc.T("EDITOR_TEST_SOLVED"), "TitleLabel", wrap: true),
                Ui.Label(Loc.T("GAME_SOLVED_TEXT", g.Moves, g.Pushes, GameScoring.FormatTime((long)elapsed.TotalMilliseconds)), wrap: true)).Expand()));
            t.AddButton(Loc.T("GAME_AGAIN"), () => _board.Restart());
            t.AddButton("« " + Loc.T("EDITOR_BACK"), EndTest);
            t.Cancelled += EndTest;
            t.Open(Host.DialogLayer);
            return;
        }

        var pack = _current!.Pack;
        var level = pack.Levels[_levelIndex];
        var levelNo = _levelIndex + 1;
        var hasNext = _levelIndex < pack.Levels.Count - 1;
        var points = GameScoring.Points(level, g.Moves, g.Pushes, elapsed);
        var candidate = new Score(levelNo, g.Moves, g.Pushes, "", DateTime.Today.ToString("yyyy-MM-dd"),
            (long)elapsed.TotalMilliseconds, points, level.Id, level.Name);
        var old = _scores.Best(levelNo, level.Id);
        var record = _scores.IsRecord(candidate);

        var d = new RetroDialog(Loc.T("GAME_SOLVED_TITLE"), "tile_box_goal", 440);
        var pointsLabel = Ui.Label(Loc.T("GAME_SOLVED_POINTS", points.ToString("#,0")), "TitleLabel");
        pointsLabel.TooltipText = Loc.T("GAME_POINTS_RULE");
        pointsLabel.MouseFilter = MouseFilterEnum.Pass;
        d.Body.AddChild(Ui.HBox(12, Icons.Rect("tile_box_goal", 3f), Ui.VBox(4,
            Ui.Label(Loc.T(record ? "GAME_SOLVED_RECORD" : "GAME_SOLVED_HEADLINE"), "BoldLabel"),
            pointsLabel,
            Ui.Label(Loc.T("GAME_SOLVED_TEXT", g.Moves, g.Pushes, GameScoring.FormatTime(candidate.Millis)), wrap: true),
            Ui.Dim(old is not null && !record ? RecordText(old) : Loc.T("GAME_POINTS_RULE"), wrap: true)).Expand()));

        LineEdit? name = null;
        if (record)
        {
            name = new LineEdit { Text = Host.Services.Settings.PlayerName, MaxLength = ScoreBoard.MaxNameLength, PlaceholderText = Loc.T("GAME_NAME_PLACEHOLDER") };
            d.Body.AddChild(Ui.HBox(8, Ui.Label(Loc.T("GAME_NAME")), name.Expand()));
        }

        var saved = false;
        void SaveIfRecord()
        {
            if (name is null || saved) return;
            saved = true;
            var player = ScoreBoard.CleanName(name.Text);
            Host.Services.Settings.PlayerName = player;
            Host.Services.SaveSettings();
            _scores.Submit(candidate with { Name = player });
            SaveScores();
            FillLevels();
            ShowRecord();
        }

        d.AddButton(Loc.T("GAME_AGAIN"), () => { SaveIfRecord(); _board.Restart(); });
        var primary = hasNext
            ? d.AddButton(Loc.T("GAME_NEXT") + " »", () => { SaveIfRecord(); SelectLevel(_levelIndex + 1); })
            : d.AddButton(Loc.T("BTN_OK"), SaveIfRecord);
        d.Cancelled += SaveIfRecord;
        d.Open(Host.DialogLayer);

        // Name eintippen, Enter = weiter
        if (name is not null)
        {
            name.TextSubmitted += _ => primary.EmitSignal(BaseButton.SignalName.Pressed);
            Callable.From(() => { name.GrabFocus(); name.SelectAll(); }).CallDeferred();
        }
        else
        {
            primary.CallDeferred(Control.MethodName.GrabFocus);
        }

        if (!hasNext && record && Enumerable.Range(0, pack.Levels.Count - 1).All(i => _scores.Best(i + 1, pack.Levels[i].Id) is not null))
            Host.SetStatusMessage(Loc.T("GAME_ALL_DONE"), "tile_box_goal");
    }

    private ScoreBoard LoadScores(PackSource source)
    {
        var board = ScoreBoard.Load(LocalScoreFile(source), source.ScoreKey);
        if (source.DiscFile is not null)
            board.Merge(ScoreBoard.Load(DiscScoreFile(source.DiscFile), source.ScoreKey));
        return board;
    }

    private void SaveScores()
    {
        var s = Host.Services;
        if (s.ReadOnlyMode || _current is null) return;

        try
        {
            var local = LocalScoreFile(_current);
            FloppyPaths.EnsureDirectory(System.IO.Path.GetDirectoryName(local)!);
            _scores.Save(local);
        }
        catch (Exception ex)
        {
            s.Log.Write(LogLevel.Warn, $"App: Rekorde nicht gespeichert: {ex.Message}");
        }

        if (_current.DiscFile is null) return;
        if (!File.Exists(_current.DiscFile))
        {
            Host.SetStatusMessage(Loc.T("GAME_SCORE_DISC_GONE"), "warn");
            return;
        }

        // Rekord auf die Diskette - der Motor soll das nicht als "neue Diskette" starten.
        WriteGuard.Begin(s.Paths.UserData);
        try
        {
            _scores.Save(DiscScoreFile(_current.DiscFile));
            WriteGuard.Note(s.Paths.UserData, DiskSignature.Compute(s.DriveRoot));
            Host.SetStatusMessage(Loc.T("GAME_SCORE_ON_DISC"), "floppy_small");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            WriteGuard.Cancel(s.Paths.UserData);
            Host.SetStatusMessage(Loc.T("GAME_SCORE_READONLY"), "warn");
        }
    }

    private string LocalScoreFile(PackSource source) =>
        System.IO.Path.Combine(Host.Services.Paths.ScoresDir, source.ScoreKey + ".txt");

    private static string DiscScoreFile(string packFile) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(packFile)!, ScoreBoard.DiscFileName);
}
