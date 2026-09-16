using Floppy.Core;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using FloppyHub.App.Fun;
using FloppyHub.App.Services;
using FloppyHub.App.Ui.Views;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>
/// Hauptfenster: Menueleiste, Werkzeugleiste, Ansicht, Statusleiste.
/// Aufbau wie klassische Windows-Werkzeuge (WinRAR, Audacity, VLC).
/// </summary>
public partial class MainWindow : PanelContainer, IAppHost
{
    private readonly Dictionary<string, ViewBase> _views = new();
    private readonly Dictionary<string, ToolButton> _tools = new();
    private readonly ButtonGroup _toolGroup = new();
    private List<LibraryEntry> _library = [];
    private Control _viewHost = null!;
    private StatusBar _status = null!;
    private Control _dialogs = null!;
    private PopupMenu _viewMenu = null!;
    private string _current = "disc";
    private string? _discSignature;
    private bool? _discPresent;
    private SceneTreeTimer? _messageTimer;

    private Action<string> _applyTheme = _ => { };
    private Action<float> _applyScale = _ => { };

    public AppServices Services { get; private set; } = null!;
    public CoverCache Covers { get; private set; } = null!;
    public Control DialogLayer => _dialogs;
    public IReadOnlyList<LibraryEntry> Library => _library;
    public string CurrentView => _current;

    public void Setup(AppServices services, CoverCache covers, string startView, Action<string> applyTheme, Action<float> applyScale)
    {
        Services = services;
        Covers = covers;
        _applyTheme = applyTheme;
        _applyScale = applyScale;
        ThemeTypeVariation = "WindowPanel";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        Build();
        ReloadLibrary();
        ShowView(startView);
        UpdateStatus(force: true);

        var tick = new Timer { WaitTime = Math.Max(2, services.Options.PollSeconds), Autostart = true };
        tick.Timeout += OnTick;
        AddChild(tick);

        // Easter Eggs: Konami-Code + manchmal "Wusstest du?"
        var konami = new KonamiListener();
        konami.Activated += () =>
        {
            _dialogs.AddChild(new CrtOverlay(10));
            SetStatusMessage(Loc.T("EGG_KONAMI"), "game");
        };
        AddChild(konami);
        if (EasterEggs.StartupFact(new Random()) is { } fact) SetStatusMessage(fact, "info");
    }

    /// <summary>Nur fuer Bildschirmfotos der Easter Eggs.</summary>
    public void TriggerCrt() => _dialogs.AddChild(new CrtOverlay(10));

    // ------------------------------------------------------------------
    // Aufbau
    // ------------------------------------------------------------------

    private void Build()
    {
        var menu = BuildMenu();
        var toolbar = BuildToolbar();

        _viewHost = new MarginContainer().Expand(vertical: true);
        _viewHost.AddThemeConstantOverride("margin_left", 8);
        _viewHost.AddThemeConstantOverride("margin_right", 8);
        _viewHost.AddThemeConstantOverride("margin_top", 8);
        _viewHost.AddThemeConstantOverride("margin_bottom", 6);

        _status = new StatusBar();
        _status.AddCell("motor", 150);
        _status.AddCell("disc", 290);
        _status.AddCell("message", 0, expand: true);
        _status.AddCell("library", 120);

        var layout = Ui.VBox(0, Ui.Panel("WindowPanel", menu), toolbar, _viewHost, _status);
        var stack = new Control().Expand(vertical: true);
        layout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        stack.AddChild(layout);

        _dialogs = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _dialogs.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        stack.AddChild(_dialogs);
        AddChild(stack);

        void AddView(ViewBase v)
        {
            v.Init(this);
            v.Visible = false;
            _views[v.Key] = v;
            _viewHost.AddChild(v);
        }

        AddView(new DiscView());
        AddView(new LibraryView());
        AddView(new WriteView());
        AddView(new DrivesView());
        AddView(new ComingSoonView("chat", "chat", 6));
        AddView(new ComingSoonView("game", "game", 5));
        AddView(new LogView());
        AddView(new SettingsView());
    }

