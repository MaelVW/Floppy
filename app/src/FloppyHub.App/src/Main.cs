using Floppy.Core;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using FloppyHub.App.Fun;
using FloppyHub.App.Services;
using FloppyHub.App.Skin;
using FloppyHub.App.Ui;
using Godot;

namespace FloppyHub.App;

/// <summary>
/// Einstieg der App. Zwei Betriebsarten:
///  * normal       - Hauptfenster (auch bei Hub-Diskette, --hub)
///  * --confirm    - kleines Rueckfrage-Fenster, das sich danach wieder schliesst
/// Laeuft die App schon, gibt ein zweiter Start nur Bescheid (Pipe) und beendet sich.
/// </summary>
public partial class Main : Control
{
    private AppServices _s = null!;
    private CoverCache _covers = null!;
    private System.Threading.Mutex? _instance;
    private readonly System.Threading.CancellationTokenSource _pipe = new();
    private MainWindow? _main;
    private bool _compact;
    private bool _quitting;
    private ChatDemo? _chatDemo;

    public override void _Ready()
    {
        var args = AppArgs.Parse(OS.GetCmdlineUserArgs());

        if (args.ForgeIcons)
        {
            ForgeIcons();
            return;
        }

        Starter.SuppressDriveErrorDialogs();
        _s = AppServices.Create(args);
        _covers = new CoverCache(_s.Paths.CoverCacheDir);

        if (!_s.ReadOnlyMode && !args.AllowMultiple && !TakeSingleInstance(args)) return;

        // Allererster Start: Sprache wie Windows (Deutsch, sonst Englisch) - im Willkommensdialog aenderbar
        if (!_s.Settings.FirstRunDone && !File.Exists(_s.Paths.AppSettingsFile))
            _s.Settings.Language = OS.GetLocaleLanguage() == "de" ? "de" : "en";
        if (args.Language is not null) _s.Settings.Language = args.Language;   // Test: nur fuer diesen Start
        Loc.Load(_s.Settings.Language);
        EasterEggs.Enabled = _s.Options.EasterEggs;
        if (args.EggDate is { } eggDate) EasterEggs.Today = eggDate;
        GetTree().AutoAcceptQuit = false;
        UiScale.Apply(GetWindow(), args.Scale ?? _s.Settings.Scale);
        _compact = args.Confirm;

        SetupWindow();
        if (args.Theme is not null) _s.Settings.Theme = args.Theme;
        ApplyTheme(_s.Settings.Theme, save: false);

        if (_s.OptionsProblem is not null) _s.Log.Write(LogLevel.Warn, "App: " + _s.OptionsProblem);

        if (!_compact && (!_s.Settings.FirstRunDone || args.FirstRun)) Callable.From(ShowFirstRun).CallDeferred();
        else if (!_compact && !_s.ReadOnlyMode) CheckForUpdate(force: true);
        if (!_compact && !_s.ReadOnlyMode)
        {
            _ = Task.Run(Floppy.Core.Updates.UpdateInstaller.CleanUp);   // Setup vom letzten Update aufraeumen
            ScheduleUpdateChecks();
        }
        if (args.Screenshot is not null) TakeScreenshotLater(args.Screenshot, args.View);
    }

    // ------------------------------------------------------------------
    // Einzelinstanz + Befehle vom Motor
    // ------------------------------------------------------------------

    private bool TakeSingleInstance(AppArgs args)
    {
        _instance = new System.Threading.Mutex(true, HubPipe.AppMutexName, out var isNew);
        if (!isNew)
        {
            // App laeuft schon: Bescheid geben und sofort wieder beenden.
            HubPipe.TrySend(args.Confirm ? HubPipe.Confirm : args.Game ? HubPipe.Game : args.Hub ? HubPipe.Hub : HubPipe.Show, 1000);
            _instance.Dispose();
            _instance = null;
            GetTree().Quit();
            return false;
        }

        _ = HubPipe.ServeAsync(cmd => Callable.From(() => OnPipeCommand(cmd)).CallDeferred(), _pipe.Token);
        return true;
    }

