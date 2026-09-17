using Floppy.Core;
using Floppy.Core.Minigame;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using Godot;

namespace FloppyHub.App.Ui.Views;

/// <summary>
/// Diskette bespielen: game.txt mit Vorschau und denselben Pruefungen wie der Launcher.
/// Ueber die App bespielte Programme kommen automatisch in die Vertrauensliste,
/// und der Motor startet das gerade Geschriebene nicht sofort (WriteGuard).
/// </summary>
public partial class WriteView : ViewBase
{
    public override string Key => "write";

    private static readonly (ReferenceKind Kind, string Icon, string Key)[] Kinds =
    [
        (ReferenceKind.Steam, "kind_steam", "STEAM"),
        (ReferenceKind.PcRun, "kind_pcrun", "PCRUN"),
        (ReferenceKind.Run, "kind_run", "RUN"),
        (ReferenceKind.Hub, "kind_hub", "HUB"),
        (ReferenceKind.Game, "game", "GAME"),
    ];

    private readonly Dictionary<ReferenceKind, CheckBox> _kindButtons = new();
    private ReferenceKind _kind = ReferenceKind.Steam;

    private OptionButton _template = null!;
    private Label _valueLabel = null!;
    private LineEdit _value = null!;
    private Button _browse = null!;
    private TextureRect _recognizedIcon = null!;
    private Label _recognized = null!;
    private Control _recognizedSpacer = null!;
    private TextureRect _cover = null!;
    private Label _argsLabel = null!;
    private LineEdit _args = null!;
    private LineEdit _title = null!;
    private CheckBox _addToLibrary = null!;
    private Control[] _valueRows = [];
    private TextEdit _preview = null!;
    private VBoxContainer _messages = null!;
    private TextureRect _targetIcon = null!;
    private Label _target = null!;
    private Button _write = null!;
    private FileDialog? _dialog;
    private int _coverRequest;
    private bool _discPresent;

