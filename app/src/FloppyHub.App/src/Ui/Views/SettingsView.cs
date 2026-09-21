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
    private OptionButton _extraDrive1 = null!;
    private OptionButton _extraDrive2 = null!;
    private readonly List<string> _extraDrive1Options = [];
    private readonly List<string> _extraDrive2Options = [];

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
        var de = new CheckBox { Text = "Deutsch", ButtonGroup = group, ButtonPressed = Loc.Language == "de" };
        var en = new CheckBox { Text = "English", ButtonGroup = group, ButtonPressed = Loc.Language == "en", Disabled = !Loc.IsAvailable("en") };
        // erst nach dem Aufbau umschalten (baut die ganze Oberflaeche neu)
        de.Toggled += on => { if (on && Loc.Language != "de") Callable.From(() => Host.ApplyLanguage("de")).CallDeferred(); };
        en.Toggled += on => { if (on && Loc.Language != "en") Callable.From(() => Host.ApplyLanguage("en")).CallDeferred(); };
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

        // ---- Zusaetzliche Laufwerke ----
        _extraDrive1 = new OptionButton();
        _extraDrive2 = new OptionButton();
        _extraDrive1.ItemSelected += _ => OnExtraDriveChanged();
        _extraDrive2.ItemSelected += _ => OnExtraDriveChanged();
        var extraGrid = new GridContainer { Columns = 2 };
        extraGrid.AddChild(Ui.Label(Loc.T("SET_EXTRA_DRIVE_1")));
        extraGrid.AddChild(_extraDrive1);
        extraGrid.AddChild(Ui.Label(Loc.T("SET_EXTRA_DRIVE_2")));
        extraGrid.AddChild(_extraDrive2);
        RefreshExtraDrives(s.Options.ExtraDriveLetters);
        column.AddChild(new GroupBox(Loc.T("SET_EXTRA_DRIVES"), Ui.VBox(6,
            Ui.IconLine("info", Loc.T("SET_EXTRA_DRIVES_HINT"), "DimLabel"),
            extraGrid,
            Ui.Dim(Loc.T("SET_EXTRA_DRIVES_RESTART"), wrap: true))));

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
        var checkUpdate = Ui.Button(Loc.T("BTN_CHECK_UPDATE"), "cloud", () =>
        {
            if (s.ReadOnlyMode) return;
            Host.SetStatusMessage(Loc.T("UPDATE_CHECKING"), "cloud");
            var current = (string)ProjectSettings.GetSetting("application/config/version");
            s.Updates.CheckManually(current, info => Callable.From(() =>
            {
                if (info is null) Host.SetStatusMessage(Loc.T("UPDATE_NONE"), "ok");
                else UpdateFlow.Offer(Host, info);
            }).CallDeferred());
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
            Ui.HBox(6, configure, folder, data, checkUpdate))));

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

    public override void OnTick()
    {
        UpdateMotor();
        RefreshExtraDrives(SelectedExtraDrives());
    }

    private void UpdateMotor()
    {
        var running = MotorProbe.IsRunning();
        _motorLed.Texture = Icons.Get(running ? "led_on" : "led_off");
        _motorText.Text = running ? Loc.T("MOTOR_RUNNING") : Loc.T("MOTOR_STOPPED");
    }

    /// <summary>
    /// Fuellt beide Dropdowns mit den aktuell angeschlossenen Wechseldatentraegern (ausser dem
    /// Hauptlaufwerk) plus den gerade gewaehlten Buchstaben, auch wenn deren Laufwerk gerade
    /// nicht angeschlossen ist - sonst wuerde die Auswahl beim Neuaufbau verschwinden.
    /// </summary>
    private void RefreshExtraDrives(IReadOnlyList<string> selected)
    {
        var primary = Host.Services.Options.DriveLetter;
        var present = DriveSnapshot.RemovableDrives()
            .Select(d => d.Root.TrimEnd('\\'))
            .Where(l => !l.Equals(primary, StringComparison.OrdinalIgnoreCase))
            .Concat(selected)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();

        void Fill(OptionButton box, List<string> options, string? own, string? other)
        {
            options.Clear();
            box.Clear();
            box.AddItem(Loc.T("SET_EXTRA_DRIVE_NONE"));
            var selectIndex = 0;
            foreach (var letter in present)
            {
                if (other is not null && letter.Equals(other, StringComparison.OrdinalIgnoreCase)) continue;
                options.Add(letter);
                box.AddItem(letter);
                if (own is not null && letter.Equals(own, StringComparison.OrdinalIgnoreCase)) selectIndex = options.Count;
            }
            box.Select(selectIndex);
        }

        Fill(_extraDrive1, _extraDrive1Options, selected.ElementAtOrDefault(0), selected.ElementAtOrDefault(1));
        Fill(_extraDrive2, _extraDrive2Options, selected.ElementAtOrDefault(1), selected.ElementAtOrDefault(0));
    }

    private List<string> SelectedExtraDrives() =>
        [.. new[] { (_extraDrive1, _extraDrive1Options), (_extraDrive2, _extraDrive2Options) }
            .Select(t => t.Item1.Selected > 0 && t.Item1.Selected <= t.Item2.Count ? t.Item2[t.Item1.Selected - 1] : "")
            .Where(l => l.Length > 0)];

    private void OnExtraDriveChanged()
    {
        var selected = SelectedExtraDrives();
        RefreshExtraDrives(selected);
        if (!Host.Services.ReadOnlyMode)
            IniDocument.SetValue(Host.Services.Paths.ConfigFile, "drive", "extra_letters", string.Join(",", selected));
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
