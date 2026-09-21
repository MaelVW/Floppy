using Floppy.Chat.Client;
using Floppy.Core.Chat;
using FloppyChat.Mobile.Services;
using FloppyChat.Mobile.Ui;

namespace FloppyChat.Mobile.Pages;

/// <summary>Anzeigename, Namensfarbe, Schriftgroesse und ein paar Schalter. Alles wirkt sofort und bleibt gespeichert.</summary>
internal sealed class SettingsPage : ContentPage
{
    private static readonly ChatFontSize[] Sizes = [ChatFontSize.Small, ChatFontSize.Normal, ChatFontSize.Large, ChatFontSize.Huge];
    private static readonly string[] SizeKeys = ["CHAT_FONT_SMALL", "CHAT_FONT_NORMAL", "CHAT_FONT_LARGE", "CHAT_FONT_HUGE"];

    private readonly ChatSettings _settings = AppHost.Settings;
    private readonly ChatClient _chat = AppHost.Chat;
    private readonly Field _alias;
    private readonly Label _aliasInfo;
    private readonly Label _preview;
    private readonly Field _server;
    private readonly Label _serverInfo;
    private readonly Button[] _swatches = new Button[ChatProfile.ColorCount + 1];
    private readonly Button[] _sizeButtons = new Button[Sizes.Length];

    public SettingsPage()
    {
        Kit.Prepare(this);
        var title = new HeaderBar(Loc.T("M_SETTINGS"), () => _ = Navigation.PopAsync());

        // ---- Anzeigename ----
        _alias = new Field(Loc.T("CHAT_ALIAS_PLACEHOLDER"), 40);
        _alias.Entry.Text = _settings.Alias;
        _alias.Entry.ReturnType = ReturnType.Done;
        _alias.Entry.Completed += (_, _) => SaveAlias();
        _aliasInfo = Kit.Text("", 13);
        _aliasInfo.IsVisible = false;
        var aliasPanel = Kit.Panel(Loc.T("CHAT_ALIAS"),
            Kit.Dim(Loc.T("CHAT_CUSTOMIZE_VISIBLE")),
            _alias,
            _aliasInfo,
            Kit.Dim(Loc.T("M_ALIAS_HINT")),
            new BevelButton(Loc.T("M_BTN_SAVE"), SaveAlias, primary: true));

        // ---- Namensfarbe ----
        var swatchGrid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        for (var i = 0; i < 5; i++) swatchGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        swatchGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        swatchGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var index = 0; index <= ChatProfile.ColorCount; index++)
        {
            var color = index;
            var swatch = new Button { HeightRequest = 44, CornerRadius = 0, Padding = new Thickness(0), FontSize = 13 };
            swatch.Tint(Button.BackgroundColorProperty, color == 0 ? Theme.Face : Theme.NameTone(color));
            swatch.Clicked += (_, _) => PickColor(color);
            _swatches[index] = swatch;
            swatchGrid.Add(swatch, index % 5, index / 5);
        }
        _preview = Kit.Text("", 16);
        var colorPanel = Kit.Panel(Loc.T("CHAT_COLOR"),
            swatchGrid,
            Kit.Dim(Loc.T("CHAT_CUSTOMIZE_PREVIEW")),
            new Bevel(new VerticalStackLayout { Padding = new Thickness(10), Children = { _preview } }, sunken: true));

        // ---- Schriftgroesse ----
        var sizeGrid = new Grid { ColumnSpacing = 6 };
        for (var i = 0; i < Sizes.Length; i++)
        {
            sizeGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var size = Sizes[i];
            var button = new Button { Text = Loc.T(SizeKeys[i]), HeightRequest = 44, CornerRadius = 0, Padding = new Thickness(2, 0), FontSize = 12 };
            button.Clicked += (_, _) => PickSize(size);
            _sizeButtons[i] = button;
            sizeGrid.Add(button, i, 0);
        }
        var sizePanel = Kit.Panel(Loc.T("CHAT_FONT"), sizeGrid);

        // ---- Schalter ----
        var highlight = new Switch { IsToggled = _settings.HighlightMentions };
        highlight.Toggled += (_, e) =>
        {
            _settings.HighlightMentions = e.Value;
            _chat.RestartFeed();
        };
        var keepScreen = new Switch { IsToggled = _settings.KeepScreenOn };
        keepScreen.Toggled += (_, e) => _settings.KeepScreenOn = e.Value;
        var switches = Kit.Panel(null,
            SwitchRow(Loc.T("CHAT_HIGHLIGHT"), highlight),
            SwitchRow(Loc.T("M_KEEP_SCREEN"), keepScreen),
            Kit.Dim(Loc.T("M_KEEP_SCREEN_HINT")));

        // ---- Dienst ----
        _server = new Field("https://ntfy.sh", 200);
        _server.Entry.Text = _settings.Server;
        _server.Entry.Keyboard = Keyboard.Url;
        _server.Entry.ReturnType = ReturnType.Done;
        _server.Entry.IsSpellCheckEnabled = false;
        _server.Entry.IsTextPredictionEnabled = false;
        _server.Entry.Completed += (_, _) => SaveServer();
        _server.Entry.Unfocused += (_, _) => SaveServer();
        _serverInfo = Kit.Text("", 13);
        _serverInfo.IsVisible = false;
        var serverPanel = Kit.Panel(Loc.T("M_SERVER"), _server, _serverInfo, Kit.Dim(Loc.T("M_SERVER_HINT")));