    protected override void Build()
    {
        // ---- links: Art + Vorlage ----
        var group = new ButtonGroup();
        var kinds = Ui.VBox(2);
        foreach (var (kind, icon, key) in Kinds)
        {
            var box = new CheckBox { Text = Loc.T("WRITE_KIND_" + key), Icon = Icons.GetSized(icon, 16), ButtonGroup = group, ButtonPressed = kind == _kind };
            box.Toggled += on => { if (on) SetKind(kind); };
            _kindButtons[kind] = box;
            var hint = Ui.Dim(Loc.T("WRITE_KIND_" + key + "_HINT"), wrap: true);
            kinds.AddChild(Ui.VBox(0, box, Ui.Margin(hint, 24, 0, 0, 6)));
        }

        _template = new OptionButton { FitToLongestItem = false, ClipText = true };
        _template.ItemSelected += i => ApplyTemplate((int)i);

        var left = Ui.VBox(10,
            new GroupBox(Loc.T("WRITE_KIND"), kinds),
            new GroupBox(Loc.T("WRITE_TEMPLATE"), Ui.VBox(4, _template, Ui.Dim(Loc.T("WRITE_TEMPLATE_HINT"), wrap: true))));
        left.CustomMinimumSize = new Vector2(270, 0);

        // ---- rechts: Angaben ----
        _valueLabel = Ui.Label("");
        _value = new LineEdit().Expand();
        _value.TextChanged += _ => Refresh();
        _browse = Ui.Button(Loc.T("BTN_BROWSE"), "folder", Browse);

        _recognizedIcon = Icons.Rect("info");
        _recognized = Ui.Dim("", wrap: true).Expand();
        _cover = new TextureRect
        {
            CustomMinimumSize = new Vector2(138, 64),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspect,
            Visible = false,
        };

        _argsLabel = Ui.Label(Loc.T("WRITE_ARGS"));
        _args = new LineEdit { PlaceholderText = Loc.T("WRITE_ARGS_PLACEHOLDER") }.Expand();
        _args.TextChanged += _ => Refresh();
        _title = new LineEdit { PlaceholderText = Loc.T("WRITE_TITLE_PLACEHOLDER") }.Expand();
        _title.TextChanged += _ => Refresh();
        _addToLibrary = new CheckBox { Text = Loc.T("WRITE_ADD_LIBRARY"), ButtonPressed = true };

        var grid = new GridContainer { Columns = 2 };
        var recognizedRow = Ui.HBox(8, _recognizedIcon, _recognized, _cover);
        var spacer1 = new Control();
        var spacer2 = new Control();
        grid.AddChild(_valueLabel);
        grid.AddChild(Ui.HBox(6, _value, _browse).Expand());
        grid.AddChild(spacer1);
        grid.AddChild(recognizedRow.Expand());
        grid.AddChild(_argsLabel);
        grid.AddChild(_args);
        grid.AddChild(Ui.Label(Loc.T("WRITE_TITLE")));
        grid.AddChild(_title);
        grid.AddChild(spacer2);
        grid.AddChild(_addToLibrary);
        _valueRows = [_valueLabel, _value.GetParent<Control>(), _argsLabel, _args];
        _recognizedSpacer = spacer1;   // GridContainer: Zeile nur gemeinsam ein-/ausblenden

        // ---- Vorschau ----
        _preview = new TextEdit
        {
            Editable = false,
            CustomMinimumSize = new Vector2(0, 88),
            ScrollFitContentHeight = true,
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
        };
        _preview.AddThemeFontOverride("font", new SystemFont { FontNames = ["Lucida Console", "Consolas", "Courier New"] });
        _messages = Ui.VBox(3);

        // ---- Ziel + Schreiben ----
        _targetIcon = Icons.Rect("media_empty");
        _target = Ui.Label("", wrap: true).Expand();
        _write = Ui.Button(Loc.T("BTN_WRITE"), "write", Write);
        _write.CustomMinimumSize = new Vector2(170, 34);

        var right = Ui.VBox(10,
            new GroupBox(Loc.T("WRITE_DETAILS"), grid),
            new GroupBox(Loc.T("WRITE_PREVIEW"), Ui.VBox(6, _preview, _messages)),
            Ui.Spacer(false),
            Ui.Panel("RaisedPanel", Ui.HBox(10, _targetIcon, _target, _write))).Expand(vertical: true);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }.Expand(vertical: true);
        scroll.AddChild(Ui.HBox(10, left, right).Expand(vertical: true));
        AddChild(scroll);

