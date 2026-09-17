using Floppy.Core;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui.Views;

/// <summary>
/// Wechseldatentraeger anzeigen: Disketten, USB-Sticks, Speicherkarten, externe
/// Festplatten, CD/DVD/Blu-ray. Nur LESEN, nie ueberwachen - der Motor schaut
/// weiterhin ausschliesslich auf A:. Rechts ein Eigenschaften-Bereich wie bei Windows XP.
/// </summary>
public partial class DrivesView : ViewBase
{
    public override string Key => "drives";

    private HFlowContainer _groups = null!;
    private CheckBox _showInternal = null!;
    private TextureRect _bigIcon = null!;
    private Label _bigTitle = null!;
    private Label _bigSub = null!;
    private GridContainer _specs = null!;
    private GridContainer _legend = null!;
    private CapacityPie _pie = null!;
    private Control _usageBlock = null!;
    private Button _open = null!;
    private IReadOnlyList<DriveSnapshot> _drives = [];
    private string? _selectedRoot;
    private string _fingerprint = "";

    protected override void Build()
    {
        _showInternal = new CheckBox { Text = Loc.T("DRIVES_SHOW_INTERNAL") };
        _showInternal.Toggled += _ => Refresh(force: true);
        AddChild(Ui.HBox(10,
            Ui.Panel("HintPanel", Ui.IconLine("info", Loc.T("DRIVES_HINT"))).Expand(),
            _showInternal,
            Ui.Button(Loc.T("BTN_REFRESH"), "refresh", () => Refresh(force: true))));

        _groups = new HFlowContainer();
        _groups.AddThemeConstantOverride("h_separation", 18);
        _groups.AddThemeConstantOverride("v_separation", 14);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }.Expand(vertical: true);
        scroll.AddChild(Ui.Margin(_groups.Expand(), 2, 2, 8, 2).Expand());

        // ---- Eigenschaften ----
        _bigIcon = Icons.Rect("media_empty", 2f);
        _bigTitle = Ui.Label("", "TitleLabel");
        _bigTitle.ClipText = true;
        _bigTitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _bigSub = Ui.Dim("", wrap: true);
        _specs = new GridContainer { Columns = 2 };
        _legend = new GridContainer { Columns = 4 };
        _pie = new CapacityPie { SizeFlagsHorizontal = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(190, 104) };
        _open = Ui.Button(Loc.T("BTN_OPEN_EXPLORER"), "folder", () =>
        {
            if (_selectedRoot is not null) OS.ShellOpen(_selectedRoot);
        });

        _usageBlock = Ui.VBox(8, new HSeparator(), _legend, _pie);
        var properties = new GroupBox(Loc.T("DRIVES_DETAILS"), Ui.VBox(6,
            Ui.HBox(10, _bigIcon, Ui.VBox(2, _bigTitle, _bigSub).Expand()),
            new HSeparator(),
            _specs,
            _usageBlock,
            Ui.Spacer(false),
            Ui.HBox(6, _open)));
        properties.SizeFlagsVertical = SizeFlags.ExpandFill;