    private PanelContainer BuildToolbar()
    {
        var row = Ui.HBox(1);

        void Tool(string key, string icon)
        {
            var b = new ToolButton(Loc.T("VIEW_" + key.ToUpperInvariant()), icon, Loc.T("TIP_" + key.ToUpperInvariant()))
            {
                ToggleMode = true,
                ButtonGroup = _toolGroup,
            };
            b.Pressed += () => ShowView(key);
            _tools[key] = b;
            row.AddChild(b);
        }

        Tool("disc", "floppy");
        Tool("library", "library");
        Tool("write", "write");
        Tool("drives", "drives");
        row.AddChild(new VSeparator());
        Tool("chat", "chat");
        Tool("game", "game");
        row.AddChild(new VSeparator());
        Tool("log", "log");
        Tool("settings", "settings");
        row.AddChild(Ui.Spacer());

        var brandName = Ui.Label("FLOPPY HUB", "BoldLabel");
        var brand = Ui.VBox(0, brandName, Ui.Dim(EasterEggs.Tagline()));
        brand.Alignment = BoxContainer.AlignmentMode.Center;
        brand.MouseFilter = MouseFilterEnum.Stop;
        brand.GuiInput += e =>
        {
            if (!EasterEggs.Enabled || e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            var (name, comment) = EasterEggs.NextEdition();
            brandName.Text = name;
            if (comment is not null) SetStatusMessage(comment, "warn");
        };
        row.AddChild(Ui.HBox(8, Icons.Rect("app"), brand).With(h => h.Alignment = BoxContainer.AlignmentMode.Center));
        row.AddChild(Ui.Margin(new Control(), 0, 0, 6, 0));

        return Ui.Panel("ToolbarPanel", row);
    }

    private MenuBar BuildMenu()
    {
        var bar = new MenuBar { Flat = true, PreferGlobalMenu = false };

        PopupMenu Menu(string titleKey, params (long Id, string? Key, string? Icon, Key Accel)[] items)
        {
            var m = new PopupMenu { Name = Loc.T(titleKey), AutoTranslateMode = AutoTranslateModeEnum.Disabled };
            foreach (var (id, key, icon, accel) in items)
            {
                if (key is null) { m.AddSeparator(); continue; }
                if (icon is null) m.AddItem(Loc.T(key), (int)id, accel);
                else m.AddIconItem(Icons.Get(icon), Loc.T(key), (int)id, accel);
            }
            m.IdPressed += OnMenu;
            bar.AddChild(m);
            return m;
        }

        Menu("MENU_FILE",
            (101, "MENU_REFRESH", "refresh", Key.F5),
            (0, null, null, Key.None),
            (102, "MENU_OPEN_HOME", "folder", Key.None),
            (103, "MENU_OPEN_DATA", "folder", Key.None),
            (0, null, null, Key.None),
            (199, "MENU_QUIT", null, Key.None));

        Menu("MENU_DISC",
            (201, "MENU_START", "start", Key.None),
            (202, "MENU_WRITE", "write", Key.None),
            (0, null, null, Key.None),
            (203, "MENU_EXAMPLES", "folder", Key.None));

        _viewMenu = Menu("MENU_VIEW");
        var views = new[] { "disc", "library", "write", "drives", "chat", "game", "log", "settings" };
        for (var i = 0; i < views.Length; i++)
            _viewMenu.AddRadioCheckItem(Loc.T("VIEW_" + views[i].ToUpperInvariant()), 300 + i);
        _viewMenu.AddSeparator();
        _viewMenu.AddRadioCheckItem(Loc.T("THEME_SYSTEM"), 310);
        _viewMenu.AddRadioCheckItem(Loc.T("THEME_LIGHT"), 311);
        _viewMenu.AddRadioCheckItem(Loc.T("THEME_DARK"), 312);
        var themeId = Services.Settings.Theme switch { AppSettings.ThemeLight => 311, AppSettings.ThemeDark => 312, _ => 310 };
        _viewMenu.SetItemChecked(_viewMenu.GetItemIndex(themeId), true);

        Menu("MENU_EXTRAS",
            (401, "MENU_TRUST", "trust", Key.None),
            (402, "MENU_LOG", "log", Key.None),
            (403, "MENU_CLEAR_LOG", "remove", Key.None));

        Menu("MENU_HELP",
            (501, "MENU_ABOUT", "info", Key.F1));

        return bar;
    }

    private void OnMenu(long id)
    {
        switch (id)
        {
            case 101: RefreshAll(); break;
            case 102: OS.ShellOpen(Services.Paths.Home); break;
            case 103:
                FloppyPaths.EnsureDirectory(Services.Paths.UserData);
                OS.ShellOpen(Services.Paths.UserData);
                break;
            case 199: GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest); break;
            case 201: LaunchDisc(); break;
            case 202: ShowView("write"); break;
            case 203:
                var examples = System.IO.Path.Combine(Services.Paths.Home, "beispiele");
                OS.ShellOpen(Directory.Exists(examples) ? examples : Services.Paths.Home);
                break;
            case >= 300 and < 308:
                ShowView(new[] { "disc", "library", "write", "drives", "chat", "game", "log", "settings" }[id - 300]);
                break;
            case 310: _applyTheme(AppSettings.ThemeSystem); break;
            case 311: _applyTheme(AppSettings.ThemeLight); break;
            case 312: _applyTheme(AppSettings.ThemeDark); break;
            case 401: ShowView("settings"); break;
            case 402: ShowView("log"); break;
            case 403:
                ShowView("log");
                RetroDialog.Ask(_dialogs, Loc.T("LOG_CLEAR_TITLE"), Loc.T("LOG_CLEAR_TEXT"), Loc.T("BTN_CLEAR_LOG"), () =>
                {
                    if (Services.ReadOnlyMode) return;
                    var r = LogFile.Clear(Services.Log.FilePath);
                    SetStatusMessage(r.Message, r.Cleared ? "ok" : "warn");
                    _views["log"].OnShown();
                });
                break;
            case 501: ShowAbout(); break;
        }
    }