        var about = Kit.Panel(Loc.T("M_ABOUT"), Kit.Dim(Loc.T("M_ABOUT_TEXT", AppInfo.Current.VersionString), 13));

        Content = Kit.Screen(title, new ScrollView
        {
            Content = new VerticalStackLayout { Padding = new Thickness(12), Spacing = 12, Children = { aliasPanel, colorPanel, sizePanel, switches, serverPanel, about } },
        });

        UpdateSwatches();
        UpdateSizes();
        UpdatePreview();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SaveServer();   // auch wenn man die Seite mit "zurueck" verlaesst, ohne das Feld zu verlassen
    }

    // ------------------------------------------------------------------

    private static View SwitchRow(string text, Switch toggle)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        toggle.VerticalOptions = LayoutOptions.Center;
        grid.Add(Kit.Text(text, 15).Also(l => l.VerticalOptions = LayoutOptions.Center), 0, 0);
        grid.Add(toggle, 1, 0);
        return grid;
    }

    private void SaveAlias()
    {
        var text = ChatProfile.Normalize(_alias.Entry.Text);
        var problem = text.Length == 0 ? AliasProblem.None : ChatProfile.Check(text, allowReserved: _chat.IAmAdmin);
        if (problem != AliasProblem.None)
        {
            AliasInfo(Loc.T("CHAT_ALIAS_PROBLEM_" + problem.ToString().ToUpperInvariant()), Theme.Warn);
            return;
        }

        _alias.Entry.Text = text;
        _settings.Alias = text;
        _chat.ApplyProfile();
        _chat.RestartFeed();
        AliasInfo(Loc.T("M_SAVED"), Theme.Ok);
        UpdatePreview();
    }

    private void AliasInfo(string text, Tone tone)
    {
        _aliasInfo.Text = text;
        _aliasInfo.IsVisible = true;
        _aliasInfo.Tint(Label.TextColorProperty, tone);
    }

    private void PickColor(int color)
    {
        _settings.Color = color;
        _chat.ApplyProfile();
        _chat.RestartFeed();
        UpdateSwatches();
        UpdatePreview();
    }

    private void PickSize(ChatFontSize size)
    {
        _settings.FontSize = size;
        UpdateSizes();
    }

    private void UpdateSwatches()
    {
        for (var index = 0; index < _swatches.Length; index++)
        {
            var selected = _settings.Color == index;
            var swatch = _swatches[index];
            swatch.Text = index == 0 ? Loc.T("CHAT_COLOR_AUTO") + (selected ? " ✓" : "") : selected ? "✓" : "";
            swatch.FontAttributes = FontAttributes.Bold;
            if (index == 0) swatch.Tint(Button.TextColorProperty, Theme.Text);
            else swatch.TextColor = Colors.White;
            swatch.BorderWidth = selected ? 3 : 1;
            swatch.Tint(Button.BorderColorProperty, selected ? Theme.Text : Theme.Shadow);
        }
    }

    private void UpdateSizes()
    {
        for (var i = 0; i < Sizes.Length; i++)
        {
            var selected = _settings.FontSize == Sizes[i];
            var button = _sizeButtons[i];
            button.Tint(Button.BackgroundColorProperty, selected ? Theme.Selection : Theme.Face);
            if (selected) button.TextColor = Colors.White;
            else button.Tint(Button.TextColorProperty, Theme.Text);
            button.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
        }
    }

    /// <summary>So sehen dich die anderen: Name (mit ID-Endung) in der Farbe, die sie sehen.</summary>
    private void UpdatePreview()
    {
        var alias = _chat.MyAlias;
        var name = alias is null ? Loc.T("CHAT_ID", _chat.Identity.Id) : $"{alias} {ChatProfile.IdTag(_chat.Identity.Id)}";
        var index = _chat.MyColor > 0 ? _chat.MyColor : ChatPalette.AutoIndex(_chat.Identity.Fingerprint);
        var dark = Theme.IsDark;
        _preview.FormattedText = new FormattedString
        {
            Spans =
            {
                new Span { Text = name, FontAttributes = FontAttributes.Bold, TextColor = Theme.NameTone(index).Pick(dark) },
                new Span { Text = ": " + Loc.T("CHAT_CUSTOMIZE_SAMPLE"), TextColor = Theme.Text.Pick(dark) },
            },
        };
    }

    private void SaveServer()
    {
        var text = (_server.Entry.Text ?? "").Trim();
        if (text.Length == 0 || IsAcceptableServer(text))
        {
            _settings.Server = text;
            _serverInfo.IsVisible = false;
        }
        else
        {
            _settings.Server = "";
            _serverInfo.Text = Loc.T("M_SERVER_INVALID");
            _serverInfo.IsVisible = true;
            _serverInfo.Tint(Label.TextColorProperty, Theme.Warn);
        }
    }

    /// <summary>Dieselbe Regel wie <see cref="DefaultChatNetwork.ParseServer"/>: nur https (http nur fuer einen Dienst auf dem Geraet selbst).</summary>
    private static bool IsAcceptableServer(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
}
