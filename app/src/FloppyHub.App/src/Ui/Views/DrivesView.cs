using Floppy.Core;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using Godot;

namespace FloppyHub.App.Ui.Views;

/// <summary>
/// Wechseldatentraeger anzeigen (Disketten, USB, CD). Nur lesen, nie ueberwachen:
/// Der Motor schaut weiterhin ausschliesslich auf A:.
/// </summary>
public partial class DrivesView : ViewBase
{
    public override string Key => "drives";

    private HFlowContainer _tiles = null!;
    private GridContainer _specs = null!;
    private ProgressBar _meter = null!;
    private Label _meterText = null!;
    private TextureRect _bigIcon = null!;
    private Label _bigTitle = null!;
    private IReadOnlyList<DriveSnapshot> _drives = [];
    private string? _selectedRoot;
    private string _fingerprint = "";

    protected override void Build()
    {
        AddChild(Ui.Panel("HintPanel", Ui.IconLine("info", Loc.T("DRIVES_HINT"))));

        _tiles = new HFlowContainer().Expand(vertical: true);
        _tiles.AddThemeConstantOverride("h_separation", 10);
        _tiles.AddThemeConstantOverride("v_separation", 10);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }.Expand(vertical: true);
        scroll.AddChild(Ui.Margin(_tiles, 2).Expand(vertical: true));
        var tilesBox = new GroupBox(Loc.T("DRIVES_LIST"), Ui.VBox(6, scroll,
            Ui.HBox(6, Ui.Button(Loc.T("BTN_REFRESH"), "refresh", () => Refresh(force: true))))).Expand(vertical: true);

        _bigIcon = Icons.Rect("media_empty", 3f);
        _bigIcon.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _bigTitle = Ui.Label("", "TitleLabel");
        _bigTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _specs = new GridContainer { Columns = 2 };
        _meter = new ProgressBar { ShowPercentage = false, MaxValue = 1, CustomMinimumSize = new Vector2(0, 24) };
        _meterText = Ui.Dim("");
        _meterText.HorizontalAlignment = HorizontalAlignment.Center;

        var details = new GroupBox(Loc.T("DRIVES_DETAILS"), Ui.VBox(8, _bigIcon, _bigTitle, _meter, _meterText, _specs));
        details.CustomMinimumSize = new Vector2(280, 0);

        AddChild(Ui.HBox(10, tilesBox, details).Expand(vertical: true));
    }

    public override void OnShown() => Refresh(force: true);
    public override void OnTick() => Refresh(force: false);

    private void Refresh(bool force)
    {
        var drives = DriveSnapshot.RemovableDrives().ToList();
        var floppyRoot = PathRules.DriveRoot(Host.Services.Options.DriveLetter);
        if (!drives.Any(d => d.Root.Equals(floppyRoot, StringComparison.OrdinalIgnoreCase)))
            drives.Insert(0, new DriveSnapshot(floppyRoot, MediaKind.Floppy, false, "", "", 0, 0));

        var fingerprint = string.Join("|", drives.Select(d => $"{d.Root}{d.Ready}{d.FreeBytes}{d.Label}"));
        if (!force && fingerprint == _fingerprint) return;
        _fingerprint = fingerprint;
        _drives = drives;

        _tiles.ClearChildren();
        foreach (var d in drives) _tiles.AddChild(Tile(d));

        _selectedRoot ??= drives.FirstOrDefault()?.Root;
        ShowDetails(drives.FirstOrDefault(d => d.Root == _selectedRoot) ?? drives.FirstOrDefault());
    }

    private PanelContainer Tile(DriveSnapshot d)
    {
        var selected = d.Root == _selectedRoot;
        var icon = Icons.Rect(MediaIcon(d), 2f);
        icon.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        var title = Ui.Label($"{d.Root.TrimEnd('\\')}  {KindName(d.Kind)}", "BoldLabel");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var sub = Ui.Dim(d.Ready ? (string.IsNullOrWhiteSpace(d.Label) ? d.FileSystem : $"{d.Label} · {d.FileSystem}") : Loc.T("DRIVES_NO_MEDIA"));
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        var bar = new ProgressBar { ShowPercentage = false, MaxValue = 1, Value = d.UsedRatio, CustomMinimumSize = new Vector2(0, 14), Visible = d.Ready };
        var free = Ui.Dim(d.Ready ? Loc.T("DRIVES_FREE_OF", Ui.Bytes(d.FreeBytes), Ui.Bytes(d.TotalBytes)) : " ");
        free.HorizontalAlignment = HorizontalAlignment.Center;

        var tile = Ui.Panel(selected ? "SelectedTile" : "RaisedPanel", Ui.VBox(5, icon, title, sub, bar, free));
        tile.CustomMinimumSize = new Vector2(176, 0);
        tile.MouseDefaultCursorShape = CursorShape.PointingHand;
        tile.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                _selectedRoot = d.Root;
                Refresh(force: true);
            }
        };
        return tile;
    }

    private void ShowDetails(DriveSnapshot? d)
    {
        _specs.ClearChildren();
        if (d is null)
        {
            _bigTitle.Text = Loc.T("DRIVES_NONE");
            _meter.Value = 0;
            _meterText.Text = "";
            return;
        }

        _bigIcon.Texture = Icons.Get(MediaIcon(d), 3f);
        _bigTitle.Text = $"{d.Root.TrimEnd('\\')}  {KindName(d.Kind)}";
        _meter.Value = d.UsedRatio;
        _meterText.Text = d.Ready ? Loc.T("DRIVES_USED_PERCENT", Ui.Number(d.UsedRatio * 100, "0")) : Loc.T("DRIVES_NO_MEDIA");

        void Row(string key, string value)
        {
            _specs.AddChild(Ui.Dim(Loc.T(key)));
            _specs.AddChild(Ui.Label(value));
        }

        Row("SPEC_DRIVE", d.Root);
        Row("SPEC_KIND", KindName(d.Kind));
        Row("SPEC_READY", d.Ready ? Loc.T("YES") : Loc.T("NO"));
        if (!d.Ready) return;
        Row("SPEC_LABEL", string.IsNullOrWhiteSpace(d.Label) ? "–" : d.Label);
        Row("SPEC_FS", d.FileSystem);
        Row("SPEC_CAPACITY", Ui.Bytes(d.TotalBytes));
        Row("SPEC_USED", Ui.Bytes(d.UsedBytes));
        Row("SPEC_FREE", Ui.Bytes(d.FreeBytes));
        if (d.Root.Equals(PathRules.DriveRoot(Host.Services.Options.DriveLetter), StringComparison.OrdinalIgnoreCase))
            Row("SPEC_WATCHED", Loc.T("DRIVES_WATCHED", Host.Services.Options.PollSeconds));
    }

    private static string MediaIcon(DriveSnapshot d) => d.Kind switch
    {
        MediaKind.Floppy => d.Ready ? "media_floppy" : "media_empty",
        MediaKind.Usb => "media_usb",
        MediaKind.Optical => d.Ready ? "media_cd" : "media_empty",
        _ => "drives",
    };

    private static string KindName(MediaKind k) => k switch
    {
        MediaKind.Floppy => Loc.T("MEDIA_FLOPPY"),
        MediaKind.Usb => Loc.T("MEDIA_USB"),
        MediaKind.Optical => Loc.T("MEDIA_CD"),
        _ => Loc.T("MEDIA_OTHER"),
    };
}
