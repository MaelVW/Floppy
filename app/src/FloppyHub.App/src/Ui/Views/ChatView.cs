using Floppy.Core;
using Floppy.Core.Chat;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using FloppyHub.App.Services;
using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui.Views;

/// <summary>
/// Chat: erst "Bitte gib eine Verschluesselung ein", danach wird automatisch verbunden.
/// Rechts Teilnehmer, Kontakte und die eigene ID. Der Verlauf lebt nur im Speicher.
/// </summary>
public partial class ChatView : ViewBase
{
    public override string Key => "chat";

    private static readonly string[] LightColors = ["#546e7a", "#b33a3a", "#2e8b3e", "#8e44ad", "#b86200", "#00838f", "#6d4c41", "#ad1457"];
    private static readonly string[] DarkColors = ["#9fb3c8", "#ff7a7a", "#6fd37f", "#c38bff", "#ffae4a", "#4fd6e0", "#d4ae98", "#ff7ab0"];

    private ChatService Chat => Host.Services.Chat;

    private TextureRect _led = null!;
    private Label _roomName = null!;
    private Label _roomState = null!;
    private Label _check = null!;
    private Label _openBadge = null!;
    private Button _local = null!;
    private Button _scores = null!;
    private Button _leave = null!;

    private PanelContainer _banner = null!;
    private Label _bannerText = null!;
    private Button _bannerYes = null!;
    private Button _bannerNo = null!;

    private RichTextLabel _log = null!;
    private ChatSession? _renderedSession;
    private int _renderedCount;
    private ChatLine? _renderedLast;
    private string _renderedTail = "";

    private LineEdit _input = null!;
    private CheckBox _showSecret = null!;
    private Button _send = null!;
    private Label _inputHint = null!;
    private HBoxContainer _keyTools = null!;
    private bool? _wasAsking;

    private GroupBox _membersBox = null!;
    private Tree _members = null!;
    private Button _saveContact = null!;
    private Button _challengeChess = null!;
    private Button _ban = null!;
    private Button _moderation = null!;
    private HBoxContainer _adminRow = null!;
    private Tree _contacts = null!;
    private Button _connectContact = null!;
    private Button _removeContact = null!;
    private Label _myId = null!;

    private FileDialog? _dialog;

