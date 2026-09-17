using Floppy.Core;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using FloppyHub.App.Fun;
using FloppyHub.App.Services;
using Godot;

namespace FloppyHub.App.Ui.Views;

/// <summary>Startansicht: Laufwerk A:, was beim Einlegen passiert, Inhalt der Diskette.</summary>
public partial class DiscView : ViewBase
{
    public override string Key => "disc";

    private TextureRect _media = null!;
    private Label _driveName = null!;
    private Label _driveStatus = null!;
    private ProgressBar _usage = null!;
    private Label _usageText = null!;

    private TextureRect _planIcon = null!;
    private Label _planTitle = null!;
    private Label _planDetail = null!;
    private TextureRect _trustIcon = null!;
    private Label _trustText = null!;
    private TextureRect _cover = null!;
    private VBoxContainer _messages = null!;
    private Button _start = null!;
    private Button _release = null!;
    private Tree _files = null!;

    private bool? _lastPresent;
    private string? _lastSignature;
    private int _coverRequest;

    public DiscState? State { get; private set; }

    protected override void Build()
    {
        var root = Host.Services.DriveRoot;

        // ---- links: das Laufwerk ----
        _media = Icons.Rect("media_empty", 3f);
        _media.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _media.MouseFilter = MouseFilterEnum.Stop;
        _media.GuiInput += OnMediaClicked;
        _driveName = Ui.Label(root.Length <= 3 ? root : Loc.T("DISC_TESTFOLDER"), "TitleLabel");
        _driveName.HorizontalAlignment = HorizontalAlignment.Center;
        _driveStatus = Ui.Dim("", wrap: true);
        _driveStatus.HorizontalAlignment = HorizontalAlignment.Center;
        _usage = new ProgressBar { ShowPercentage = false, MaxValue = 1, CustomMinimumSize = new Vector2(0, 18) };
        _usageText = Ui.Dim("");
        _usageText.HorizontalAlignment = HorizontalAlignment.Center;
        var refresh = Ui.Button(Loc.T("BTN_REFRESH"), "refresh", () => Refresh(force: true));

        var driveBox = new GroupBox(Loc.T("DISC_DRIVE"), Ui.VBox(8,
            Ui.Margin(_media, 0, 6, 0, 0), _driveName, _driveStatus, _usage, _usageText, Ui.Spacer(false), refresh));
        driveBox.CustomMinimumSize = new Vector2(230, 0);

        // ---- rechts oben: was beim Einlegen passiert ----
        _planIcon = Icons.Rect("floppy");
        _planTitle = Ui.Label("", "TitleLabel", wrap: true);
        _planDetail = Ui.Dim("", wrap: true);
        _trustIcon = Icons.Rect("info");
        _trustText = Ui.Label("", wrap: true).Expand();
        _cover = new TextureRect
        {
            CustomMinimumSize = new Vector2(184, 86),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspect,
            Visible = false,
        };
        _messages = Ui.VBox(3);
        _start = Ui.Button(Loc.T("BTN_START_NOW"), "start", () => Host.LaunchDisc());
        _release = Ui.Button(Loc.T("BTN_RELEASE"), "trust", () => Host.LaunchDisc());

        var planText = Ui.VBox(4, _planTitle, _planDetail, Ui.HBox(5, _trustIcon, _trustText)).Expand();
        var planBox = new GroupBox(Loc.T("DISC_ON_INSERT"), Ui.VBox(10,
            Ui.HBox(12, _planIcon, planText, _cover),
            _messages,
            Ui.HBox(6, _start, _release)));

        // ---- rechts unten: Dateien ----
        _files = new Tree
        {
            Columns = 3,
            HideRoot = true,
            ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 120),
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
        };
        _files.SetColumnTitle(0, Loc.T("COL_NAME"));
        _files.SetColumnTitle(1, Loc.T("COL_SIZE"));
        _files.SetColumnTitle(2, Loc.T("COL_CHANGED"));
        _files.SetColumnExpand(0, true);
        _files.SetColumnExpand(1, false);
        _files.SetColumnExpand(2, false);
        _files.SetColumnCustomMinimumWidth(1, 90);
        _files.SetColumnCustomMinimumWidth(2, 130);
        var filesBox = new GroupBox(Loc.T("DISC_CONTENT"), _files) { SizeFlagsVertical = SizeFlags.ExpandFill };

