using Floppy.Core;
using Floppy.Core.Minigame;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>
/// Level-Editor fuer "Diskettenlager": malen, testen, speichern. Eigene Level landen in
/// <c>%LOCALAPPDATA%\FloppyHub\levels\eigene-level.txt</c> und bekommen eine Level-ID aus ihrem
/// Aufbau - gleiche ID heisst gleiches Level, auch in der Rangliste im Chat.
/// </summary>
public partial class LevelEditorPanel : VBoxContainer
{
    private IAppHost _host = null!;
    private LevelEditorCanvas _canvas = null!;
    private LineEdit _name = null!;
    private SpinBox _width = null!;
    private SpinBox _height = null!;
    private Label _state = null!;
    private Tree _list = null!;
    private Button _delete = null!;
    private string? _loadedName;
    private bool _dirty;
    private bool _syncing;

    /// <summary>Level ausprobieren.</summary>
    public event Action<Level>? TestRequested;

    /// <summary>Ein eigenes Level wurde gespeichert oder geloescht.</summary>
    public event Action? LevelsChanged;

    public string CustomFile => System.IO.Path.Combine(_host.Services.Paths.LevelsDir, CustomLevels.FileName);

    public void Init(IAppHost host)
    {
        _host = host;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);

        _name = new LineEdit { MaxLength = 40, PlaceholderText = Loc.T("EDITOR_NAME_DEFAULT"), CustomMinimumSize = new Vector2(200, 0) };
        _name.TextChanged += t =>
        {
            _canvas.Draft.Name = t;
            MarkDirty();
        };
        _width = new SpinBox { MinValue = LevelDraft.MinSize, MaxValue = LevelPack.MaxWidth, Step = 1 };
        _height = new SpinBox { MinValue = LevelDraft.MinSize, MaxValue = LevelPack.MaxHeight, Step = 1 };
        _width.ValueChanged += _ => Resize();
        _height.ValueChanged += _ => Resize();

        AddChild(Ui.HBox(8,
            Ui.Label(Loc.T("EDITOR_NAME")), _name,
            Ui.Label(Loc.T("EDITOR_SIZE")), _width, Ui.Label("×"), _height,
            Ui.Spacer(),
            Ui.Button(Loc.T("EDITOR_NEW"), "add", () => ConfirmDiscard(() => Load(NewDraft(), null))),
            Ui.Button(Loc.T("EDITOR_TEST"), "start", Test),
            Ui.Button(Loc.T("EDITOR_SAVE"), "ok", Save)));

        // ---- links: Werkzeuge + eigene Level ----
        var tools = Ui.VBox(2);
        var group = new ButtonGroup();
        foreach (var (tool, key, icon) in new[]
                 {
                     (EditorTool.Wall, "EDITOR_TOOL_WALL", "tile_wall"),
                     (EditorTool.Floor, "EDITOR_TOOL_FLOOR", "tile_floor"),
                     (EditorTool.Goal, "EDITOR_TOOL_GOAL", "tile_goal"),
                     (EditorTool.Box, "EDITOR_TOOL_BOX", "tile_box"),
                     (EditorTool.Player, "EDITOR_TOOL_PLAYER", "tile_player"),
                     (EditorTool.Erase, "EDITOR_TOOL_ERASE", "remove"),
                 })
        {
            var b = Ui.Button(Loc.T(key), icon, () => _canvas.Tool = tool);
            b.ToggleMode = true;
            b.ButtonGroup = group;
            b.ButtonPressed = tool == EditorTool.Wall;
            b.Alignment = HorizontalAlignment.Left;
            b.FocusMode = FocusModeEnum.None;
            tools.AddChild(b);
        }