    protected override void Build()
    {
        // ---- Raum-Leiste ----
        _led = Icons.Rect("led_off");
        _roomName = Ui.Label("", "BoldLabel");
        _openBadge = Ui.Label(Loc.T("CHAT_OPEN_BADGE"), "BoldLabel");
        _openBadge.AddThemeColorOverride("font_color", Palette.Current.Warn);
        _openBadge.TooltipText = Loc.T("CHAT_OPEN_HINT");
        _openBadge.MouseFilter = MouseFilterEnum.Pass;
        _roomState = Ui.Dim("");
        _check = Ui.Dim("");
        _check.TooltipText = Loc.T("CHAT_CHECK_TIP");
        _check.MouseFilter = MouseFilterEnum.Pass;
        var lockIcon = Icons.Rect("key", 0.75f);
        lockIcon.TooltipText = Loc.T("CHAT_ENCRYPTED_TIP");
        lockIcon.MouseFilter = MouseFilterEnum.Pass;

        _local = Ui.Button(Loc.T("CHAT_BTN_LOCAL"), "lan", ProposeLocal);
        _scores = Ui.Button(Loc.T("CHAT_BTN_SCORES"), "trophy", () => LeaderboardDialog.Open(Host));
        _leave = Ui.Button(Loc.T("CHAT_BTN_LEAVE"), "door", LeaveOrReset);

        var titleBlock = Ui.VBox(0,
            Ui.HBox(8, _roomName, _openBadge),
            Ui.HBox(10, _roomState, lockIcon, _check));
        AddChild(Ui.Panel("RaisedPanel", Ui.HBox(8, _led, titleBlock.Expand(), _local, _scores, _leave)));

        // ---- Wechsel-Vorschlag ----
        _bannerText = Ui.Label("", wrap: true).Expand();
        _bannerYes = Ui.Button("", "ok", () => AnswerBanner(true));
        _bannerNo = Ui.Button("", "error", () => AnswerBanner(false));
        _banner = Ui.Panel("HintPanel", Ui.HBox(8, Icons.Rect("lan"), _bannerText, _bannerYes, _bannerNo));
        _banner.Visible = false;
        AddChild(_banner);

        // ---- Verlauf + Eingabe ----
        _log = new RichTextLabel
        {
            ScrollFollowing = true,
            SelectionEnabled = true,
            ContextMenuEnabled = true,
            BbcodeEnabled = false,
            FocusMode = FocusModeEnum.Click,
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
            CustomMinimumSize = new Vector2(300, 160),
        }.Expand(vertical: true);

        _input = new LineEdit { MaxLength = ChatRoomKey.MaxSecretLength, ClearButtonEnabled = true }.Expand();
        _input.TextSubmitted += _ => Submit();
        _showSecret = new CheckBox { Text = Loc.T("CHAT_SHOW_SECRET"), FocusMode = FocusModeEnum.None };
        _showSecret.Toggled += on => _input.Secret = !on && IsAsking;
        _send = Ui.Button(Loc.T("CHAT_BTN_CONNECT"), "key", Submit);
        _send.CustomMinimumSize = new Vector2(100, 0);
        _inputHint = Ui.Dim("", wrap: true);

        var open = Ui.Button(Loc.T("CHAT_BTN_OPEN"), "warn", () => ShowBanned(Chat.ConnectOpen()));
        open.TooltipText = Loc.T("CHAT_OPEN_HINT");
        open.Visible = ChatRoomKey.OpenRoomAvailable;
        _keyTools = Ui.HBox(6,
            Ui.Button(Loc.T("CHAT_BTN_LOAD_KEY"), "floppy_small", LoadKey),
            Ui.Button(Loc.T("CHAT_BTN_NEW_KEY"), "key", NewKey),
            Ui.Button(Loc.T("CHAT_BTN_SAVE_KEY"), "write", () => SaveKey(IsAsking ? _input.Text : Chat.RoomSecret)),
            Ui.Spacer(),
            open);

        var left = Ui.VBox(6, _log, Ui.HBox(6, _input, _showSecret, _send), _inputHint, _keyTools);

        // ---- rechts: Raum, Kontakte, eigene ID ----
        _members = MakeTree(2);
        _members.SetColumnExpand(1, false);
        _members.SetColumnCustomMinimumWidth(1, 54);
        _members.ItemSelected += UpdateButtons;
        _members.ItemActivated += SaveContactFromSelection;
        _saveContact = Ui.Button(Loc.T("CHAT_BTN_SAVE_CONTACT"), "contact", SaveContactFromSelection);
        _challengeChess = Ui.Button(Loc.T("CHAT_BTN_CHALLENGE"), "chess", ChallengeSelectedMember);
        _ban = Ui.Button(Loc.T("CHAT_BTN_BAN"), "error", BanSelectedMember);
        _ban.TooltipText = Loc.T("CHAT_BTN_BAN_TIP");
        _moderation = Ui.Button(Loc.T("CHAT_BTN_MODERATION"), "trust", OpenModeration);
        _adminRow = Ui.HBox(6, _ban, _moderation);
        _membersBox = new GroupBox(Loc.T("CHAT_MEMBERS", 0), Ui.VBox(6, _members, Ui.HBox(6, _saveContact, _challengeChess), _adminRow));
        _membersBox.SizeFlagsVertical = SizeFlags.ExpandFill;

        _contacts = MakeTree(2);
        _contacts.SetColumnExpand(1, false);
        _contacts.SetColumnCustomMinimumWidth(1, 70);
        _contacts.ItemSelected += UpdateButtons;
        _contacts.ItemActivated += ConnectSelectedContact;
        _connectContact = Ui.Button(Loc.T("CHAT_BTN_CONNECT_CONTACT"), "key", ConnectSelectedContact);
        _removeContact = Ui.Button(Loc.T("CHAT_BTN_REMOVE_CONTACT"), "remove", RemoveSelectedContact);
        var contactsBox = new GroupBox(Loc.T("CHAT_CONTACTS"), Ui.VBox(6, _contacts, Ui.HBox(6, _connectContact, _removeContact)));
        contactsBox.SizeFlagsVertical = SizeFlags.ExpandFill;

        _myId = Ui.Label("", "BoldLabel");
        _myId.MouseFilter = MouseFilterEnum.Pass;
        _myId.TooltipText = Loc.T("CHAT_MY_ID_HINT");
        var copy = Ui.Button(Loc.T("CHAT_BTN_COPY"), null, () =>
        {
            DisplayServer.ClipboardSet(Chat.Identity.Id);
            Host.SetStatusMessage(Loc.T("CHAT_COPIED"), "ok");
        });
        var idBox = new GroupBox(Loc.T("CHAT_MY_ID"), Ui.HBox(6, Icons.Rect("contact"), _myId.Expand(), copy));

        var right = Ui.VBox(8, _membersBox, contactsBox, idBox);
        right.CustomMinimumSize = new Vector2(270, 0);

        AddChild(Ui.HBox(10, left.Expand(vertical: true), right).Expand(vertical: true));

        var clock = new Timer { WaitTime = 0.25, Autostart = true };
        clock.Timeout += () =>
        {
            if (IsVisibleInTree()) UpdateBanner();
        };
        AddChild(clock);

        Chat.Changed += OnChatChanged;
    }

    public override void _ExitTree() => Chat.Changed -= OnChatChanged;