    // ------------------------------------------------------------------
    // IAppHost
    // ------------------------------------------------------------------

    public void ShowView(string key)
    {
        if (!_views.ContainsKey(key)) key = "disc";
        _current = key;
        foreach (var (k, v) in _views) v.Visible = k == key;
        if (_tools.TryGetValue(key, out var tool)) tool.SetPressedNoSignal(true);

        var index = Array.IndexOf(new[] { "disc", "library", "write", "drives", "chat", "game", "log", "settings" }, key);
        for (var i = 0; i < 8; i++) _viewMenu.SetItemChecked(_viewMenu.GetItemIndex(300 + i), i == index);

        _views[key].OnShown();
    }

    public void ReloadLibrary()
    {
        try
        {
            var file = Services.Paths.LibraryFile;
            _library = File.Exists(file) ? LibraryCsv.Read(file).ToList() : [];
        }
        catch (Exception ex)
        {
            _library = [];
            GD.PushWarning($"library.csv nicht lesbar: {ex.Message}");
        }
        _status?.Set("library", Loc.T("STATUS_LIBRARY", _library.Count), "library_small");
    }

    public void SetStatusMessage(string text, string icon = "info")
    {
        _status.Set("message", text, icon);
        _messageTimer = GetTree().CreateTimer(6);
        var mine = _messageTimer;
        _messageTimer.Timeout += () =>
        {
            if (mine == _messageTimer && IsInstanceValid(_status)) _status.Set("message", "");
        };
    }

    public void LaunchDisc()
    {
        var st = DiscState.Read(Services);
        var d = st.Decision;
        var name = st.DisplayName(_library);
        switch (d?.Action)
        {
            case GateAction.StartSteam when st.Plan?.SteamId is { } id:
                if (!Services.ReadOnlyMode) Starter.OpenSteam(id);
                Services.Log.Write(LogLevel.Ok, $"App: starte Steam-Spiel {id}.");
                SetStatusMessage(Loc.T("STATUS_STEAM_STARTED", name), "kind_steam");
                break;
            case GateAction.OpenHub:
                ShowView("disc");
                SetStatusMessage(Loc.T("STATUS_HUB_ALREADY"), "kind_hub");
                break;
            case GateAction.Start when d.Target is not null:
                if (!Services.ReadOnlyMode) Starter.StartProgram(d.Target, st.Plan?.Arguments);
                Services.Log.Write(LogLevel.Ok, $"App: starte freigegebenes Programm {d.Target} {st.Plan?.Arguments}".TrimEnd());
                SetStatusMessage(Loc.T("STATUS_PROGRAM_STARTED", name), "start");
                break;
            case GateAction.Ask:
                OpenConfirm(p => p.SetupForDisc(Services, st, _library));
                break;
            default:
                var why = st.Present
                    ? string.Join("\n", st.Result?.Messages.Select(m => m.Text) ?? []) is { Length: > 0 } msg ? msg : Loc.T("DISC_NOTHING_TEXT")
                    : Loc.T("DISC_NONE_TEXT", st.Root);
                RetroDialog.Message(_dialogs, Loc.T("DISC_NOTHING"), why, st.Present ? "warn" : "info");
                break;
        }
        RefreshDiscSoon();
    }

    public void LaunchProgram(string path, string? arguments, string label)
    {
        if (Services.Trust.Check(path, arguments) == TrustState.Trusted)
        {
            if (!Services.ReadOnlyMode) Starter.StartProgram(path, arguments);
            Services.Log.Write(LogLevel.Ok, $"App: starte freigegebenes Programm {path} {arguments}".TrimEnd());
            SetStatusMessage(Loc.T("STATUS_PROGRAM_STARTED", label), "start");
            return;
        }
        OpenConfirm(p => p.SetupForProgram(Services, path, arguments, label));
    }