        var right = Ui.VBox(10, planBox, filesBox).Expand(vertical: true);
        AddChild(Ui.HBox(10, driveBox, right).Expand(vertical: true));
    }

    public override void OnShown() => Refresh(force: true);
    public override void OnTick() => Refresh(force: false);

    public void Refresh(bool force)
    {
        var s = Host.Services;
        var present = DiscWatcher.IsReady(s.DriveRoot);
        var signature = present ? DiskSignature.Compute(s.DriveRoot) : DiskSignature.Empty;
        if (!force && present == _lastPresent && signature == _lastSignature) return;
        _lastPresent = present;
        _lastSignature = signature;

        State = DiscState.Read(s);
        ShowDrive(State);
        ShowPlan(State);
        ShowFiles(State);
    }

    private void ShowDrive(DiscState st)
    {
        var d = st.Drive;
        _media.Texture = Icons.Get(st.Present ? "media_floppy" : "media_empty", 3f);
        if (!st.Present)
        {
            _driveStatus.Text = Loc.T("DISC_STATUS_EMPTY");

            _usage.Value = 0;
            _usageText.Text = " ";
            return;
        }
        var label = string.IsNullOrWhiteSpace(d?.Label) ? Loc.T("DISC_NO_LABEL") : d!.Label;
        _driveStatus.Text = Loc.T("DISC_STATUS_READY", label, d?.FileSystem ?? "?");
        if (EasterEggs.IsOfficialDisc(d?.Label))
        {
            _media.Texture = Icons.Get("app", 3f);
            _driveStatus.Text = Loc.T("EGG_OFFICIAL_DISC");
        }
        _usage.Value = d?.UsedRatio ?? 0;
        _usageText.Text = d is null ? "" : Loc.T("DISC_USAGE", Ui.Bytes(d.UsedBytes), Ui.Bytes(d.TotalBytes));
    }

    // Easter Egg: fuenfmal schnell auf die grosse Diskette klicken
    private int _mediaClicks;
    private ulong _lastMediaClick;

    private void OnMediaClicked(InputEvent e)
    {
        if (!EasterEggs.Enabled || e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
        var now = Time.GetTicksMsec();
        _mediaClicks = now - _lastMediaClick < 1500 ? _mediaClicks + 1 : 1;
        _lastMediaClick = now;
        if (_mediaClicks < 5) return;

        _mediaClicks = 0;
        var y = _media.Position.Y;
        var tween = CreateTween();
        tween.TweenProperty(_media, "position:y", y - 22, 0.12).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_media, "position:y", y, 0.5).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
        Host.SetStatusMessage(Loc.T("EGG_EJECT"), "floppy_small");
    }

    private void ShowPlan(DiscState st)
    {
        _messages.ClearChildren();
        _cover.Visible = false;
        _release.Visible = false;
        _start.Disabled = true;

        if (!st.Present)
        {
            SetPlan("media_empty", Loc.T("DISC_NONE_TITLE"), Loc.T("DISC_NONE_TEXT", st.Root), "info", Loc.T("DISC_NONE_HINT"));
            return;
        }

        foreach (var m in (st.Result?.Messages ?? []).Where(m => m.Level != MessageLevel.Ok))
        {
            var icon = m.Level switch { MessageLevel.Error => "error", MessageLevel.Warn => "warn", MessageLevel.Ok => "ok", _ => "info" };
            _messages.AddChild(Ui.IconLine(icon, Loc.Message(m), "DimLabel"));
        }

        var plan = st.Plan;
        var decision = st.Decision;
        if (plan is null || decision is null)
        {
            var rejected = st.Result?.HasErrors == true;
            SetPlan(rejected ? "error" : "floppy",
                rejected ? Loc.T("DISC_REJECTED_TITLE") : Loc.T("DISC_NOTHING"),
                rejected ? Loc.T("DISC_REJECTED_TEXT") : Loc.T("DISC_NOTHING_TEXT"),
                "info", Loc.T("DISC_NOTHING_HINT"));
            return;
        }

        var name = st.DisplayName(Host.Library);
        _start.Disabled = false;
        switch (plan.Kind)
        {
            case LaunchKind.Steam:
                SetPlan("kind_steam", name, Loc.T("PLAN_STEAM", plan.SteamId), "ok", Loc.T("TRUST_STEAM"));
                LoadCover(plan.SteamId!);
                break;

            case LaunchKind.Hub:
                SetPlan("kind_hub", Loc.T("KIND_HUB"), Loc.T("PLAN_HUB"), "ok", Loc.T("TRUST_HUB"));
                _start.Disabled = true;
                break;

            case LaunchKind.Game:
                var pack = Floppy.Core.Minigame.LevelPack.Load(plan.Candidates[0]);
                SetPlan("game", name, Loc.T("PLAN_GAME", pack.Levels.Count), "ok", Loc.T("TRUST_GAME"));
                break;

            default:
                if (plan.Candidates.Count > 1)
                {
                    SetPlan("exe", name, string.Join("\n", plan.Candidates.Take(6)), "warn", Loc.T("TRUST_MULTIPLE"));
                    break;
                }
                var target = plan.Candidates[0];
                var onDisc = PathRules.IsUnder(target, st.Root);
                var detail = Loc.T(onDisc ? "PLAN_RUN" : "PLAN_PCRUN", target) +
                             (string.IsNullOrWhiteSpace(plan.Arguments) ? "" : "\n" + Loc.T("PLAN_ARGS", plan.Arguments));
                switch (decision.Trust)
                {
                    case TrustState.Trusted:
                        SetPlan(onDisc ? "kind_run" : "kind_pcrun", name, detail, "trust", Loc.T("TRUST_TRUSTED"));
                        break;
                    case TrustState.Changed:
                        SetPlan(onDisc ? "kind_run" : "kind_pcrun", name, detail, "warn", Loc.T("TRUST_CHANGED"));
                        _release.Visible = true;
                        break;
                    default:
                        SetPlan(onDisc ? "kind_run" : "kind_pcrun", name, detail, "warn", Loc.T("TRUST_UNKNOWN"));
                        _release.Visible = true;
                        break;
                }
                break;
        }
    }

    private void SetPlan(string icon, string title, string detail, string trustIcon, string trustText)
    {
        _planIcon.Texture = Icons.GetSized(icon, 32);
        _planIcon.CustomMinimumSize = new Vector2(32, 32);
        _planTitle.Text = title;
        _planDetail.Text = detail;
        _trustIcon.Texture = Icons.Get(trustIcon);
        _trustText.Text = trustText;
    }

    private async void LoadCover(string appId)
    {
        var request = ++_coverRequest;
        var tex = await Host.Covers.GetAsync(appId, Host.Services.Settings.LoadCovers);
        if (request != _coverRequest || !IsInstanceValid(this)) return;
        _cover.Texture = tex;
        _cover.Visible = tex is not null;
    }

    private void ShowFiles(DiscState st)
    {
        _files.Clear();
        var root = _files.CreateItem();
        foreach (var e in st.Entries)
        {
            var item = _files.CreateItem(root);
            var isDir = e is DirectoryInfo;
            var isExe = !isDir && PathRules.HasExecutableExtension(e.FullName, Host.Services.Options.ExecutableExtensions);
            item.SetIcon(0, Icons.Get(isDir ? "folder" : isExe ? "exe" : "file"));
            item.SetText(0, e.Name);
            item.SetText(1, e is FileInfo f ? Ui.Bytes(f.Length) : "");
            item.SetTextAlignment(1, HorizontalAlignment.Right);
            item.SetText(2, e.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
        }
        if (st.Present && st.Entries.Count == 0)
            _files.CreateItem(root).SetText(0, Loc.T("DISC_EMPTY_FILES"));
    }
}