        var propertiesScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(350, 0),
        };
        propertiesScroll.AddChild(properties.Expand(vertical: true));

        AddChild(Ui.HBox(10, scroll, propertiesScroll).Expand(vertical: true));
    }

    public override void OnShown() => Refresh(force: true);
    public override void OnTick() => Refresh(force: false);

    private void Refresh(bool force)
    {
        var drives = Host.Services.Args.DemoDrives
            ? DemoDrives(_showInternal.ButtonPressed)
            : DriveSnapshot.ListDrives(_showInternal.ButtonPressed).ToList();

        // Das Diskettenlaufwerk des Launchers immer zeigen - auch wenn es gerade fehlt.
        var floppyRoot = PathRules.DriveRoot(Host.Services.Options.DriveLetter);
        if (!drives.Any(d => d.Root.Equals(floppyRoot, StringComparison.OrdinalIgnoreCase)))
            drives.Insert(0, new DriveSnapshot(floppyRoot, MediaKind.Floppy, false, "", "", 0, 0) { Bus = BusKind.Unknown, Model = Missing });

        var fingerprint = string.Join("|", drives.Select(d => $"{d.Root}{d.Ready}{d.FreeBytes}{d.Label}{d.Kind}"));
        if (!force && fingerprint == _fingerprint) return;
        _fingerprint = fingerprint;
        _drives = drives;

        if (_selectedRoot is null || drives.All(d => d.Root != _selectedRoot))
            _selectedRoot = drives.FirstOrDefault(d => d.Ready)?.Root ?? drives.FirstOrDefault()?.Root;

        _groups.ClearChildren();
        foreach (var group in drives.GroupBy(d => d.Kind).OrderBy(g => g.Key))
        {
            var flow = new HFlowContainer();
            flow.AddThemeConstantOverride("h_separation", 10);
            flow.AddThemeConstantOverride("v_separation", 10);
            foreach (var d in group) flow.AddChild(Tile(d));

            var header = Ui.HBox(6, Icons.Rect(GroupIcon(group.Key), 0.5f), Ui.Label(GroupName(group.Key), "BoldLabel"),
                Ui.Dim($"({group.Count()})"));
            _groups.AddChild(Ui.VBox(6, header, flow));
        }
        if (drives.Count == 0) _groups.AddChild(Ui.Dim(Loc.T("DRIVES_NONE")));

        ShowDetails(drives.FirstOrDefault(d => d.Root == _selectedRoot));
    }

    // Kennzeichnung fuer "Laufwerk fehlt ganz" (A: nicht angeschlossen)
    private const string Missing = "\u0001missing";

    private PanelContainer Tile(DriveSnapshot d)
    {
        var selected = d.Root == _selectedRoot;
        var icon = Icons.Rect(MediaIcon(d), 2f);
        icon.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        if (!d.Ready) icon.Modulate = new Color(1, 1, 1, 0.6f);

        var title = Ui.Label($"{d.Root.TrimEnd('\\')}  {ShortName(d)}", "BoldLabel");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.ClipText = true;
        title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var sub = Ui.Dim(SubLine(d));
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.ClipText = true;
        sub.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var bar = new ProgressBar { ShowPercentage = false, MaxValue = 1, Value = d.UsedRatio, CustomMinimumSize = new Vector2(0, 14), Visible = d.Ready };
        var free = Ui.Dim(d.Ready ? Loc.T("DRIVES_FREE_OF", Ui.Bytes(d.FreeBytes), Ui.Bytes(d.TotalBytes)) : " ");
        free.HorizontalAlignment = HorizontalAlignment.Center;

        var tile = Ui.Panel(selected ? "SelectedTile" : "RaisedPanel", Ui.VBox(4, icon, title, sub, bar, free));
        tile.CustomMinimumSize = new Vector2(190, 0);
        tile.MouseDefaultCursorShape = CursorShape.PointingHand;
        tile.TooltipText = string.IsNullOrEmpty(d.DeviceName) || d.Model == Missing ? d.Root : $"{d.Root}  {d.DeviceName}";
        tile.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) return;
            if (click.DoubleClick && d.Ready) OS.ShellOpen(d.Root);
            CountCupholderClicks(d);
            _selectedRoot = d.Root;
            Refresh(force: true);
        };
        return tile;
    }

    private void ShowDetails(DriveSnapshot? d)
    {
        _specs.ClearChildren();
        _legend.ClearChildren();
        _open.Disabled = d is not { Ready: true };

        _usageBlock.Visible = d is { Ready: true };
        if (d is null)
        {
            _bigTitle.Text = Loc.T("DRIVES_NONE");
            _bigSub.Text = "";
            return;
        }

        _bigIcon.Texture = Icons.Get(MediaIcon(d), 2f);
        _bigTitle.Text = $"{d.Root.TrimEnd('\\')}  {(string.IsNullOrWhiteSpace(d.Label) ? ShortName(d) : d.Label)}";
        _bigSub.Text = d.Model == Missing ? Loc.T("DRIVES_NOT_CONNECTED")
            : d.Ready ? KindAndFormat(d)
            : Loc.T("DRIVES_NO_MEDIA");

        void Row(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            _specs.AddChild(Ui.Dim(Loc.T(key)));
            var l = Ui.Label(value);
            l.ClipText = true;
            l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            l.TooltipText = value;
            l.MouseFilter = MouseFilterEnum.Pass;
            _specs.AddChild(l.Expand());
        }

        Row("SPEC_KIND", KindName(d.Kind));
        if (d.Format != MediaFormat.Unknown) Row("SPEC_FORMAT", Loc.T("FORMAT_" + d.Format.ToString().ToUpperInvariant()));
        if (d.Model != Missing)
        {
            Row("SPEC_BUS", Loc.T("BUS_" + d.Bus.ToString().ToUpperInvariant()));
            Row("SPEC_DEVICE", d.DeviceName + (d.Revision.Length > 0 ? $"  ({d.Revision})" : ""));
        }
        if (d.Ready)
        {
            Row("SPEC_FS", d.FileSystem);
            if (d.ClusterBytes > 0) Row("SPEC_CLUSTER", Ui.Bytes(d.ClusterBytes));
            Row("SPEC_SERIAL", d.SerialText);
        }
        if (d.Root.Equals(PathRules.DriveRoot(Host.Services.Options.DriveLetter), StringComparison.OrdinalIgnoreCase))
            Row("SPEC_WATCHED", Loc.T("DRIVES_WATCHED", Host.Services.Options.PollSeconds));

        if (!d.Ready) return;

        void Legend(Color? swatch, string key, long bytes)
        {
            if (swatch is { } color)
            {
                var box = Ui.Panel("FieldPanel", new ColorRect { Color = color, CustomMinimumSize = new Vector2(12, 12) });
                box.AddThemeStyleboxOverride("panel", new BevelStyle { Kind = BevelStyle.Edge.ThinSunken, Palette = Palette.Current });
                _legend.AddChild(box);
            }
            else
            {
                _legend.AddChild(new Control());
            }
            _legend.AddChild(Ui.Label(Loc.T(key)));
            var exact = Ui.Label($"{Ui.Number(bytes, "#,0")} {Loc.T("UNIT_BYTES")}");
            exact.HorizontalAlignment = HorizontalAlignment.Right;
            _legend.AddChild(exact.Expand());
            var shortSize = Ui.Label(Ui.Bytes(bytes));
            shortSize.HorizontalAlignment = HorizontalAlignment.Right;
            shortSize.CustomMinimumSize = new Vector2(70, 0);
            _legend.AddChild(shortSize);
        }

        Legend(CapacityPie.UsedColor, "SPEC_USED", d.UsedBytes);
        Legend(CapacityPie.FreeColor, "SPEC_FREE", d.FreeBytes);
        Legend(null, "SPEC_CAPACITY", d.TotalBytes);
        _pie.SetRatio(d.UsedRatio);
    }

    // ------------------------------------------------------------------
    // Texte + Icons
    // ------------------------------------------------------------------

    private static string MediaIcon(DriveSnapshot d) => d.Kind switch
    {
        MediaKind.Floppy => d.Ready ? "media_floppy" : "media_empty",
        MediaKind.Usb => "media_usb",
        MediaKind.Sd => d.Ready ? "media_sd" : "media_reader",
        MediaKind.ExternalDisk => "media_hdd",
        MediaKind.Optical when !d.Ready => "media_empty",
        MediaKind.Optical when d.Bus == BusKind.Virtual => "media_virtual",
        MediaKind.Optical => d.Format switch
        {
            MediaFormat.Dvd => "media_dvd",
            MediaFormat.BluRay => "media_bluray",
            MediaFormat.AudioCd => "media_audio",
            _ => "media_cd",
        },
        _ => "drives",
    };

    private static string GroupIcon(MediaKind kind) => kind switch
    {
        MediaKind.Floppy => "media_floppy",
        MediaKind.Usb => "media_usb",
        MediaKind.Sd => "media_sd",
        MediaKind.ExternalDisk => "media_hdd",
        MediaKind.Optical => "media_cd",
        _ => "drives",
    };

    private static string GroupName(MediaKind kind) => Loc.T("GROUP_" + kind.ToString().ToUpperInvariant());

    public static string KindName(MediaKind k) => Loc.T("MEDIA_" + k.ToString().ToUpperInvariant());

    private static string ShortName(DriveSnapshot d)
    {
        if (!string.IsNullOrWhiteSpace(d.Label)) return d.Label;
        if (d.Model != Missing && d.Model.Length > 0 && d.Kind is not MediaKind.Floppy) return d.Model;
        return KindName(d.Kind);
    }

    private static string SubLine(DriveSnapshot d)
    {
        if (d.Model == Missing) return Loc.T("DRIVES_NOT_CONNECTED");
        if (!d.Ready) return Loc.T("DRIVES_NO_MEDIA");
        return KindAndFormat(d);
    }

    private static string KindAndFormat(DriveSnapshot d)
    {
        var parts = new List<string>();
        parts.Add(d.Format != MediaFormat.Unknown ? Loc.T("FORMAT_" + d.Format.ToString().ToUpperInvariant()) : KindName(d.Kind));
        if (d.FileSystem.Length > 0) parts.Add(d.FileSystem);
        if (d.Bus == BusKind.Virtual) parts.Add(Loc.T("BUS_VIRTUAL"));
        return string.Join(" · ", parts);
    }

    // ------------------------------------------------------------------
    // Easter Egg: dreimal auf ein CD-Laufwerk klicken
    // ------------------------------------------------------------------

    private int _cdClicks;
    private ulong _lastCdClick;

    private void CountCupholderClicks(DriveSnapshot d)
    {
        if (!Fun.EasterEggs.Enabled || d.Kind != MediaKind.Optical)
        {
            _cdClicks = 0;
            return;
        }
        var now = Time.GetTicksMsec();
        _cdClicks = now - _lastCdClick < 1500 ? _cdClicks + 1 : 1;
        _lastCdClick = now;
        if (_cdClicks < 3) return;
        _cdClicks = 0;
        RetroDialog.Message(Host.DialogLayer, Loc.T("EGG_CUPHOLDER_TITLE"), Loc.T("EGG_CUPHOLDER_TEXT"), "warn");
    }

    // ------------------------------------------------------------------
    // Vorfuehrdaten (nur fuer Bildschirmfotos: --demo-drives)
    // ------------------------------------------------------------------

    private static List<DriveSnapshot> DemoDrives(bool withInternal)
    {
        const long GB = 1_000_000_000;
        var list = new List<DriveSnapshot>
        {
            new(@"A:\", MediaKind.Floppy, true, "SPIELE", "FAT12", 1_457_664, 312_832)
                { Bus = BusKind.Usb, Vendor = "TEAC", Model = "USB UF000x", Revision = "0.00", VolumeSerial = 0x1A2B3C4D, ClusterBytes = 512, Format = MediaFormat.Floppy35HD },
            new(@"E:\", MediaKind.Usb, true, "STICK", "FAT32", 16 * GB, 11 * GB)
                { Bus = BusKind.Usb, Vendor = "SanDisk", Model = "Ultra Fit", Revision = "1.00", VolumeSerial = 0x0042F00D, ClusterBytes = 16384 },
            new(@"F:\", MediaKind.Sd, true, "KAMERA", "exFAT", 64 * GB, 21 * GB)
                { Bus = BusKind.Sd, Vendor = "Generic", Model = "SD/MMC Reader", VolumeSerial = 0xC0FFEE00, ClusterBytes = 131072 },
            new(@"G:\", MediaKind.ExternalDisk, true, "BACKUP", "NTFS", 1000 * GB, 390 * GB)
                { Bus = BusKind.Usb, Vendor = "WD", Model = "Elements 2620", VolumeSerial = 0xBEEF1234, ClusterBytes = 4096 },
            new(@"D:\", MediaKind.Optical, true, "FLOPPYHUB_2", "UDF", 4_480 * 1024L * 1024, 0)
                { Bus = BusKind.Sata, Vendor = "HL-DT-ST", Model = "DVDRAM GH24NSD1", Format = MediaFormat.Dvd, VolumeSerial = 0x77AA0011, ClusterBytes = 2048 },
            new(@"H:\", MediaKind.Optical, false, "", "", 0, 0) { Bus = BusKind.Usb, Vendor = "ASUS", Model = "BW-16D1X-U" },
        };
        if (withInternal)
            list.Add(new(@"C:\", MediaKind.Fixed, true, "Windows-SSD", "NTFS", 1_000 * GB, 400 * GB)
                { Bus = BusKind.Nvme, Model = "NVMe SSD", VolumeSerial = 0x68761692, ClusterBytes = 4096 });
        return list;
    }
}