    /// <summary>Rueckfrage fuer die Diskette (vom Motor ueber die Pipe angefordert).</summary>
    public void ConfirmDisc()
    {
        var st = DiscState.Read(Services);
        if (st.Decision?.Action == GateAction.Ask) OpenConfirm(p => p.SetupForDisc(Services, st, _library));
        else ShowView("disc");
    }

    public void OpenConfirm(Action<ConfirmPanel> setup)
    {
        foreach (var child in _dialogs.GetChildren())
            if (child is RetroDialog open && open.Name == "Confirm") return;   // nur eine Rueckfrage gleichzeitig

        var dialog = new RetroDialog(Loc.T("CONFIRM_WINDOW_TITLE"), "warn", 560) { Name = "Confirm" };
        var panel = new ConfirmPanel();
        dialog.Body.AddChild(panel);
        setup(panel);
        panel.Finished += started =>
        {
            dialog.Close();
            SetStatusMessage(started ? Loc.T("STATUS_CONFIRM_STARTED") : Loc.T("STATUS_CONFIRM_CANCELLED"), started ? "start" : "info");
            RefreshDiscSoon();
        };
        dialog.Cancelled += () => panel.Cancel(Loc.T("CONFIRM_CANCELLED"));
        dialog.Open(_dialogs);
    }

    public void OpenWrite(LibraryEntry entry)
    {
        ShowView("write");
        ((WriteView)_views["write"]).Prefill(entry);
    }

    public void ApplyTheme(string theme) => _applyTheme(theme);
    public void ApplyScale(float scale) => _applyScale(scale);

    // ------------------------------------------------------------------

    private void RefreshAll()
    {
        ReloadLibrary();
        UpdateStatus(force: true);
        _views[_current].OnShown();
        SetStatusMessage(Loc.T("STATUS_REFRESHED"), "refresh");
    }

    private void RefreshDiscSoon() => GetTree().CreateTimer(0.4).Timeout += () =>
    {
        if (!IsInstanceValid(this)) return;
        UpdateStatus(force: true);
        if (_current == "disc") _views["disc"].OnShown();
    };

    private void OnTick()
    {
        UpdateStatus(force: false);
        if (_views.TryGetValue(_current, out var v)) v.OnTick();
    }

    private void UpdateStatus(bool force)
    {
        var running = MotorProbe.IsRunning();
        _status.Set("motor", running ? Loc.T("MOTOR_RUNNING_SHORT") : Loc.T("MOTOR_STOPPED_SHORT"), running ? "led_on" : "led_off",
            running ? Loc.T("MOTOR_RUNNING") : Loc.T("MOTOR_STOPPED"));

        var root = Services.DriveRoot;
        var present = DiscWatcher.IsReady(root);
        var signature = present ? DiskSignature.Compute(root) : DiskSignature.Empty;
        if (!force && present == _discPresent && signature == _discSignature) return;
        _discPresent = present;
        _discSignature = signature;

        if (!present)
        {
            _status.Set("disc", Loc.T("STATUS_NO_DISC", root.Length <= 3 ? root.TrimEnd('\\') : "Test"), "led_off");
            return;
        }
        var st = DiscState.Read(Services);
        var icon = st.Decision?.Action switch
        {
            GateAction.StartSteam or GateAction.Start or GateAction.OpenHub => "led_on",
            GateAction.Ask => "led_warn",
            _ => "floppy_small",
        };
        _status.Set("disc", $"{(root.Length <= 3 ? root.TrimEnd('\\') : "Test")}  {st.DisplayName(_library)}", icon);
    }

    public void ShowAbout()
    {
        var d = new RetroDialog(Loc.T("ABOUT_TITLE"), "info", 440);
        var logo = Icons.Rect("app", 3f);
        var clicks = 0;
        var egg = Ui.Dim(" ");
        logo.MouseFilter = MouseFilterEnum.Stop;
        logo.GuiInput += e =>
        {
            if (EasterEggs.Enabled && e is InputEventMouseButton { Pressed: true } && ++clicks == 3) egg.Text = Loc.T("ABOUT_EGG");
        };
        var version = (string)ProjectSettings.GetSetting("application/config/version");
        d.Body.AddChild(Ui.HBox(16, logo, Ui.VBox(4,
            Ui.Label("Floppy Hub", "TitleLabel"),
            Ui.Label(Loc.T("ABOUT_VERSION", version)),
            Ui.Dim(Loc.T("ABOUT_TEXT"), wrap: true),
            Ui.Dim(Loc.T("ABOUT_CREDITS"), wrap: true),
            egg).Expand()));
        d.AddButton(Loc.T("BTN_OK"), () => { });
        d.Open(_dialogs);
    }
}