        _list = new Tree { HideRoot = true, SelectMode = Tree.SelectModeEnum.Row, CustomMinimumSize = new Vector2(0, 120), AutoTranslateMode = AutoTranslateModeEnum.Disabled };
        _list.SizeFlagsVertical = SizeFlags.ExpandFill;
        _list.ItemActivated += OpenSelected;
        _list.ItemSelected += () => _delete.Disabled = _list.GetSelected()?.GetMetadata(0).AsString() is not { Length: > 0 };
        _delete = Ui.Button(Loc.T("EDITOR_DELETE"), "remove", DeleteSelected);
        _delete.Disabled = true;
        var toDisc = Ui.Button(Loc.T("EDITOR_TO_DISC"), "write", () =>
        {
            if (File.Exists(CustomFile)) _host.OpenWriteGame(CustomFile);
        });

        var myLevels = new GroupBox(Loc.T("EDITOR_MY_LEVELS"), Ui.VBox(6, _list, Ui.HBox(6, _delete, toDisc)));
        myLevels.SizeFlagsVertical = SizeFlags.ExpandFill;
        var left = Ui.VBox(8, new GroupBox(Loc.T("EDITOR_TOOLS"), tools), myLevels);
        left.CustomMinimumSize = new Vector2(220, 0);

        // ---- Mitte: Zeichenflaeche ----
        _canvas = new LevelEditorCanvas();
        _canvas.Changed += MarkDirty;
        _state = Ui.Label("", wrap: true);
        var canvasBox = new GroupBox(Loc.T("EDITOR_TITLE"), Ui.VBox(6, _canvas, _state, Ui.Dim(Loc.T("EDITOR_HINT"), wrap: true)));
        canvasBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        canvasBox.SizeFlagsVertical = SizeFlags.ExpandFill;