        SetKind(_kind);
    }

    public override void OnShown()
    {
        FillTemplates();
        Refresh();
    }

    public override void OnTick()
    {
        if (DiscWatcher.IsReady(Host.Services.DriveRoot) != _discPresent) Refresh();
    }

    /// <summary>Aus der Bibliothek vorausfuellen (Knopf "Auf Diskette…").</summary>
    public void Prefill(LibraryEntry entry)
    {
        var kind = ParseKind(entry.Kind);
        if (kind is null) return;
        _kindButtons[kind.Value].ButtonPressed = true;
        SetKind(kind.Value);
        _value.Text = entry.Value;
        _title.Text = entry.Label;
        _args.Text = "";
        Refresh();
    }

    // ------------------------------------------------------------------

    private void SetKind(ReferenceKind kind)
    {
        _kind = kind;
        var hub = kind == ReferenceKind.Hub;
        foreach (var c in _valueRows) c.Visible = !hub;
        var takesArgs = kind is ReferenceKind.Steam or ReferenceKind.PcRun or ReferenceKind.Run;
        _argsLabel.Visible = takesArgs;
        _args.Visible = takesArgs;
        _addToLibrary.Disabled = kind is ReferenceKind.Hub or ReferenceKind.Game;
        _browse.Visible = kind is ReferenceKind.PcRun or ReferenceKind.Run or ReferenceKind.Game;
        (_valueLabel.Text, _value.PlaceholderText) = kind switch
        {
            ReferenceKind.Steam => (Loc.T("WRITE_VALUE_STEAM"), Loc.T("WRITE_VALUE_STEAM_PLACEHOLDER")),
            ReferenceKind.PcRun => (Loc.T("WRITE_VALUE_PCRUN"), Loc.T("WRITE_VALUE_PCRUN_PLACEHOLDER")),
            ReferenceKind.Run => (Loc.T("WRITE_VALUE_RUN"), Loc.T("WRITE_VALUE_RUN_PLACEHOLDER")),
            ReferenceKind.Game => (Loc.T("WRITE_VALUE_GAME"), Loc.T("WRITE_VALUE_GAME_PLACEHOLDER")),
            _ => ("", ""),
        };
        Refresh();
    }

    private void FillTemplates()
    {
        _template.Clear();
        _template.AddItem(Loc.T("WRITE_TEMPLATE_NONE"));
        foreach (var e in Host.Library)
            _template.AddIconItem(Icons.Get(LibraryView.KindIcon(e.Kind)), e.Label);
        _template.Select(0);
    }

    private void ApplyTemplate(int index)
    {
        if (index <= 0 || index > Host.Library.Count) return;
        Prefill(Host.Library[index - 1]);
    }

    private void Refresh()
    {
        if (_value is null) return;
        var s = Host.Services;
        var root = s.DriveRoot;
        _discPresent = DiscWatcher.IsReady(root);

        var game = _kind == ReferenceKind.Game ? LoadGameSource() : null;
        var preview = _kind == ReferenceKind.Game
            ? ReferenceWriter.Prepare(ReferenceKind.Game, GameFileName, null, TitleOrPack(game), s.Options)
            : ReferenceWriter.Prepare(_kind, _value.Text, _args.Text, _title.Text, s.Options, _discPresent ? root : null);
        var empty = _kind is not (ReferenceKind.Hub or ReferenceKind.Game) && PathRules.StripQuotes(_value.Text).Length == 0;

        _preview.Text = preview.IsValid ? preview.Text.Replace("\r\n", "\n").TrimEnd() : Loc.T("WRITE_PREVIEW_INVALID");
        _messages.ClearChildren();
        if (empty)
        {
            _messages.AddChild(Ui.IconLine("info", Loc.T("WRITE_HINT_EMPTY_" + _kind.ToString().ToUpperInvariant()), "DimLabel"));
        }
        else
        {
            foreach (var m in preview.Messages)
            {
                var icon = m.Level switch { MessageLevel.Error => "error", MessageLevel.Warn => "warn", _ => "info" };
                _messages.AddChild(Ui.IconLine(icon, Loc.Message(m), "DimLabel"));
            }
            foreach (var problem in game?.Pack.Problems ?? [])
                _messages.AddChild(Ui.IconLine(game!.Pack.Levels.Count == 0 ? "error" : "warn", problem, "DimLabel"));
        }

        ShowRecognized(preview, empty, game);
        ShowTarget(root);
        _write.Disabled = !_discPresent || !preview.IsValid || game is { Pack.Levels.Count: 0 };
    }

    private void ShowRecognized(ReferencePreview preview, bool empty, GameSource? game)
    {
        _cover.Visible = false;
        var row = _recognizedIcon.GetParent<Control>();
        var show = !empty && preview.IsValid && _kind != ReferenceKind.Hub;
        row.Visible = show;
        _recognizedSpacer.Visible = show;
        if (!show)
        {
            _recognized.Text = "";
            return;
        }

        switch (_kind)
        {
            case ReferenceKind.Steam:
                var name = Host.Library.FirstOrDefault(e => e.Kind.Equals("steam", StringComparison.OrdinalIgnoreCase) && e.Value.Trim() == preview.Value)?.Label
                           ?? SteamApps.TryGetKnownName(preview.Value);
                SetRecognized("ok", name is null ? Loc.T("WRITE_REC_STEAM_ID", preview.Value) : Loc.T("WRITE_REC_STEAM", name, preview.Value));
                LoadCover(preview.Value);
                break;

            case ReferenceKind.PcRun:
                var fi = new FileInfo(preview.Value);
                var trust = fi.Exists ? Host.Services.Trust.Check(preview.Value, preview.Arguments) : TrustState.Unknown;
                SetRecognized(fi.Exists ? "ok" : "warn", fi.Exists
                    ? Loc.T("WRITE_REC_FILE", Ui.Bytes(fi.Length)) + (trust == TrustState.Trusted ? "  " + Loc.T("WRITE_REC_TRUSTED") : "")
                    : Loc.T("WRITE_REC_MISSING"));
                break;

            case ReferenceKind.Run:
                var onDisc = _discPresent && File.Exists(System.IO.Path.Combine(Host.Services.DriveRoot, preview.Value));
                SetRecognized(onDisc ? "ok" : "warn", onDisc ? Loc.T("WRITE_REC_ON_DISC") : Loc.T("WRITE_REC_NOT_ON_DISC"));
                break;

            case ReferenceKind.Game when game is not null:
                SetRecognized(game.Pack.Levels.Count > 0 ? "ok" : "error", game.Pack.Levels.Count > 0
                    ? Loc.T("WRITE_REC_GAME", game.Pack.Title, game.Pack.Levels.Count)
                    : Loc.T("WRITE_REC_GAME_INVALID"));
                break;
        }
    }

    private void SetRecognized(string icon, string text)
    {
        _recognizedIcon.Texture = Icons.Get(icon);
        _recognized.Text = text;
    }

    private async void LoadCover(string appId)
    {
        var request = ++_coverRequest;
        var tex = await Host.Covers.GetAsync(appId, Host.Services.Settings.LoadCovers);
        if (request != _coverRequest || !IsInstanceValid(this) || _kind != ReferenceKind.Steam) return;
        _cover.Texture = tex;
        _cover.Visible = tex is not null;
    }

    private void ShowTarget(string root)
    {
        var shown = root.Length <= 3 ? root : Loc.T("DISC_TESTFOLDER");
        if (!_discPresent)
        {
            _targetIcon.Texture = Icons.Get("media_empty");
            _target.Text = Loc.T("WRITE_TARGET_NONE", shown);
            return;
        }
        _targetIcon.Texture = Icons.Get("media_floppy");
        var existing = File.Exists(System.IO.Path.Combine(root, "game.txt")) ||
                       (_kind == ReferenceKind.Game && File.Exists(System.IO.Path.Combine(root, GameFileName)));
        var free = root.Length <= 3 ? Ui.Bytes(DriveSnapshot.Read(root).FreeBytes) : null;
        _target.Text = Loc.T("WRITE_TARGET", shown) +
                       (free is null ? "" : "  ·  " + Loc.T("WRITE_TARGET_FREE", free)) +
                       (existing ? "\n" + Loc.T("WRITE_TARGET_REPLACE") : "");
    }

    // ------------------------------------------------------------------
    // Durchsuchen
    // ------------------------------------------------------------------

    private void Browse()
    {
        var s = Host.Services;
        _dialog?.QueueFree();
        _dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Title = Loc.T(_kind switch
            {
                ReferenceKind.Run => "WRITE_BROWSE_RUN",
                ReferenceKind.Game => "WRITE_BROWSE_GAME",
                _ => "WRITE_BROWSE_PCRUN",
            }),
            Filters = _kind == ReferenceKind.Game
                ? [$"*.txt ; {Loc.T("WRITE_FILTER_LEVELS")}"]
                : [$"{string.Join(", ", s.Options.ExecutableExtensions.Select(e => "*" + e))} ; {Loc.T("WRITE_FILTER_PROGRAMS")}"],
        };
        if (_kind == ReferenceKind.Run && DiscWatcher.IsReady(s.DriveRoot)) _dialog.CurrentDir = s.DriveRoot;

        _dialog.FileSelected += path =>
        {
            path = path.Replace('/', '\\');
            if (_kind == ReferenceKind.Run)
            {
                if (!PathRules.IsUnder(path, s.DriveRoot))
                {
                    RetroDialog.Message(Host.DialogLayer, Loc.T("WRITE_BROWSE_RUN"), Loc.T("WRITE_NOT_ON_DISC", s.DriveRoot), "warn");
                    return;
                }
                path = System.IO.Path.GetRelativePath(s.DriveRoot, path);
            }
            _value.Text = path;
            if (_title.Text.Length == 0 && _kind != ReferenceKind.Game) _title.Text = System.IO.Path.GetFileNameWithoutExtension(path);
            Refresh();
        };
        AddChild(_dialog);
        _dialog.PopupCentered();
    }

    // ------------------------------------------------------------------
    // Schreiben
    // ------------------------------------------------------------------

    private void Write()
    {
        var root = Host.Services.DriveRoot;
        if (File.Exists(System.IO.Path.Combine(root, "game.txt")) ||
            (_kind == ReferenceKind.Game && File.Exists(System.IO.Path.Combine(root, GameFileName))))
        {
            RetroDialog.Ask(Host.DialogLayer, Loc.T("WRITE_REPLACE_TITLE"), Loc.T("WRITE_REPLACE_TEXT"), Loc.T("BTN_REPLACE"),
                () => DoWrite(force: true));
            return;
        }
        DoWrite(force: false);
    }

    private void DoWrite(bool force)
    {
        var s = Host.Services;
        var root = s.DriveRoot;
        if (s.ReadOnlyMode) return;

        var game = _kind == ReferenceKind.Game ? LoadGameSource() : null;
        if (game is { Pack.Levels.Count: 0 }) return;

        // 1. Motor vorwarnen, 2. schreiben, 3. Fingerabdruck hinterlegen
        WriteGuard.Begin(s.Paths.UserData);
        WriteResult result;
        if (game is not null)
        {
            try
            {
                File.WriteAllText(System.IO.Path.Combine(root, GameFileName), game.Text, new System.Text.UTF8Encoding(true));
                result = ReferenceWriter.Write(root, ReferenceKind.Game, GameFileName, null, TitleOrPack(game), s.Options, force: force);
            }
            catch (Exception ex)
            {
                result = new WriteResult(null, [new PlanMessage(MessageLevel.Error, ex.Message)]);
            }
        }
        else
        {
            result = ReferenceWriter.Write(root, _kind, _value.Text, _args.Text, _title.Text, s.Options, force: force);
        }
        if (!result.Success)
        {
            WriteGuard.Cancel(s.Paths.UserData);
            RetroDialog.Message(Host.DialogLayer, Loc.T("WRITE_FAILED_TITLE"),
                string.Join("\n", result.Messages.Where(m => m.Level != MessageLevel.Ok).Select(Loc.Message)), "error");
            return;
        }
        WriteGuard.Note(s.Paths.UserData, DiskSignature.Compute(root));

        var preview = game is not null
            ? ReferenceWriter.Prepare(ReferenceKind.Game, GameFileName, null, TitleOrPack(game), s.Options)
            : ReferenceWriter.Prepare(_kind, _value.Text, _args.Text, _title.Text, s.Options, root);
        var label = game is not null ? TitleOrPack(game) : Label(preview);
        s.Log.Write(LogLevel.Ok, $"App: Diskette bespielt ({_kind}): {result.Path}");

        var trusted = TrustWritten(preview, label);
        var added = AddToLibrary(preview, label);

        var details = new List<string> { Loc.T("WRITE_DONE_TEXT", label) };
        if (trusted) details.Add(Loc.T("WRITE_DONE_TRUSTED"));
        if (added) details.Add(Loc.T("WRITE_DONE_LIBRARY"));
        RetroDialog.Message(Host.DialogLayer, Loc.T("WRITE_DONE_TITLE"), string.Join("\n\n", details), "ok");
        Host.SetStatusMessage(Loc.T("WRITE_DONE_STATUS", label), "write");
        Refresh();
    }

    private string Label(ReferencePreview p)
    {
        if (_title.Text.Trim().Length > 0) return _title.Text.Trim();
        return _kind switch
        {
            ReferenceKind.Steam => SteamApps.TryGetKnownName(p.Value) ?? Loc.T("KIND_STEAM_ID", p.Value),
            ReferenceKind.Hub => Loc.T("KIND_HUB"),
            _ => System.IO.Path.GetFileNameWithoutExtension(p.Value),
        };
    }

    /// <summary>Ueber die App eingerichtet = vertrauenswuerdig (Entscheidung von Mael).</summary>
    private bool TrustWritten(ReferencePreview p, string label)
    {
        var s = Host.Services;
        string? target = _kind switch
        {
            ReferenceKind.PcRun => s.Planner.ResolvePcPath(p.Value, []),      // gesperrte Ordner bekommen KEINE Freigabe
            ReferenceKind.Run => s.Planner.ResolveOnDrive(s.DriveRoot, p.Value, []),
            _ => null,
        };
        if (target is null) return false;
        try
        {
            s.Trust.Add(target, p.Arguments, label, TrustStore.SourceWritten);
            s.Log.Write(LogLevel.Info, $"App: Freigabe beim Bespielen: {target} {p.Arguments}".TrimEnd());
            return true;
        }
        catch (Exception ex)
        {
            s.Log.Write(LogLevel.Warn, $"App: Freigabe nicht gespeichert: {ex.Message}");
            return false;
        }
    }

    private bool AddToLibrary(ReferencePreview p, string label)
    {
        if (!_addToLibrary.ButtonPressed || _kind is ReferenceKind.Hub or ReferenceKind.Game) return false;
        var s = Host.Services;
        try
        {
            var file = s.Paths.LibraryFile;
            var entries = File.Exists(file) ? LibraryCsv.Read(file) : [];
            var kind = _kind switch { ReferenceKind.Steam => "steam", ReferenceKind.PcRun => "pcrun", _ => "run" };
            LibraryCsv.Write(file, LibraryCsv.Upsert(entries,
                new LibraryEntry(label, kind, p.Value, DateTime.Today.ToString("yyyy-MM-dd"), "via Floppy Hub App")));
            Host.ReloadLibrary();
            return true;
        }
        catch (Exception ex)
        {
            s.Log.Write(LogLevel.Warn, $"App: Bibliothek nicht gespeichert: {ex.Message}");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Minispiel-Diskette
    // ------------------------------------------------------------------

    /// <summary>So heisst das Levelpaket auf der Diskette.</summary>
    public const string GameFileName = "levels.txt";

    private sealed record GameSource(string Text, LevelPack Pack);

    /// <summary>"Minispiel-Diskette" vorauswaehlen (Knopf im Minispiel).</summary>
    public void PrefillGame(string? packFile = null)
    {
        _kindButtons[ReferenceKind.Game].ButtonPressed = true;
        SetKind(ReferenceKind.Game);
        _value.Text = packFile ?? "";
        _title.Text = "";
        Refresh();
    }

    /// <summary>Leer = eingebautes Paket, sonst die gewaehlte Datei.</summary>
    private GameSource LoadGameSource()
    {
        var path = PathRules.StripQuotes(_value.Text);
        try
        {
            if (path.Length == 0)
            {
                var text = GameView.BuiltInPackText();
                return new GameSource(text, LevelPack.Parse(text));
            }
            var info = new FileInfo(path);
            if (!info.Exists) return new GameSource("", LevelPack.Parse("") with { Problems = [Loc.T("WRITE_REC_MISSING")] });
            if (info.Length > LevelPack.MaxFileBytes) return new GameSource("", LevelPack.Load(path));
            var fileText = File.ReadAllText(path);
            return new GameSource(fileText, LevelPack.Parse(fileText));
        }
        catch (Exception ex)
        {
            return new GameSource("", LevelPack.Parse("") with { Problems = [ex.Message] });
        }
    }

    private string TitleOrPack(GameSource? game) =>
        _title.Text.Trim().Length > 0 ? _title.Text.Trim() : game?.Pack.Title ?? "";

    private static ReferenceKind? ParseKind(string kind) => kind.ToLowerInvariant() switch
    {
        "steam" => ReferenceKind.Steam,
        "pcrun" => ReferenceKind.PcRun,
        "run" => ReferenceKind.Run,
        "hub" => ReferenceKind.Hub,
        _ => null,
    };
}