    private void OnPipeCommand(string command)
    {
        if (_quitting) return;
        BringToFront(command == HubPipe.Confirm);
        if (command != HubPipe.Confirm && !_compact && !_s.ReadOnlyMode) CheckForUpdate();   // Fenster wird geoeffnet: gleich nach Neuem schauen
        switch (command)
        {
            case HubPipe.Hub:
                _main?.ShowView("disc");
                break;
            case HubPipe.Game:
                _main?.OpenGameFromDisc();
                break;
            case HubPipe.Confirm:
                if (_main is not null) _main.ConfirmDisc();
                else if (_compact) BuildCompact();   // Fenster zeigt die aktuelle Diskette neu
                break;
        }
    }

    private void BringToFront(bool keepOnTop)
    {
        var w = GetWindow();
        if (w.Mode == Window.ModeEnum.Minimized) w.Mode = Window.ModeEnum.Windowed;
        w.GrabFocus();
        DisplayServer.WindowMoveToForeground();
        w.AlwaysOnTop = true;
        if (!keepOnTop) GetTree().CreateTimer(0.5).Timeout += () => w.AlwaysOnTop = false;
    }

    // ------------------------------------------------------------------
    // Fenster, Farbschema, Aufbau
    // ------------------------------------------------------------------

    private void SetupWindow()
    {
        var w = GetWindow();
        var f = UiScale.Factor;
        w.Title = _compact ? Loc.T("CONFIRM_WINDOW_TITLE") : "Floppy Hub";
        var logical = _compact ? new Vector2(600, 440) : new Vector2(1000, 660);
        var min = _compact ? new Vector2(520, 380) : new Vector2(800, 540);
        w.MinSize = (Vector2I)(min * f);
        w.Size = (Vector2I)(logical * f);
        w.MoveToCenter();

        var icon = IconForge.Draw("app", Palette.Classic);
        icon.Resize(64, 64, Image.Interpolation.Nearest);
        DisplayServer.SetIcon(icon);

        if (_compact) w.AlwaysOnTop = true;
    }

    private void ApplyTheme(string theme, bool save = true)
    {
        _s.Settings.Theme = theme;
        if (save) _s.SaveSettings();

        var dark = theme == AppSettings.ThemeDark ||
                   (theme == AppSettings.ThemeSystem && DisplayServer.IsDarkModeSupported() && DisplayServer.IsDarkMode());
        var palette = dark ? Palette.Midnight : Palette.Classic;
        Icons.ClearCache();
        GetWindow().Theme = SkinBuilder.Build(palette);
        RenderingServer.SetDefaultClearColor(palette.Window);
        Rebuild();
    }

    private void ApplyLanguage(string language)
    {
        if (!Loc.IsAvailable(language)) return;
        _s.Settings.Language = language;
        _s.SaveSettings();
        Loc.Load(language);
        GetWindow().Title = _compact ? Loc.T("CONFIRM_WINDOW_TITLE") : "Floppy Hub";
        ApplyTheme(_s.Settings.Theme, save: false);   // baut alles in der neuen Sprache neu auf
    }

    private void ApplyScale(float scale)
    {
        _s.Settings.Scale = scale;
        _s.SaveSettings();
        UiScale.Apply(GetWindow(), scale);
        ApplyTheme(_s.Settings.Theme, save: false);
    }

    private void Rebuild()
    {
        var view = _main?.CurrentView ?? _s.Args.View ?? (_s.Args.Game ? "game" : "disc");
        this.ClearChildren();
        _main = null;

        if (_compact)
        {
            BuildCompact();
            return;
        }

        _main = new MainWindow();
        AddChild(_main);
        _main.Setup(_s, _covers, IsViewKey(view) ? view : "disc", t => ApplyTheme(t), ApplyScale, ApplyLanguage, Quit);
    }

    private static bool IsViewKey(string v) => v is "disc" or "library" or "write" or "drives" or "chat" or "game" or "log" or "settings" or "help";

    /// <summary>Nur die Rueckfrage - fuer den Motor, wenn die App nicht offen ist.</summary>
    private void BuildCompact()
    {
        this.ClearChildren();
        var root = Ui.Ui.Panel("WindowPanel");
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);