    public override void OnShown()
    {
        Refresh();
        _input.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void OnChatChanged()
    {
        if (IsInsideTree() && IsVisibleInTree()) Refresh();
    }

    private static Tree MakeTree(int columns)
    {
        var tree = new Tree
        {
            Columns = columns,
            HideRoot = true,
            SelectMode = Tree.SelectModeEnum.Row,
            CustomMinimumSize = new Vector2(0, 90),
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
        };
        tree.SizeFlagsVertical = SizeFlags.ExpandFill;
        tree.SetColumnExpand(0, true);
        return tree;
    }

    // ==================================================================
    // Anzeige
    // ==================================================================

    private bool IsAsking => !Chat.IsInRoom && !Chat.IsDeriving;

    private void Refresh()
    {
        var session = Chat.Session;
        var inRoom = Chat.IsInRoom;
        var asking = IsAsking;
        var connected = inRoom && session!.State == ChatSessionState.Connected;

        // Raum-Leiste
        _roomName.Text = Chat.RoomLabel.Length > 0 ? Chat.RoomLabel : Loc.T("VIEW_CHAT");
        _openBadge.Visible = Chat.IsOpenRoom;
        _check.Text = session is not null ? Loc.T("CHAT_CHECK", session.Room.Check) : "";
        var (state, led) = StateText(session);
        _roomState.Text = state;
        _led.Texture = Icons.Get(led);

        _local.Disabled = !(connected && session!.Mode == ChatMode.Online && session.Proposal is null);
        _scores.Disabled = !connected;
        _leave.Text = inRoom || Chat.IsDeriving ? Loc.T("CHAT_BTN_LEAVE") : Loc.T("CHAT_BTN_OTHER_ROOM");
        _leave.Disabled = session is null && !Chat.IsDeriving;

        // Eingabe: Verschluesselung oder Nachricht
        if (_wasAsking != asking)
        {
            _input.Text = "";   // nie eine Verschluesselung als Nachricht verschicken
            _showSecret.ButtonPressed = false;
            _wasAsking = asking;
        }
        _input.Secret = asking && !_showSecret.ButtonPressed;
        _input.MaxLength = asking ? ChatRoomKey.MaxSecretLength : ChatPayload.MaxTextLength;
        _input.PlaceholderText = Loc.T(asking ? "CHAT_PLACEHOLDER_SECRET" : "CHAT_PLACEHOLDER_MESSAGE");
        _input.Editable = asking || connected;
        _showSecret.Visible = asking;
        _keyTools.Visible = asking;
        _send.Text = Loc.T(asking ? "CHAT_BTN_CONNECT" : "CHAT_BTN_SEND");
        _send.Icon = Icons.Get(asking ? "key" : "send");
        _send.Disabled = !(asking || connected);
        SetHint(asking ? Loc.T("CHAT_PROMPT_HINT") : Chat.IsOpenRoom ? Loc.T("CHAT_OPEN_HINT") : "", warn: Chat.IsOpenRoom && !asking);

        _myId.Text = Chat.IAmAdmin ? $"{Loc.T("CHAT_ADMIN_TAG")} {Chat.Identity.Id}" : Chat.Identity.Id;
        _adminRow.Visible = Chat.IAmAdmin;
        RenderLog();
        FillMembers();
        FillContacts();
        UpdateBanner();
        UpdateButtons();
    }

    private (string Text, string Led) StateText(ChatSession? s)
    {
        if (Chat.IsDeriving) return (Loc.T("CHAT_STATE_DERIVING"), "led_warn");
        if (s is null) return (Loc.T("CHAT_STATE_OFFLINE"), "led_off");
        if (s.State == ChatSessionState.Ended) return (Loc.T("CHAT_STATE_ENDED"), "led_off");
        if (s.State == ChatSessionState.Connecting) return (Loc.T("CHAT_STATE_CONNECTING"), "led_warn");
        if (s.Mode == ChatMode.Local)
            return s.LocalPeers > 0 ? (Loc.T("CHAT_STATE_LOCAL", s.LocalPeers), "led_on") : (Loc.T("CHAT_STATE_LOCAL_WAITING"), "led_warn");
        return s.Link == ChatLinkState.Reconnecting
            ? (Loc.T("CHAT_STATE_RECONNECTING"), "led_warn")
            : (Loc.T("CHAT_STATE_ONLINE", s.ServiceName), "led_on");
    }

    private void SetHint(string text, bool warn)
    {
        _inputHint.Text = text;
        _inputHint.Visible = text.Length > 0;
        if (warn) _inputHint.AddThemeColorOverride("font_color", Palette.Current.Warn);
        else _inputHint.RemoveThemeColorOverride("font_color");
    }

    // ---- Verlauf ----

    private void RenderLog(bool full = false)
    {
        var session = Chat.Session;
        var lines = session?.Lines ?? [];
        var tail = Chat.IsDeriving ? "deriving" : IsAsking ? "prompt" : "";

        var canAppend = !full && session == _renderedSession && tail == _renderedTail && _renderedCount <= lines.Count &&
                        (_renderedCount == 0 || ReferenceEquals(lines[_renderedCount - 1], _renderedLast));
        if (!canAppend)
        {
            _log.Clear();
            _renderedCount = 0;
        }
        else if (_renderedCount == lines.Count)
        {
            return;
        }

        for (var i = _renderedCount; i < lines.Count; i++) AppendLine(lines[i]);
        _renderedCount = lines.Count;
        _renderedLast = lines.Count > 0 ? lines[^1] : null;
        _renderedSession = session;
        _renderedTail = tail;

        if (!canAppend && tail.Length > 0)
            AppendAppLine(Loc.T(tail == "deriving" ? "CHAT_STATE_DERIVING" : "CHAT_PROMPT"), highlight: tail == "prompt");
    }

    private void AppendLine(ChatLine line)
    {
        var p = Palette.Current;
        _log.PushColor(p.TextDim);
        _log.AddText(line.Time.ToLocalTime().ToString("HH:mm") + "  ");
        _log.Pop();

        switch (line.Kind)
        {
            case ChatLineKind.Mine or ChatLineKind.Theirs:
                _log.PushColor(ColorOf(line.Fingerprint));
                _log.PushBold();
                _log.AddText(Chat.NameOf(line.Fingerprint, line.MemberId));
                _log.Pop();
                _log.Pop();
                _log.AddText(": " + line.Text);   // AddText: kein BBCode, fremder Text kann nichts formatieren
                break;
            case ChatLineKind.Warning:
                _log.PushColor(p.Warn);
                _log.AddText("▲ " + NoticeText(line));
                _log.Pop();
                break;
            default:
                _log.PushColor(p.TextDim);
                _log.AddText("• " + NoticeText(line));
                _log.Pop();
                break;
        }
        _log.Newline();
    }

    private void AppendAppLine(string text, bool highlight)
    {
        var p = Palette.Current;
        _log.PushColor(p.TextDim);
        _log.AddText(DateTime.Now.ToString("HH:mm") + "  ");
        _log.Pop();
        _log.PushColor(highlight ? p.Accent : p.TextDim);
        _log.PushBold();
        _log.AddText(Loc.T("CHAT_APP") + ": ");
        _log.Pop();
        _log.AddText(text);
        _log.Pop();
        _log.Newline();
    }

    private string NoticeText(ChatLine line)
    {
        var args = new List<object?> { Chat.NameOf(line.Fingerprint, line.MemberId) };
        if (line.Args is not null) args.AddRange(line.Args);
        return Loc.T("CHAT_N_" + line.Text.ToUpperInvariant(), args.ToArray());
    }

    private Color ColorOf(string? fingerprint)
    {
        var p = Palette.Current;
        if (fingerprint is null || fingerprint == Chat.Identity.Fingerprint) return p.Accent;
        var colors = p.Dark ? DarkColors : LightColors;
        var index = Convert.ToInt32(fingerprint[..2], 16) % colors.Length;
        return new Color(colors[index]);
    }

    // ---- Teilnehmer + Kontakte ----

    private void FillMembers()
    {
        var selected = _members.GetSelected()?.GetMetadata(0).AsString();
        _members.Clear();
        var root = _members.CreateItem();
        var members = Chat.IsInRoom ? Chat.Session!.Members : [];
        foreach (var m in members)
        {
            var item = _members.CreateItem(root);
            var contact = Chat.ContactOf(m.Fingerprint);
            item.SetText(0, Chat.NameOf(m.Fingerprint, m.Id));
            item.SetIcon(0, Icons.Get(contact is not null ? "contact" : m.IsSelf ? "kind_hub" : "led_on"));
            item.SetCustomColor(0, ColorOf(m.Fingerprint));
            item.SetText(1, m.IsSelf ? "" : Loc.T(m.Mode == ChatMode.Local ? "CHAT_MODE_LOCAL" : "CHAT_MODE_ONLINE"));
            item.SetTooltipText(0, Loc.T("CHAT_ID", m.Id) + (contact is not null ? $"\n{contact.Name}" : ""));
            item.SetMetadata(0, m.Fingerprint);
            item.SetMetadata(1, m.Id);
            if (m.Fingerprint == selected) item.Select(0);
        }
        _membersBox.Title = Loc.T("CHAT_MEMBERS", members.Count);
    }

    private void FillContacts()
    {
        var selected = _contacts.GetSelected()?.GetMetadata(0).AsString();
        _contacts.Clear();
        var root = _contacts.CreateItem();
        var inRoom = Chat.IsInRoom ? Chat.Session!.Members.Select(m => m.Fingerprint).ToHashSet() : [];
        foreach (var c in Chat.Contacts)
        {
            var item = _contacts.CreateItem(root);
            item.SetText(0, c.Name);
            item.SetIcon(0, Icons.Get(c.HasSecret ? "key" : "contact"));
            item.SetText(1, ChatIdentity.ShortId(c.MemberId));
            item.SetTooltipText(0, $"{Loc.T("CHAT_ID", c.MemberId)}\n{Loc.T(c.HasSecret ? "CHAT_CONTACT_KEY_TIP" : "CHAT_CONTACT_NO_KEY_TIP")}");
            if (inRoom.Contains(c.Fingerprint)) item.SetCustomColor(0, Palette.Current.Ok);
            item.SetMetadata(0, c.Fingerprint);
            if (c.Fingerprint == selected) item.Select(0);
        }
        if (Chat.Contacts.Count == 0)
        {
            var empty = _contacts.CreateItem(root);
            empty.SetText(0, Loc.T("CHAT_CONTACTS_EMPTY"));
            empty.SetSelectable(0, false);
            empty.SetSelectable(1, false);
        }
    }

    private void UpdateButtons()
    {
        var member = _members.GetSelected()?.GetMetadata(0).AsString();
        _saveContact.Disabled = string.IsNullOrEmpty(member) || member == Chat.Identity.Fingerprint || Host.Services.ReadOnlyMode;
        _challengeChess.Disabled = string.IsNullOrEmpty(member) || member == Chat.Identity.Fingerprint
            || Chat.Session?.Chess is { Stage: not ChessGameStage.Ended };
        _ban.Disabled = Chat.Session is not { State: ChatSessionState.Connected, Mode: ChatMode.Online, CanModerate: true }
            || string.IsNullOrEmpty(member) || member == Chat.Identity.Fingerprint || Chat.IsAdmin(member);
        _moderation.Disabled = !Chat.IAmAdmin;
        var contact = Chat.ContactOf(_contacts.GetSelected()?.GetMetadata(0).AsString());
        _connectContact.Disabled = contact is not { HasSecret: true };
        _removeContact.Disabled = contact is null;
    }

    private void ChallengeSelectedMember()
    {
        var id = _members.GetSelected()?.GetMetadata(1).AsString();
        if (string.IsNullOrEmpty(id) || Chat.Session is not { } session) return;
        var result = session.ChallengeChess(id, DateTimeOffset.UtcNow);
        if (result == ChatResult.Ok) Host.ShowView("chess");
        else ShowResult(result);
        Refresh();
    }

    // ---- Moderation (nur Admin) ----

    private void BanSelectedMember()
    {
        var item = _members.GetSelected();
        var fingerprint = item?.GetMetadata(0).AsString();
        if (string.IsNullOrEmpty(fingerprint)) return;
        OpenBanDialog(fingerprint, item!.GetMetadata(1).AsString());
    }

    public void OpenBanDialog(string fingerprint, string memberId)
    {
        if (Chat.Session is not { CanModerate: true } session) return;
        var name = Chat.NameOf(fingerprint, memberId);

        TimeSpan?[] lengths = [TimeSpan.FromHours(1), TimeSpan.FromDays(1), TimeSpan.FromDays(7), null];
        var duration = new OptionButton();
        foreach (var key in new[] { "CHAT_BAN_1H", "CHAT_BAN_24H", "CHAT_BAN_7D", "CHAT_BAN_PERMANENT" }) duration.AddItem(Loc.T(key));
        duration.Select(1);
        var reason = new LineEdit { MaxLength = ChatPayload.MaxBanReasonLength, PlaceholderText = Loc.T("CHAT_BAN_REASON_HINT") };

        var d = new RetroDialog(Loc.T("CHAT_BAN_TITLE"), "warn", 500);
        d.Body.AddChild(Ui.HBox(12, Icons.Rect("warn", 2f), Ui.VBox(8,
            Ui.Label(Loc.T("CHAT_BAN_TEXT", name), wrap: true),
            Ui.HBox(8, Ui.Label(Loc.T("CHAT_BAN_REASON")), reason.Expand()),
            Ui.HBox(8, Ui.Label(Loc.T("CHAT_BAN_DURATION")), duration.Expand()),
            Ui.Dim(Loc.T("CHAT_BAN_NOTE"), wrap: true)).Expand()));
        d.AddButton(Loc.T("CHAT_BAN_CONFIRM"), () =>
        {
            var result = session.BanMember(fingerprint, reason.Text, lengths[duration.Selected], DateTimeOffset.UtcNow);
            if (result == ChatResult.Ok) Host.SetStatusMessage(Loc.T("CHAT_BAN_DONE", name), "ok");
            else ShowResult(result);
            _renderedSession = null;   // ausgeblendete Nachrichten: Verlauf neu zeichnen
            Refresh();
        }, icon: "error");
        d.AddButton(Loc.T("BTN_CANCEL"), () => { });
        d.Open(Host.DialogLayer);
        Callable.From(() => reason.GrabFocus()).CallDeferred();
    }

    public void OpenModeration()
    {
        if (!Chat.IAmAdmin) return;
        var tree = new Tree
        {
            Columns = 3,
            HideRoot = true,
            ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row,
            CustomMinimumSize = new Vector2(0, 190),
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
        };
        tree.SetColumnTitle(0, Loc.T("CHAT_MOD_COL_ID"));
        tree.SetColumnTitle(1, Loc.T("CHAT_MOD_COL_REASON"));
        tree.SetColumnTitle(2, Loc.T("CHAT_MOD_COL_UNTIL"));
        tree.SetColumnExpand(0, false);
        tree.SetColumnCustomMinimumWidth(0, 130);
        tree.SetColumnExpand(1, true);
        tree.SetColumnExpand(2, false);
        tree.SetColumnCustomMinimumWidth(2, 130);

        void Fill()
        {
            tree.Clear();
            var root = tree.CreateItem();
            var bans = Chat.Bans.Active(DateTimeOffset.UtcNow);
            foreach (var b in bans.OrderByDescending(b => b.At))
            {
                var item = tree.CreateItem(root);
                item.SetText(0, b.MemberId.Length > 0 ? b.MemberId : $"{b.Fingerprint[..8]}…");
                item.SetText(1, b.Reason);
                item.SetText(2, b.Until == 0
                    ? Loc.T("CHAT_BAN_PERMANENT")
                    : DateTimeOffset.FromUnixTimeMilliseconds(b.Until).ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                item.SetTooltipText(0, $"{Loc.T("CHAT_ID", b.MemberId)}\n{b.Fingerprint}");
                item.SetMetadata(0, b.Fingerprint);
            }
            if (bans.Count == 0)
            {
                var none = tree.CreateItem(root);
                none.SetText(0, Loc.T("CHAT_MOD_EMPTY"));
                none.SetSelectable(0, false);
                none.SetSelectable(1, false);
                none.SetSelectable(2, false);
            }
        }
        Fill();

        var d = new RetroDialog(Loc.T("CHAT_MOD_TITLE"), "trust", 640);
        d.Body.AddChild(Ui.IconLine("info", Loc.T("CHAT_MOD_HINT"), "DimLabel"));
        d.Body.AddChild(tree);
        d.AddButton(Loc.T("CHAT_MOD_UNBAN"), () =>
        {
            var fingerprint = tree.GetSelected()?.GetMetadata(0).AsString();
            if (string.IsNullOrEmpty(fingerprint)) return;
            if (Chat.Session is not { CanModerate: true, State: ChatSessionState.Connected, Mode: ChatMode.Online } session)
            {
                Host.SetStatusMessage(Loc.T("CHAT_MOD_NEEDS_OPEN"), "warn");   // aufheben heisst: allen im offenen Chat Bescheid sagen
                return;
            }
            var result = session.UnbanMember(fingerprint, DateTimeOffset.UtcNow);
            if (result == ChatResult.Ok)
            {
                Host.SetStatusMessage(Loc.T("CHAT_MOD_UNBANNED"), "ok");
                Fill();
                Refresh();
            }
            else
            {
                ShowResult(result);
            }
        }, closes: false, icon: "ok");
        d.AddButton(Loc.T("BTN_OK"), () => { });
        d.Open(Host.DialogLayer);
    }

    // ---- Wechsel-Vorschlag ----

    private void UpdateBanner()
    {
        var session = Chat.Session;
        if (session?.Proposal is not { } p || session.State == ChatSessionState.Ended)
        {
            _banner.Visible = false;
            return;
        }
        _banner.Visible = true;
        var seconds = Math.Max(0, (int)Math.Ceiling((p.Deadline - DateTimeOffset.UtcNow).TotalSeconds));
        var name = Chat.NameOf(p.ProposerFingerprint, p.ProposerId);
        (string text, string? yes, string? no) = p.Stage switch
        {
            ProposalStage.Waiting => (Loc.T("CHAT_BANNER_WAITING", p.YesCount, p.NoCount, p.AskedCount, seconds), null, null),
            ProposalStage.Asking => (p.Wifi is { } wifi
                ? Loc.T("CHAT_BANNER_ASKING_WIFI", name, seconds, wifi)
                : Loc.T("CHAT_BANNER_ASKING", name, seconds), Loc.T("CHAT_BTN_APPROVE"), Loc.T("CHAT_BTN_DECLINE")),
            ProposalStage.Joining => (Loc.T("CHAT_BANNER_JOINING"), null, null),
            ProposalStage.Declined => (Loc.T("CHAT_BANNER_DECLINED"), null, null),
            _ => (Loc.T("CHAT_BANNER_COUNTDOWN", seconds), Loc.T("CHAT_BTN_FOLLOW"), Loc.T("CHAT_BTN_LEAVE_NOW")),
        };
        _bannerText.Text = text;
        _bannerYes.Visible = yes is not null;
        _bannerNo.Visible = no is not null;
        if (yes is not null) _bannerYes.Text = yes;
        if (no is not null) _bannerNo.Text = no;
    }

    private void AnswerBanner(bool yes)
    {
        var session = Chat.Session;
        if (session?.Proposal is not { } p) return;
        if (p.Stage == ProposalStage.Countdown && !yes)
        {
            Chat.Leave();
            return;
        }
        session.AnswerProposal(yes, DateTimeOffset.UtcNow);
        Refresh();
    }

    // ==================================================================
    // Aktionen
    // ==================================================================

    private void Submit()
    {
        var text = _input.Text;
        if (IsAsking)
        {
            var problem = ChatRoomKey.Problem(text);
            if (problem != SecretProblem.None)
            {
                SetHint(Loc.T("CHAT_SECRET_" + problem.ToString().ToUpperInvariant()), warn: true);
                _input.GrabFocus();
                return;
            }
            ConnectWith(text);
            return;
        }

        var session = Chat.Session;
        if (session is null) return;
        var result = session.SendText(text, DateTimeOffset.UtcNow);
        if (result == ChatResult.Ok) _input.Text = "";
        else if (result != ChatResult.Empty) ShowResult(result);
        _input.GrabFocus();
    }

    private void ConnectWith(string secret, string? label = null)
    {
        if (ChatRoomKey.IsWeak(secret)) Host.SetStatusMessage(Loc.T("CHAT_WEAK_KEY"), "warn");
        _input.Text = "";
        ShowBanned(Chat.Connect(secret, label ?? LabelFor(secret)));
        _input.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>Du bist aus dem offenen Chat gesperrt - Grund und Ende zeigen (bei null passiert nichts).</summary>
    private void ShowBanned(ChatBan? ban)
    {
        if (ban is null) return;
        var until = ban.Until == 0
            ? Loc.T("CHAT_BAN_PERMANENT")
            : DateTimeOffset.FromUnixTimeMilliseconds(ban.Until).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var text = ban.Reason.Length > 0
            ? Loc.T("CHAT_BANNED_TEXT_REASON", ban.Reason, until)
            : Loc.T("CHAT_BANNED_TEXT", until);
        RetroDialog.Message(Host.DialogLayer, Loc.T("CHAT_BANNED_TITLE"), text, "warn");
    }

    /// <summary>Gehoert die Verschluesselung zu einem Kontakt? Dann dessen Name als Raumname.</summary>
    private string? LabelFor(string secret)
    {
        var normalized = ChatRoomKey.Normalize(secret);
        return Chat.Contacts.FirstOrDefault(c => c.HasSecret && ChatContactBook.RevealSecret(c) == normalized)?.Name;
    }

    private void ShowResult(ChatResult result)
    {
        var text = result switch
        {
            ChatResult.TooFast => Loc.T("CHAT_RESULT_TOOFAST", ChatSession.OnlineMessagesPerMinute),
            ChatResult.TooLong => Loc.T("CHAT_RESULT_TOOLONG"),
            ChatResult.Alone => Loc.T("CHAT_RESULT_ALONE"),
            ChatResult.NoNetwork => Loc.T("CHAT_RESULT_NONETWORK"),
            ChatResult.Busy => Loc.T("CHAT_RESULT_BUSY"),
            ChatResult.NotFound => Loc.T("CHAT_RESULT_NOTFOUND"),
            ChatResult.WrongTurn => Loc.T("CHAT_RESULT_WRONGTURN"),
            ChatResult.NotAllowed => Loc.T("CHAT_RESULT_NOTALLOWED"),
            _ => Loc.T("CHAT_RESULT_NOTCONNECTED"),
        };
        Host.SetStatusMessage(text, "warn");
    }

    private void ProposeLocal()
    {
        var result = Chat.Session?.ProposeLocal(DateTimeOffset.UtcNow) ?? ChatResult.NotConnected;
        if (result != ChatResult.Ok) ShowResult(result);
        Refresh();
    }

    private void LeaveOrReset()
    {
        if (Chat.IsInRoom || Chat.IsDeriving) Chat.Leave();
        else Chat.Reset();
    }

    // ---- Kontakte ----

    private void SaveContactFromSelection()
    {
        var item = _members.GetSelected();
        var fingerprint = item?.GetMetadata(0).AsString();
        if (string.IsNullOrEmpty(fingerprint) || fingerprint == Chat.Identity.Fingerprint || Host.Services.ReadOnlyMode) return;
        var memberId = item!.GetMetadata(1).AsString();
        var existing = Chat.ContactOf(fingerprint);

        var d = new RetroDialog(Loc.T("CHAT_CONTACT_TITLE"), "contact", 460);
        var name = new LineEdit { Text = existing?.Name ?? "", MaxLength = ChatContact.MaxNameLength, PlaceholderText = Loc.T("CHAT_ID", memberId) };
        var withKey = new CheckBox
        {
            Text = Loc.T("CHAT_CONTACT_WITH_KEY"),
            ButtonPressed = Chat.RoomSecret is not null,
            Disabled = Chat.RoomSecret is null,
        };
        d.Body.AddChild(Ui.HBox(12, Icons.Rect("contact", 2f), Ui.VBox(6,
            Ui.Label(Loc.T("CHAT_CONTACT_TEXT", Loc.T("CHAT_ID", memberId)), wrap: true),
            Ui.HBox(8, Ui.Label(Loc.T("CHAT_CONTACT_NAME")), name.Expand()),
            withKey,
            Ui.Dim(Chat.RoomSecret is null ? Loc.T("CHAT_CONTACT_KEY_OPEN") : "", wrap: true)).Expand()));

        Button? save = null;
        save = d.AddButton(Loc.T("BTN_SAVE"), () =>
        {
            var clean = ChatContactBook.CleanName(name.Text);
            if (clean.Length == 0)
            {
                name.GrabFocus();
                return;
            }
            try
            {
                Chat.SaveContact(clean, memberId, fingerprint, withKey.ButtonPressed && !withKey.Disabled);
                Host.SetStatusMessage(Loc.T("CHAT_CONTACT_SAVED", clean), "contact");
            }
            catch (Exception ex)
            {
                Host.SetStatusMessage(ex.Message, "warn");
            }
            _renderedSession = null;   // Namen im Verlauf neu zeichnen
            d.Close();
            Refresh();
        }, closes: false, icon: "ok");
        d.AddButton(Loc.T("BTN_CANCEL"), () => { });
        d.Open(Host.DialogLayer);
        name.TextSubmitted += _ => save.EmitSignal(BaseButton.SignalName.Pressed);
        Callable.From(() => name.GrabFocus()).CallDeferred();
    }

    private void ConnectSelectedContact()
    {
        var contact = Chat.ContactOf(_contacts.GetSelected()?.GetMetadata(0).AsString());
        if (contact is null) return;
        var secret = ChatContactBook.RevealSecret(contact);
        if (secret is null)
        {
            RetroDialog.Message(Host.DialogLayer, Loc.T("CHAT_CONTACT_NO_KEY_TITLE"), Loc.T("CHAT_CONTACT_NO_KEY_TEXT", contact.Name), "contact");
            return;
        }
        ConnectWith(secret, contact.Name);
    }

    private void RemoveSelectedContact()
    {
        var contact = Chat.ContactOf(_contacts.GetSelected()?.GetMetadata(0).AsString());
        if (contact is null) return;
        RetroDialog.Ask(Host.DialogLayer, Loc.T("CHAT_CONTACT_REMOVE_TITLE"), Loc.T("CHAT_CONTACT_REMOVE_TEXT", contact.Name),
            Loc.T("CHAT_BTN_REMOVE_CONTACT"), () =>
            {
                Chat.RemoveContact(contact.Fingerprint);
                _renderedSession = null;
                Refresh();
            });
    }

    // ---- Schluessel auf Datentraeger ----

    private IEnumerable<string> KeyRoots()
    {
        var roots = DriveSnapshot.RemovableDrives().Where(d => d.Ready).Select(d => d.Root).ToList();
        var watched = Host.Services.DriveRoot;
        if (DiscWatcher.IsReady(watched)) roots.Add(watched);
        return roots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void LoadKey()
    {
        var found = ChatKeyFile.FindOn(KeyRoots());
        if (found.Count == 1)
        {
            ConnectWith(found[0].Secret);
            return;
        }

        var d = new RetroDialog(Loc.T("CHAT_LOADKEY_TITLE"), "key", 460);
        OptionButton? choice = null;
        if (found.Count == 0)
        {
            d.Body.AddChild(Ui.HBox(12, Icons.Rect("floppy_small", 2f), Ui.Label(Loc.T("CHAT_LOADKEY_NONE"), wrap: true).Expand()));
        }
        else
        {
            choice = new OptionButton();
            foreach (var (root, _) in found) choice.AddItem(RootLabel(root));
            d.Body.AddChild(Ui.HBox(12, Icons.Rect("key", 2f), Ui.VBox(6, Ui.Label(Loc.T("CHAT_LOADKEY_CHOOSE"), wrap: true), choice).Expand()));
            d.AddButton(Loc.T("CHAT_BTN_CONNECT"), () => ConnectWith(found[choice.Selected].Secret), icon: "key");
        }
        d.AddButton(Loc.T("CHAT_LOADKEY_FILE"), BrowseKeyFile, icon: "folder");
        d.AddButton(Loc.T("BTN_CANCEL"), () => { });
        d.Open(Host.DialogLayer);
    }

    private void BrowseKeyFile()
    {
        _dialog?.QueueFree();
        _dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Title = Loc.T("CHAT_LOADKEY_TITLE"),
            Filters = [$"{ChatKeyFile.FileName}, *.txt ; {Loc.T("CHAT_LOADKEY_FILTER")}"],
        };
        _dialog.FileSelected += path =>
        {
            var secret = ChatKeyFile.Read(path.Replace('/', '\\'));
            if (secret is null) RetroDialog.Message(Host.DialogLayer, Loc.T("CHAT_LOADKEY_TITLE"), Loc.T("CHAT_LOADKEY_INVALID"), "warn");
            else ConnectWith(secret);
        };
        AddChild(_dialog);
        _dialog.PopupCentered();
    }

    private void NewKey()
    {
        var secret = ChatRoomKey.Generate();
        var d = new RetroDialog(Loc.T("CHAT_NEWKEY_TITLE"), "key", 480);
        var field = new LineEdit { Text = secret, Editable = false, Alignment = HorizontalAlignment.Center, SelectAllOnFocus = true };
        field.AddThemeFontOverride("font", SkinBuilder.BoldFont);
        field.AddThemeFontSizeOverride("font_size", 18);
        d.Body.AddChild(Ui.HBox(12, Icons.Rect("key", 2f), Ui.VBox(8, Ui.Label(Loc.T("CHAT_NEWKEY_TEXT"), wrap: true), field).Expand()));
        d.AddButton(Loc.T("CHAT_BTN_SAVE_KEY"), () => SaveKey(secret), icon: "write");
        d.AddButton(Loc.T("CHAT_BTN_COPY"), () =>
        {
            DisplayServer.ClipboardSet(secret);
            Host.SetStatusMessage(Loc.T("CHAT_COPIED"), "ok");
        }, closes: false);
        d.AddButton(Loc.T("CHAT_BTN_USE"), () => ConnectWith(secret), icon: "ok");
        d.AddButton(Loc.T("BTN_CANCEL"), () => { });
        d.Open(Host.DialogLayer);
    }

    private void SaveKey(string? secret)
    {
        if (secret is null || ChatRoomKey.Problem(secret) != SecretProblem.None)
        {
            RetroDialog.Message(Host.DialogLayer, Loc.T("CHAT_SAVEKEY_TITLE"), Loc.T("CHAT_SAVEKEY_NOTHING"), "info");
            return;
        }
        var roots = KeyRoots().ToList();
        if (roots.Count == 0)
        {
            RetroDialog.Message(Host.DialogLayer, Loc.T("CHAT_SAVEKEY_TITLE"), Loc.T("CHAT_SAVEKEY_NONE"), "floppy_small");
            return;
        }

        var d = new RetroDialog(Loc.T("CHAT_SAVEKEY_TITLE"), "write", 480);
        var target = new OptionButton();
        foreach (var root in roots) target.AddItem(RootLabel(root));
        var replace = Ui.Dim("", wrap: true);
        void UpdateReplace() => replace.Text = File.Exists(System.IO.Path.Combine(roots[target.Selected], ChatKeyFile.FileName))
            ? Loc.T("CHAT_SAVEKEY_REPLACE") : "";
        target.ItemSelected += _ => UpdateReplace();
        UpdateReplace();

        d.Body.AddChild(Ui.HBox(12, Icons.Rect("key", 2f), Ui.VBox(8,
            Ui.Label(Loc.T("CHAT_SAVEKEY_TEXT"), wrap: true),
            Ui.HBox(8, Ui.Label(Loc.T("CHAT_SAVEKEY_TARGET")), target.Expand()),
            replace).Expand()));
        d.AddButton(Loc.T("BTN_SAVE"), () => WriteKey(roots[target.Selected], secret), icon: "write");
        d.AddButton(Loc.T("BTN_CANCEL"), () => { });
        d.Open(Host.DialogLayer);
    }

    private void WriteKey(string root, string secret)
    {
        var s = Host.Services;
        if (s.ReadOnlyMode) return;
        // Auf der beobachteten Diskette: Motor vorwarnen (eine Schluessel-Diskette ist eine normale Diskette)
        var watched = string.Equals(root.TrimEnd('\\'), s.DriveRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        if (watched) WriteGuard.Begin(s.Paths.UserData);
        try
        {
            ChatKeyFile.Write(root, secret);
            if (watched) WriteGuard.Note(s.Paths.UserData, DiskSignature.Compute(s.DriveRoot));
            Host.SetStatusMessage(Loc.T("CHAT_SAVEKEY_DONE", System.IO.Path.Combine(root, ChatKeyFile.FileName)), "ok");
        }
        catch (Exception ex)
        {
            if (watched) WriteGuard.Cancel(s.Paths.UserData);
            RetroDialog.Message(Host.DialogLayer, Loc.T("CHAT_SAVEKEY_TITLE"), Loc.T("CHAT_SAVEKEY_FAILED", ex.Message), "error");
        }
    }

    private static string RootLabel(string root)
    {
        if (root.Length > 3) return root;   // Testordner
        var d = DriveSnapshot.Read(root);
        var name = string.IsNullOrWhiteSpace(d.Label) ? Loc.T("MEDIA_" + d.Kind.ToString().ToUpperInvariant()) : $"„{d.Label}“";
        return $"{root.TrimEnd('\\')}  {name}";
    }
}