        AddChild(Ui.HBox(10, left, canvasBox).Expand(vertical: true));
        Load(NewDraft(), null);
    }

    public void OnShown() => FillList();

    private LevelDraft NewDraft() => new(9, 7, "");

    private void Load(LevelDraft draft, string? loadedName)
    {
        _syncing = true;
        _canvas.Load(draft);
        _name.Text = draft.Name;
        _width.Value = draft.Width;
        _height.Value = draft.Height;
        _syncing = false;
        _loadedName = loadedName;
        _dirty = false;
        UpdateState();
    }

    private void Resize()
    {
        if (_syncing) return;
        _canvas.Draft.Resize((int)_width.Value, (int)_height.Value);
        _canvas.QueueRedraw();
        MarkDirty();
    }

    private void MarkDirty()
    {
        if (_syncing) return;
        _dirty = true;
        UpdateState();
    }

    private void UpdateState()
    {
        var problem = _canvas.Draft.ProblemCode();
        var p = Palette.Current;
        _state.Text = (problem is null
            ? Loc.T("EDITOR_OK", _canvas.Draft.ToLevel().DisplayId)
            : Loc.T("EDITOR_PROBLEM", Loc.Level(problem))) + (_dirty ? "  ·  " + Loc.T("EDITOR_UNSAVED") : "");
        _state.AddThemeColorOverride("font_color", problem is null ? p.Ok : p.TextDim);
    }

    private void FillList()
    {
        _list.Clear();
        var root = _list.CreateItem();
        var pack = CustomLevels.Load(CustomFile);
        foreach (var level in pack.Levels)
        {
            var item = _list.CreateItem(root);
            item.SetText(0, level.Name);
            item.SetIcon(0, Icons.Get("tile_box"));
            item.SetTooltipText(0, Loc.T("GAME_LEVEL_ID", level.DisplayId));
            item.SetMetadata(0, level.Name);
            if (level.Name == _loadedName) item.Select(0);
        }
        if (pack.Levels.Count == 0)
        {
            var empty = _list.CreateItem(root);
            empty.SetText(0, Loc.T("EDITOR_MY_LEVELS_EMPTY"));
            empty.SetSelectable(0, false);
            empty.SetMetadata(0, "");
        }
        _delete.Disabled = _list.GetSelected()?.GetMetadata(0).AsString() is not { Length: > 0 };
    }

    private void OpenSelected()
    {
        var name = _list.GetSelected()?.GetMetadata(0).AsString();
        if (string.IsNullOrEmpty(name)) return;
        var level = CustomLevels.Load(CustomFile).Levels.FirstOrDefault(l => l.Name == name);
        if (level is null) return;
        ConfirmDiscard(() => Load(LevelDraft.FromLevel(level), level.Name));
    }

    private void ConfirmDiscard(Action then)
    {
        if (!_dirty)
        {
            then();
            return;
        }
        RetroDialog.Ask(_host.DialogLayer, Loc.T("EDITOR_TITLE"), Loc.T("EDITOR_DISCARD_TEXT"), Loc.T("EDITOR_DISCARD"), then, "warn");
    }

    private void Test()
    {
        var problem = _canvas.Draft.ProblemCode();
        if (problem is not null)
        {
            RetroDialog.Message(_host.DialogLayer, Loc.T("EDITOR_TEST"), Loc.T("EDITOR_PROBLEM", Loc.Level(problem)), "warn");
            return;
        }
        TestRequested?.Invoke(_canvas.Draft.ToLevel() with { Name = DisplayName() });
    }

    private string DisplayName() => _name.Text.Trim().Length > 0 ? _name.Text.Trim() : Loc.T("EDITOR_NAME_DEFAULT");

    public void Save()
    {
        var problem = _canvas.Draft.ProblemCode();
        if (problem is not null)
        {
            RetroDialog.Message(_host.DialogLayer, Loc.T("EDITOR_SAVE"), Loc.T("EDITOR_PROBLEM", Loc.Level(problem)), "warn");
            return;
        }
        var level = _canvas.Draft.ToLevel() with { Name = DisplayName() };
        var existing = CustomLevels.Load(CustomFile).Levels.Any(l => string.Equals(l.Name, level.Name, StringComparison.CurrentCultureIgnoreCase));
        if (existing && !string.Equals(level.Name, _loadedName, StringComparison.CurrentCultureIgnoreCase))
        {
            RetroDialog.Ask(_host.DialogLayer, Loc.T("EDITOR_REPLACE_TITLE"), Loc.T("EDITOR_REPLACE_TEXT", level.Name), Loc.T("BTN_REPLACE"), () =>
            {
                // das andere Level mit diesem Namen wird ersetzt, das bisher geladene bleibt
                if (!_host.Services.ReadOnlyMode) CustomLevels.Delete(CustomFile, level.Name);
                DoSave(level, _loadedName);
            });
            return;
        }
        DoSave(level, _loadedName);
    }

    private void DoSave(Level level, string? replaceName)
    {
        var s = _host.Services;
        if (s.ReadOnlyMode) return;
        try
        {
            CustomLevels.Save(CustomFile, level, s.Settings.PlayerName, replaceName);
            _loadedName = level.Name;
            _dirty = false;
            UpdateState();
            FillList();
            _host.SetStatusMessage(Loc.T("EDITOR_SAVED", level.Name), "ok");
            LevelsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            RetroDialog.Message(_host.DialogLayer, Loc.T("EDITOR_SAVE"), Loc.T("EDITOR_SAVE_FAILED", ex.Message), "error");
        }
    }

    private void DeleteSelected()
    {
        var name = _list.GetSelected()?.GetMetadata(0).AsString();
        if (string.IsNullOrEmpty(name)) return;
        RetroDialog.Ask(_host.DialogLayer, Loc.T("EDITOR_DELETE_TITLE"), Loc.T("EDITOR_DELETE_TEXT", name), Loc.T("EDITOR_DELETE"), () =>
        {
            if (_host.Services.ReadOnlyMode) return;
            CustomLevels.Delete(CustomFile, name);
            if (name == _loadedName) Load(NewDraft(), null);
            FillList();
            LevelsChanged?.Invoke();
        });
    }
}
