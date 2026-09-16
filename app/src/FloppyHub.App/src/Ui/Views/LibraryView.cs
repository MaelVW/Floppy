using Floppy.Core;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using Godot;

namespace FloppyHub.App.Ui.Views;

/// <summary>Bibliothek (library.csv): Liste links, Details mit Cover rechts.</summary>
public partial class LibraryView : ViewBase
{
    public override string Key => "library";

    private LineEdit _search = null!;
    private Label _count = null!;
    private Tree _tree = null!;
    private TextureRect _cover = null!;
    private TextureRect _coverPlaceholder = null!;
    private Label _title = null!;
    private Label _kind = null!;
    private Label _value = null!;
    private Label _notes = null!;
    private Button _start = null!;
    private Button _write = null!;
    private LibraryEntry? _selected;
    private int _coverRequest;

    protected override void Build()
    {
        _search = new LineEdit { PlaceholderText = Loc.T("LIB_SEARCH"), ClearButtonEnabled = true }.Expand();
        _search.TextChanged += _ => Fill();
        _count = Ui.Dim("");
        var reload = Ui.Button(Loc.T("BTN_REFRESH"), "refresh", () => { Host.ReloadLibrary(); Fill(); });

        _tree = new Tree
        {
            Columns = 4,
            HideRoot = true,
            ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row,
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
        }.Expand(vertical: true);
        _tree.SetColumnTitle(0, Loc.T("COL_NAME"));
        _tree.SetColumnTitle(1, Loc.T("COL_KIND"));
        _tree.SetColumnTitle(2, Loc.T("COL_VALUE"));
        _tree.SetColumnTitle(3, Loc.T("COL_ADDED"));
        _tree.SetColumnExpand(0, true);
        _tree.SetColumnExpandRatio(0, 3);
        _tree.SetColumnExpand(1, false);
        _tree.SetColumnCustomMinimumWidth(1, 120);
        _tree.SetColumnExpand(2, true);
        _tree.SetColumnExpandRatio(2, 2);
        _tree.SetColumnClipContent(2, true);
        _tree.SetColumnExpand(3, false);
        _tree.SetColumnCustomMinimumWidth(3, 96);
        _tree.ItemSelected += OnSelected;
        _tree.ItemActivated += Start;

        var list = Ui.VBox(6, Ui.HBox(6, _search, reload), _tree, _count).Expand(vertical: true);

        // Details
        _cover = new TextureRect
        {
            CustomMinimumSize = new Vector2(250, 117),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        _coverPlaceholder = Icons.Rect("floppy", 2f);
        var coverFrame = Ui.Panel("FieldPanel");
        coverFrame.AddChild(_cover);
        coverFrame.AddChild(new CenterContainer().With(c => c.AddChild(_coverPlaceholder)));

        _title = Ui.Label("", "TitleLabel", wrap: true);
        _kind = Ui.Label("", "BoldLabel");
        _value = Ui.Dim("", wrap: true);
        _notes = Ui.Label("", wrap: true);
        _start = Ui.Button(Loc.T("BTN_START"), "start", Start);
        _write = Ui.Button(Loc.T("BTN_WRITE_DISC"), "write");
        _write.Disabled = true;
        _write.TooltipText = Loc.T("SOON_STAGE", 3);

        var details = new GroupBox(Loc.T("LIB_DETAILS"), Ui.VBox(8,
            coverFrame, _title, _kind, _value, _notes, Ui.Spacer(false),
            Ui.HBox(6, _start, _write),
            Ui.Dim(Loc.T("LIB_COVER_HINT"), wrap: true)));
        details.CustomMinimumSize = new Vector2(290, 0);

        AddChild(Ui.HBox(10, list, details).Expand(vertical: true));
        ShowDetails(null);
    }

    public override void OnShown() => Fill();

    private void Fill()
    {
        var filter = _search.Text.Trim();
        var entries = Host.Library
            .Where(e => filter.Length == 0 ||
                        e.Label.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                        e.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        _tree.Clear();
        var root = _tree.CreateItem();
        TreeItem? reselect = null;
        for (var i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            var item = _tree.CreateItem(root);
            item.SetIcon(0, Icons.Get(KindIcon(e.Kind)));
            item.SetText(0, e.Label);
            item.SetText(1, KindName(e.Kind));
            item.SetText(2, e.Value);
            item.SetTooltipText(2, e.Value);
            item.SetText(3, e.Added);
            item.SetMetadata(0, Array.IndexOf(Host.Library.ToArray(), e));
            if (_selected is not null && e == _selected) reselect = item;
        }
        _count.Text = Loc.T("LIB_COUNT", entries.Length, Host.Library.Count);

        if (reselect is not null) reselect.Select(0);
        else ShowDetails(null);
    }

    private void OnSelected()
    {
        var item = _tree.GetSelected();
        var index = item?.GetMetadata(0).AsInt32() ?? -1;
        ShowDetails(index >= 0 && index < Host.Library.Count ? Host.Library[index] : null);
    }

    private async void ShowDetails(LibraryEntry? e)
    {
        _selected = e;
        _cover.Texture = null;
        _coverPlaceholder.Visible = true;
        _start.Disabled = e is null || e.Kind.Equals("run", StringComparison.OrdinalIgnoreCase) || e.Kind.Equals("hub", StringComparison.OrdinalIgnoreCase);

        if (e is null)
        {
            _title.Text = Loc.T("LIB_NONE_SELECTED");
            _kind.Text = "";
            _value.Text = Host.Library.Count == 0 ? Loc.T("LIB_EMPTY") : "";
            _notes.Text = "";
            return;
        }

        _title.Text = e.Label;
        _kind.Text = KindName(e.Kind);
        _value.Text = e.Value;
        _notes.Text = e.Notes;

        if (!e.Kind.Equals("steam", StringComparison.OrdinalIgnoreCase)) return;
        _coverPlaceholder.Texture = Icons.Get("kind_steam", 3f);
        var request = ++_coverRequest;
        var tex = await Host.Covers.GetAsync(e.Value.Trim(), Host.Services.Settings.LoadCovers);
        if (request != _coverRequest || !IsInstanceValid(this)) return;
        _cover.Texture = tex;
        _coverPlaceholder.Visible = tex is null;
    }

    private void Start()
    {
        var e = _selected;
        if (e is null) return;
        switch (e.Kind.ToLowerInvariant())
        {
            case "steam" when SteamApps.TryResolveAppId(e.Value, out var id):
                Starter.OpenSteam(id);
                Host.Services.Log.Write(LogLevel.Ok, $"Bibliothek: starte Steam-Spiel {id} ({e.Label}).");
                Host.SetStatusMessage(Loc.T("STATUS_STEAM_STARTED", e.Label), "kind_steam");
                break;
            case "pcrun":
                var planner = Host.Services.Planner;
                var messages = new List<PlanMessage>();
                var path = planner.ResolvePcPath(e.Value, messages);
                if (path is null)
                {
                    RetroDialog.Message(Host.DialogLayer, Loc.T("LIB_START_FAILED"), string.Join("\n", messages.Select(m => m.Text)), "error");
                    return;
                }
                Host.LaunchProgram(path, null, e.Label);
                break;
        }
    }

    public static string KindIcon(string kind) => kind.ToLowerInvariant() switch
    {
        "steam" => "kind_steam",
        "pcrun" => "kind_pcrun",
        "run" => "kind_run",
        "hub" => "kind_hub",
        _ => "file",
    };

    public static string KindName(string kind) => kind.ToLowerInvariant() switch
    {
        "steam" => Loc.T("KIND_STEAM"),
        "pcrun" => Loc.T("KIND_PCRUN"),
        "run" => Loc.T("KIND_RUN"),
        "hub" => Loc.T("KIND_HUB"),
        _ => kind,
    };
}