        var state = DiscState.Read(_s);
        if (state.Decision?.Action != GateAction.Ask)
        {
            // Nichts (mehr) zu fragen - z. B. Diskette schon wieder draussen.
            _s.Log.Write(LogLevel.Info, "App: keine Rueckfrage mehr noetig.");
            var info = Ui.Ui.VBox(12, Ui.Ui.IconLine("info", Loc.T("CONFIRM_NOTHING")));
            root.AddChild(Ui.Ui.Margin(info, 16));
            GetTree().CreateTimer(2.5).Timeout += Quit;
            return;
        }

        var panel = new ConfirmPanel();
        root.AddChild(Ui.Ui.Margin(panel, 14));
        panel.SetupForDisc(_s, state, LoadLibrary());
        panel.Finished += _ => GetTree().CreateTimer(0.2).Timeout += Quit;
    }

    private IReadOnlyList<LibraryEntry> LoadLibrary()
    {
        try { return File.Exists(_s.Paths.LibraryFile) ? LibraryCsv.Read(_s.Paths.LibraryFile) : []; }
        catch { return []; }
    }

    private void ShowFirstRun()
    {
        if (_main is null) return;
        var d = new RetroDialog(Loc.T("FIRST_TITLE"), "app", 520);

        var language = new ButtonGroup();
        var de = new CheckBox { Text = "Deutsch", ButtonGroup = language, ButtonPressed = Loc.Language == "de" };
        var en = new CheckBox { Text = "English", ButtonGroup = language, ButtonPressed = Loc.Language == "en", Disabled = !Loc.IsAvailable("en") };
        void LanguageChoice(CheckBox box, string value) => box.Toggled += on =>
        {
            if (on && Loc.Language != value) Callable.From(() => SwitchLanguageFromFirstRun(value)).CallDeferred();
        };
        LanguageChoice(de, "de");
        LanguageChoice(en, "en");

        var themes = new ButtonGroup();
        CheckBox ThemeChoice(string key, string value)
        {
            var c = new CheckBox { Text = Loc.T(key), ButtonGroup = themes, ButtonPressed = _s.Settings.Theme == value };
            c.Toggled += on =>
            {
                if (on && _s.Settings.Theme != value) Callable.From(() => SwitchThemeFromFirstRun(value)).CallDeferred();
            };
            return c;
        }

        d.Body.AddChild(Ui.Ui.HBox(16, Icons.Rect("app", 3f), Ui.Ui.VBox(6,
            Ui.Ui.Label(Loc.T("FIRST_HEADLINE"), "TitleLabel", wrap: true),
            Ui.Ui.Label(Loc.T("FIRST_TEXT"), wrap: true)).Expand()));
        d.Body.AddChild(new GroupBox(Loc.T("SET_LANGUAGE"), Ui.Ui.VBox(4, Ui.Ui.HBox(16, de, en), Ui.Ui.Dim(Loc.T("SET_LANGUAGE_HINT"), wrap: true))));
        d.Body.AddChild(new GroupBox(Loc.T("SET_THEME"), Ui.Ui.HBox(16,
            ThemeChoice("THEME_SYSTEM", AppSettings.ThemeSystem),
            ThemeChoice("THEME_LIGHT", AppSettings.ThemeLight),
            ThemeChoice("THEME_DARK", AppSettings.ThemeDark))));
        d.AddButton(Loc.T("FIRST_GO"), () =>
        {
            _s.Settings.FirstRunDone = true;
            _s.Settings.Language = Loc.Language;
            _s.SaveSettings();
        });
        d.Open(_main.DialogLayer);
    }

    private void SwitchThemeFromFirstRun(string theme)
    {
        ApplyTheme(theme);
        ShowFirstRun();   // Dialog im neuen Aussehen wieder oeffnen
    }

    private void SwitchLanguageFromFirstRun(string language)
    {
        ApplyLanguage(language);
        ShowFirstRun();   // Dialog in der neuen Sprache wieder oeffnen
    }

    // ------------------------------------------------------------------
    // Update-Pruefung
    // ------------------------------------------------------------------

    /// <summary>Wie oft die laufende App von sich aus nach einer neuen Version schaut (der Dienst bremst zusaetzlich).</summary>
    private const double UpdateCheckIntervalSeconds = 45 * 60;

    /// <summary>Diese Version wurde in dieser Sitzung schon angeboten - nicht alle 45 Minuten erneut.</summary>
    private string? _offeredUpdate;

    private void CheckForUpdate(bool force = false)
    {
        var current = (string)ProjectSettings.GetSetting("application/config/version");
        _s.Updates.CheckInBackground(current, info => Callable.From(() => ShowUpdateDialog(info)).CallDeferred(), force);
    }

    private void ShowUpdateDialog(Floppy.Core.Updates.UpdateInfo info)
    {
        if (_main is null || _quitting || _offeredUpdate == info.Version) return;
        _offeredUpdate = info.Version;
        UpdateFlow.Offer(_main, info);
    }

    /// <summary>
    /// Die App laeuft oft tagelang (auch nach "App oeffnen" ist es dieselbe Instanz): nur beim Start zu
    /// pruefen wuerde jedes spaeter erschienene Update verpassen. Deshalb alle 45 Minuten und beim
    /// Nach-vorn-Holen des Fensters erneut. (Nicht ueber einen Knoten der Oberflaeche - die wird bei
    /// Farbschema-/Sprachwechsel komplett neu gebaut.)
    /// </summary>
    private void ScheduleUpdateChecks()
    {
        GetTree().CreateTimer(UpdateCheckIntervalSeconds).Timeout += () =>
        {
            if (_quitting) return;
            CheckForUpdate();
            ScheduleUpdateChecks();
        };
    }

    // ------------------------------------------------------------------
    // Chat im Hintergrund
    // ------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_s is null || _compact || _quitting) return;
        _chatDemo?.Pump();
        _s.Chat.Pump();
    }

    // ------------------------------------------------------------------
    // Beenden
    // ------------------------------------------------------------------

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) Quit();
    }

    private void Quit()
    {
        if (_quitting) return;
        _quitting = true;

        // Chat: den anderen tschuess sagen (wartet hoechstens kurz)
        if (_s is not null && !_compact) _s.Chat.Shutdown();

        if (_s is not null && !_s.ReadOnlyMode && !_compact && _s.Options.ClearLogOnHubExit)
        {
            // Wie der Konsolen-Hub (V1): beim Schliessen das Launcher-Log leeren (Sicherung in .old).
            LogFile.Clear(_s.Log.FilePath);
        }

        _pipe.Cancel();
        if (_instance is not null)
        {
            try { _instance.ReleaseMutex(); } catch { }
            _instance.Dispose();
            _instance = null;
        }
        GetTree().Quit();
    }

    // ------------------------------------------------------------------
    // Entwicklung / Tests
    // ------------------------------------------------------------------

    private void ForgeIcons()
    {
        var dir = ProjectSettings.GlobalizePath(Icons.IconDir);
        var n = IconForge.ExportPngs(dir);

        var appDir = ProjectSettings.GlobalizePath("res://assets/app");
        System.IO.Directory.CreateDirectory(appDir);
        var appIcon = System.IO.Path.Combine(appDir, "icon.png");
        if (!System.IO.File.Exists(appIcon))
        {
            var icon = IconForge.Draw("app", Palette.Classic);
            icon.Resize(256, 256, Image.Interpolation.Nearest);
            icon.SavePng(appIcon);
        }

        GD.Print($"FORGE OK {n} Icons -> {dir}");
        GetTree().Quit();
    }

    private async void TakeScreenshotLater(string file, string? view)
    {
        OS.LowProcessorUsageMode = false;
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

        switch (view)
        {
            case "confirm": _main?.ConfirmDisc(); break;
            case "about": _main?.ShowAbout(); break;
            case "crt": _main?.TriggerCrt(); break;
            case "game-solve":
                // Level 1 per simulierter Tastatur loesen (prueft Steuerung + Geschafft-Dialog)
                _main?.ShowView("game");
                await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
                foreach (var key in new[] { Key.Right, Key.Right, Key.Right })
                {
                    Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = true });
                    Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = false });
                    await ToSignal(GetTree().CreateTimer(0.15), SceneTreeTimer.SignalName.Timeout);
                }
                break;
            case "write-demo" when _main is { Library.Count: > 0 }: _main.OpenWrite(_main.Library[^1]); break;
            case "editor":
                _main?.ShowView("game");
                _main?.OpenLevelEditor();
                break;
            case "update" or "update-progress" when _main is not null:
                var fake = new Floppy.Core.Updates.UpdateInfo("3.1.0", "Floppy Hub 3.1.0",
                    "https://github.com/MaelVW/Floppy/releases/tag/v3.1.0");
                if (view == "update") UpdateFlow.Offer(_main, fake, previewAuto: true);
                else UpdateFlow.PreviewProgress(_main, fake);
                break;
            case "chat-open":
                _main?.ShowView("chat");
                _s.Chat.ConnectOpen();
                await ToSignal(GetTree().CreateTimer(4), SceneTreeTimer.SignalName.Timeout);
                break;
            case { } chat when chat.StartsWith("chat-", StringComparison.Ordinal):
                await RunChatDemo(chat);
                break;
        }

        await ToSignal(GetTree().CreateTimer(1.6), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        image.SavePng(file);
        GD.Print($"SCREENSHOT OK {file} {image.GetWidth()}x{image.GetHeight()}");
        _s.Chat.Shutdown();
        _quitting = true;
        GetTree().Quit();
    }

    /// <summary>Chat-Vorschau ohne Netzwerk: zwei Mitspieler im Speicher.</summary>
    /// <summary>
    /// Vorschau fuer Admin/Sperren im Offenen Chat: diese (Vorschau-)Installation ist Admin,
    /// Tom und Lea sitzen mit im Raum. Gibt es nur fuer Bildschirmfotos - echte Admins stehen fest in ChatAdmins.
    /// </summary>
    private async Task RunAdminDemo(string scenario)
    {
        if (_main is null) return;
        _main.ShowView("chat");
        var me = _s.Chat.Identity.Fingerprint;
        _s.Chat.AdminCheck = fp => fp == me;

        var demo = new ChatDemo();
        _chatDemo = demo;
        demo.Attach(_s.Chat);
        await demo.StartBotsAsync(open: true, isAdmin: fp => fp == me);
        _s.Chat.ConnectOpen();
        await WaitUntil(() => _s.Chat.Session is { State: Floppy.Core.Chat.ChatSessionState.Connected }, 10);
        demo.StartBots(DateTimeOffset.UtcNow);
        await WaitUntil(() => _s.Chat.Session!.Members.Count == 3, 10);
        await Seconds(0.3);

        var session = _s.Chat.Session!;
        demo.Tom!.SendText("Hallo zusammen!", DateTimeOffset.UtcNow);
        await Seconds(0.3);
        session.SendText("Willkommen im Offenen Chat – bitte freundlich bleiben.", DateTimeOffset.UtcNow);
        await Seconds(0.3);
        demo.Lea!.SendText("Das hier ist eine Beispiel-Nachricht, die gleich ausgeblendet wird.", DateTimeOffset.UtcNow);
        await Seconds(0.5);

        if (scenario == "chat-ban-dialog")
        {
            _main.OpenChatBan(demo.Lea.Me.Fingerprint, demo.Lea.Me.Id);
            return;
        }
        if (scenario is "chat-ban" or "chat-moderation")
        {
            session.BanMember(demo.Lea.Me.Fingerprint, "Regelverstoß", TimeSpan.FromDays(1), DateTimeOffset.UtcNow);
            await WaitUntil(() => demo.Lea.State == Floppy.Core.Chat.ChatSessionState.Ended, 5);
            await Seconds(0.3);
        }
        if (scenario == "chat-moderation") _main.OpenChatModeration();
    }

    private async Task RunChatDemo(string scenario)
    {
        if (_main is null) return;
        if (scenario is "chat-admin" or "chat-ban" or "chat-ban-dialog" or "chat-moderation")
        {
            await RunAdminDemo(scenario);
            return;
        }
        _main.ShowView("chat");
        if (scenario == "chat-prompt") return;

        var demo = new ChatDemo();
        _chatDemo = demo;
        demo.Attach(_s.Chat);
        await demo.StartBotsAsync();
        _s.Chat.Connect(ChatDemo.Secret, "Schulhof");
        await WaitUntil(() => _s.Chat.Session is { State: Floppy.Core.Chat.ChatSessionState.Connected }, 10);
        demo.StartBots(DateTimeOffset.UtcNow);
        await WaitUntil(() => _s.Chat.Session!.Members.Count == 3, 10);
        await Seconds(0.3);

        var now = DateTimeOffset.UtcNow;
        demo.Tom!.SendText("Hallo! Ist das hier die Schulhof-Diskette?", now);
        await Seconds(0.3);
        _s.Chat.Session!.SendText("Ja, die Prüfzahl stimmt bei mir auch.", DateTimeOffset.UtcNow);
        await Seconds(0.3);
        demo.Lea!.SendText("Ich hab „Zwei Laufwerke“ in 0:18 geschafft 💾", DateTimeOffset.UtcNow);
        await Seconds(0.3);

        switch (scenario)
        {
            case "chat-proposal":
                demo.Tom.ProposeLocal(DateTimeOffset.UtcNow);
                await WaitUntil(() => _s.Chat.Session!.Proposal is not null, 5);
                break;
            case "chat-countdown":
                demo.Tom.ProposeLocal(DateTimeOffset.UtcNow);
                await WaitUntil(() => demo.Lea.Proposal is not null && _s.Chat.Session!.Proposal is not null, 5);
                demo.Lea.AnswerProposal(true, DateTimeOffset.UtcNow);
                await WaitUntil(() => _s.Chat.Session!.Proposal is { Stage: Floppy.Core.Chat.ProposalStage.Countdown }, 5);
                await Seconds(2);
                break;
            case "chat-local":
                _s.Chat.Session.ProposeLocal(DateTimeOffset.UtcNow);
                await WaitUntil(() => demo.Tom.Proposal is not null, 5);
                demo.Tom.AnswerProposal(true, DateTimeOffset.UtcNow);
                await WaitUntil(() => _s.Chat.Session.Mode == Floppy.Core.Chat.ChatMode.Local, 5);
                await Seconds(0.5);
                demo.Tom.SendText("Jetzt ohne Dienst und ohne Limit.", DateTimeOffset.UtcNow);
                await Seconds(0.3);
                break;
            case "chat-chess":
                _s.Chat.Session!.ChallengeChess(demo.Tom!.Me.Id, DateTimeOffset.UtcNow);
                await WaitUntil(() => demo.Tom.Chess is not null, 5);
                demo.Tom.AnswerChessChallenge(true, DateTimeOffset.UtcNow);
                await WaitUntil(() => _s.Chat.Session!.Chess is { Stage: Floppy.Core.Chat.ChessGameStage.Active }, 5);
                Floppy.Core.Chess.ChessMove.TryParse("e2e4", out var wm1); _s.Chat.Session!.MakeChessMove(wm1, DateTimeOffset.UtcNow);
                await WaitUntil(() => demo.Tom.Chess!.Board.History.Count == 1, 5);
                Floppy.Core.Chess.ChessMove.TryParse("e7e5", out var bm1); demo.Tom.MakeChessMove(bm1, DateTimeOffset.UtcNow);
                await WaitUntil(() => _s.Chat.Session!.Chess!.Board.History.Count == 2, 5);
                Floppy.Core.Chess.ChessMove.TryParse("g1f3", out var wm2); _s.Chat.Session!.MakeChessMove(wm2, DateTimeOffset.UtcNow);
                await WaitUntil(() => demo.Tom.Chess!.Board.History.Count == 3, 5);
                _main.ShowView("chess");
                break;
            case "chat-scores":
                _main.OpenChatScores();
                await Seconds(3);
                break;
        }
    }

    private async Task Seconds(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task WaitUntil(Func<bool> condition, double timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!condition() && DateTime.UtcNow < deadline) await Seconds(0.05);
        if (!condition()) GD.PushWarning("Chat-Vorschau: Bedingung nicht erreicht.");
    }
}
