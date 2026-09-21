using Floppy.Chat.Client;
using Floppy.Core.Chat;
using FloppyChat.Mobile.Services;
using FloppyChat.Mobile.Ui;

namespace FloppyChat.Mobile.Pages;

/// <summary>Raumwahl: Offener Chat, eigener Raum per Verschluesselung, dazu die eigene ID und die Einstellungen.</summary>
internal sealed class ConnectPage : ContentPage
{
    private readonly Field _secret;
    private readonly Label _info;
    private readonly Label _id;
    private bool _busy;

    public ConnectPage()
    {
        Kit.Prepare(this);
        var chat = AppHost.Chat;

        var title = new HeaderBar(Loc.T("M_APP_TITLE"));
        title.Right.Add(Kit.ToolButton("ico_settings", () => _ = Open(new SettingsPage()), Loc.T("M_SETTINGS")));

        // ---- Offener Chat ----
        var open = Kit.Panel(Loc.T("CHAT_OPEN_ROOM"),
            Kit.Dim(Loc.T("CHAT_OPEN_HINT")),
            new BevelButton(Loc.T("M_BTN_ENTER_OPEN"), () => _ = EnterOpen(), primary: true, icon: "ico_chat"));

        // ---- Privater Raum ----
        _secret = new Field(Loc.T("CHAT_PLACEHOLDER_SECRET"), ChatRoomKey.MaxSecretLength);
        _secret.Entry.IsPassword = true;
        _secret.Entry.Keyboard = Keyboard.Plain;
        _secret.Entry.IsTextPredictionEnabled = false;
        _secret.Entry.IsSpellCheckEnabled = false;
        _secret.Entry.ReturnType = ReturnType.Go;
        _secret.Entry.Completed += (_, _) => _ = EnterPrivate();
        _secret.Entry.TextChanged += (_, _) => UpdateInfo();

        var show = new CheckBox();
        show.CheckedChanged += (_, e) => _secret.Entry.IsPassword = !e.Value;
        var showRow = new HorizontalStackLayout { Spacing = 2, Children = { show, Kit.Text(Loc.T("CHAT_SHOW_SECRET"), 14).Also(l => l.VerticalOptions = LayoutOptions.Center) } };

        _info = Kit.Text("", 13);
        _info.IsVisible = false;

        var twoButtons = new Grid { ColumnSpacing = 8 };
        twoButtons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        twoButtons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        twoButtons.Add(new BevelButton(Loc.T("CHAT_BTN_COPY"), () => _ = CopyKey()), 0, 0);
        twoButtons.Add(new BevelButton(Loc.T("M_BTN_SHARE"), () => _ = ShareKey()), 1, 0);

        var privateRoom = Kit.Panel(Loc.T("M_PRIVATE_ROOM"),
            Kit.Dim(Loc.T("M_PRIVATE_HINT")),
            _secret,
            showRow,
            _info,
            new BevelButton(Loc.T("CHAT_BTN_CONNECT"), () => _ = EnterPrivate(), primary: true, icon: "ico_key"),
            new BevelButton(Loc.T("CHAT_BTN_NEW_KEY"), NewKey),
            twoButtons);

        // ---- Meine ID ----
        _id = Kit.Text(chat.Identity.Id, 22, bold: true);
        _id.FontFamily = "monospace";
        var myId = Kit.Panel(Loc.T("CHAT_MY_ID"),
            _id,
            Kit.Dim(Loc.T("CHAT_MY_ID_HINT")),
            new BevelButton(Loc.T("CHAT_BTN_COPY"), () => _ = CopyId()));

        var stack = new VerticalStackLayout { Padding = new Thickness(12), Spacing = 12, Children = { open, privateRoom, myId } };
        if (chat.IdentityWarning is not null) stack.Add(Kit.Text(Loc.T("M_ID_UNSAFE"), 12, tone: Theme.Warn));
        stack.Add(Kit.Dim(Loc.T("M_ABOUT_TEXT", AppInfo.Current.VersionString), 11));

        Content = Kit.Screen(title, new ScrollView { Content = stack });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _busy = false;
        _id.Text = AppHost.Chat.Identity.Id;
    }

    // ------------------------------------------------------------------

    private async Task Open(Page page)
    {
        if (_busy) return;
        _busy = true;
        try { await Navigation.PushAsync(page); }
        finally { _busy = false; }
    }

    private async Task EnterOpen()
    {
        if (_busy) return;
        AppHost.Chat.ConnectOpen();
        await Open(new ChatPage());
    }

    private async Task EnterPrivate()
    {
        if (_busy) return;
        var secret = _secret.Entry.Text ?? "";
        if (ChatClient.SecretProblemText(ChatRoomKey.Problem(secret)) is { } problem)
        {
            Info(problem, Theme.Warn);
            return;
        }

        AppHost.Chat.Connect(secret);
        _secret.Entry.Text = "";   // nie stehen lassen: weder zum Mitlesen noch zum Verwechseln mit einer Nachricht
        Info(null, Theme.TextDim);
        await Open(new ChatPage());
    }

    private void NewKey()
    {
        _secret.Entry.Text = ChatRoomKey.Generate();
        _secret.Entry.IsPassword = false;   // sichtbar, damit man sie weitergeben kann
        Info(Loc.T("M_KEY_CREATED"), Theme.Ok);
    }

    private async Task CopyKey()
    {
        if (CurrentSecret() is not { } secret) return;
        await Clipboard.Default.SetTextAsync(secret);
        Info(Loc.T("CHAT_COPIED"), Theme.Ok);
    }

    private async Task ShareKey()
    {
        if (CurrentSecret() is not { } secret) return;
        await Share.Default.RequestAsync(new ShareTextRequest { Text = secret, Title = Loc.T("M_PRIVATE_ROOM") });
    }

    private async Task CopyId()
    {
        await Clipboard.Default.SetTextAsync(AppHost.Chat.Identity.Id);
        Info(Loc.T("CHAT_COPIED"), Theme.Ok);
    }

    /// <summary>Die eingetippte Verschluesselung, wenn sie taugt (sonst wird das Problem angezeigt).</summary>
    private string? CurrentSecret()
    {
        var secret = _secret.Entry.Text ?? "";
        if (ChatClient.SecretProblemText(ChatRoomKey.Problem(secret)) is { } problem)
        {
            Info(problem, Theme.Warn);
            return null;
        }
        return ChatRoomKey.Normalize(secret);
    }

    private void UpdateInfo()
    {
        var text = _secret.Entry.Text ?? "";
        if (text.Length > 0 && ChatRoomKey.Problem(text) == SecretProblem.None && ChatRoomKey.IsWeak(text))
            Info(Loc.T("CHAT_WEAK_KEY"), Theme.Warn);
        else
            Info(null, Theme.TextDim);
    }

    private void Info(string? text, Tone tone)
    {
        _info.IsVisible = !string.IsNullOrEmpty(text);
        _info.Text = text ?? "";
        _info.Tint(Label.TextColorProperty, tone);
    }
}
