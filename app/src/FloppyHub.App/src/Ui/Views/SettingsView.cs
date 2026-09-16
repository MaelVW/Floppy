using Floppy.Core;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using FloppyHub.App.Services;
using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui.Views;

/// <summary>Einstellungen: Darstellung, Sprache, Bibliothek, Vertrauensliste, Motor.</summary>
public partial class SettingsView : ViewBase
{
    public override string Key => "settings";

    private Tree _trust = null!;
    private Label _coverSize = null!;
    private TextureRect _motorLed = null!;
    private Label _motorText = null!;

    protected override void Build()
    {
        var s = Host.Services;
        var column = Ui.VBox(12);

        // ---- Darstellung ----
        var theme = new OptionButton();
        theme.AddItem(Loc.T("THEME_SYSTEM"));
        theme.AddItem(Loc.T("THEME_LIGHT"));
        theme.AddItem(Loc.T("THEME_DARK"));
        theme.Select(s.Settings.Theme switch { AppSettings.ThemeLight => 1, AppSettings.ThemeDark => 2, _ => 0 });
        theme.ItemSelected += i => Host.ApplyTheme(i switch { 1 => AppSettings.ThemeLight, 2 => AppSettings.ThemeDark, _ => AppSettings.ThemeSystem });

        float[] scales = [0f, 1f, 1.25f, 1.5f, 2f];
        var scale = new OptionButton();
        scale.AddItem(Loc.T("SCALE_AUTO", Ui.Number(UiScale.Detect() * 100, "0")));
        foreach (var f in scales.Skip(1)) scale.AddItem($"{Ui.Number(f * 100, "0")} %");
        scale.Select(Math.Max(0, Array.IndexOf(scales, s.Settings.Scale)));
        scale.ItemSelected += i => Host.ApplyScale(scales[(int)i]);

        var look = new GridContainer { Columns = 2 };
        look.AddChild(Ui.Label(Loc.T("SET_THEME")));
        look.AddChild(theme);
        look.AddChild(Ui.Label(Loc.T("SET_SCALE")));
        look.AddChild(scale);
        column.AddChild(new GroupBox(Loc.T("SET_LOOK"), look));

        // ---- Sprache ----
        var group = new ButtonGroup();
        var de = new CheckBox { Text = "Deutsch", ButtonGroup = group, ButtonPressed = true };
        var en = new CheckBox { Text = "English", ButtonGroup = group, Disabled = !Loc.IsAvailable("en") };
        column.AddChild(new GroupBox(Loc.T("SET_LANGUAGE"), Ui.VBox(4,
            Ui.HBox(16, de, en), Ui.Dim(Loc.T("SET_LANGUAGE_HINT"), wrap: true))));

        // ---- Bibliothek ----
        var covers = new CheckBox { Text = Loc.T("SET_COVERS"), ButtonPressed = s.Settings.LoadCovers };
        covers.Toggled += on =>
        {
            s.Settings.LoadCovers = on;
            s.SaveSettings();
        };
        _coverSize = Ui.Dim("");
        var clearCovers = Ui.Button(Loc.T("BTN_CLEAR_COVERS"), "remove", () =>
        {
            if (!s.ReadOnlyMode) Host.Covers.Clear();
            UpdateCoverSize();
        });
        column.AddChild(new GroupBox(Loc.T("SET_LIBRARY"), Ui.VBox(6,
            covers,
            Ui.Dim(Loc.T("SET_COVERS_HINT"), wrap: true),
            Ui.HBox(8, clearCovers, _coverSize))));

        // ---- Vertrauensliste ----
        _trust = new Tree
        {
            Columns = 3,
            HideRoot = true,
            ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row,
            CustomMinimumSize = new Vector2(0, 130),
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
        };
        _trust.SetColumnTitle(0, Loc.T("COL_PROGRAM"));
        _trust.SetColumnTitle(1, Loc.T("COL_SOURCE"));
        _trust.SetColumnTitle(2, Loc.T("COL_SINCE"));
        _trust.SetColumnExpand(0, true);
        _trust.SetColumnExpand(1, false);
        _trust.SetColumnExpand(2, false);
        _trust.SetColumnCustomMinimumWidth(1, 110);
        _trust.SetColumnCustomMinimumWidth(2, 96);
        var remove = Ui.Button(Loc.T("BTN_REMOVE_TRUST"), "remove", RemoveTrust);
        column.AddChild(new GroupBox(Loc.T("SET_TRUST"), Ui.VBox(6,
            Ui.IconLine("trust", Loc.T("SET_TRUST_HINT"), "DimLabel"),
            _trust,
            Ui.HBox(6, remove))));

        // ---- Motor ----
        _motorLed = Icons.Rect("led_off");
        _motorText = Ui.Label("");
        var configure = Ui.Button(Loc.T("BTN_OPEN_CONFIG"), "file", () => OS.ShellOpen(s.Paths.ConfigFile));
        var folder = Ui.Button(Loc.T("BTN_OPEN_HOME"), "folder", () => OS.ShellOpen(s.Paths.Home));
        var data = Ui.Button(Loc.T("BTN_OPEN_DATA"), "folder", () =>
        {
            FloppyPaths.EnsureDirectory(s.Paths.UserData);
            OS.ShellOpen(s.Paths.UserData);
        });
        var info = new GridContainer { Columns = 2 };
        void Row(string key, string value)
        {
            info.AddChild(Ui.Dim(Loc.T(key)));
            var l = Ui.Label(value);
            l.ClipText = true;
            l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            l.TooltipText = value;
            l.MouseFilter = MouseFilterEnum.Pass;
            info.AddChild(l.Expand());
        }
        Row("SET_WATCHED_DRIVE", Loc.T("DRIVES_WATCHED_SHORT", s.DriveRoot, s.Options.PollSeconds));
        Row("SET_HOME", s.Paths.Home);
        Row("SET_USERDATA", s.Paths.UserData);
        Row("SET_LOGFILE", s.Log.FilePath ?? Loc.T("LOG_DISABLED"));
        Row("SET_VERSION", (string)ProjectSettings.GetSetting("application/config/version"));
        column.AddChild(new GroupBox(Loc.T("SET_MOTOR"), Ui.VBox(8,
            Ui.HBox(6, _motorLed, _motorText),
            info,
            Ui.HBox(6, configure, folder, data))));

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }.Expand(vertical: true);
        scroll.AddChild(Ui.Margin(column.Expand(), 0, 0, 8, 0).Expand());
        AddChild(scroll);
    }

    public override void OnShown()
    {
        FillTrust();
        UpdateCoverSize();
        UpdateMotor();
    }

    public override void OnTick() => UpdateMotor();

    private void UpdateMotor()
    {
        var running = MotorProbe.IsRunning();
        _motorLed.Texture = Icons.Get(running ? "led_on" : "led_off");
        _motorText.Text = running ? Loc.T("MOTOR_RUNNING") : Loc.T("MOTOR_STOPPED");
    }

    private void UpdateCoverSize() =>
        _coverSize.Text = Loc.T("SET_COVERS_SIZE", Ui.Bytes(Host.Covers.SizeOnDisk()));

    private void FillTrust()
    {
        _trust.Clear();
        var root = _trust.CreateItem();
        var entries = Host.Services.Trust.Load();
        foreach (var e in entries.OrderBy(e => e.Label, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = _trust.CreateItem(root);
            item.SetIcon(0, Icons.Get("trust"));
            var args = string.IsNullOrEmpty(e.Arguments) ? "" : "  " + e.Arguments;
            item.SetText(0, $"{e.Label}  –  {e.Path}{args}");
            item.SetTooltipText(0, $"{e.Path}{args}\nSHA-256: {e.Sha256}");
            item.SetText(1, e.Source == TrustStore.SourceWritten ? Loc.T("TRUST_SRC_WRITTEN") : Loc.T("TRUST_SRC_CONFIRMED"));
            item.SetText(2, e.Added.ToString("yyyy-MM-dd"));
            item.SetMetadata(0, e.Path);
            item.SetMetadata(1, e.Arguments);
        }
        if (entries.Count == 0) _trust.CreateItem(root).SetText(0, Loc.T("TRUST_EMPTY"));
    }

    private void RemoveTrust()
    {
        var item = _trust.GetSelected();
        var path = item?.GetMetadata(0).AsString();
        if (string.IsNullOrEmpty(path)) return;
        var args = item!.GetMetadata(1).AsString();
        RetroDialog.Ask(Host.DialogLayer, Loc.T("TRUST_REMOVE_TITLE"), Loc.T("TRUST_REMOVE_TEXT", path), Loc.T("BTN_REMOVE_TRUST"), () =>
        {
            if (Host.Services.ReadOnlyMode) return;
            Host.Services.Trust.Remove(path, args);
            Host.Services.Log.Write(LogLevel.Info, $"Freigabe entfernt: {path} {args}".TrimEnd());
            FillTrust();
        });
    }
}
