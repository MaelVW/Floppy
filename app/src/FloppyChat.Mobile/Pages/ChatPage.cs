using System.Collections.ObjectModel;
using Floppy.Chat.Client;
using Floppy.Core.Chat;
using FloppyChat.Mobile.Services;
using FloppyChat.Mobile.Ui;

namespace FloppyChat.Mobile.Pages;

/// <summary>Der Raum: Verlauf, Eingabe, Verbindungsstand, Personen, Wechsel ins lokale Netz.</summary>
internal sealed class ChatPage : ContentPage
{
    private readonly ChatClient _chat = AppHost.Chat;
    private readonly double _fontSize = 15 * ChatProfile.FontScale(AppHost.Settings.FontSize);

    private readonly HeaderBar _title;
    private readonly BevelButton _members;
    private readonly BevelButton _local;
    private readonly Bevel _banner;
    private readonly Label _bannerText;
    private readonly BevelButton _yes;
    private readonly BevelButton _no;
    private readonly CollectionView _list;
    private readonly Label _hint;
    private readonly Label _result;
    private readonly Field _input;
    private readonly BevelButton _send;

    private ObservableCollection<ChatRow> _rows = [];
    private IDispatcherTimer? _ticker;
    private string _dismissedHint = "";
    private DateTimeOffset _resultUntil;
    private bool _closing;

    public ChatPage()
    {
        Kit.Prepare(this);
        _chat.RestartFeed();   // diese Seite zeigt immer den ganzen Verlauf

        _title = new HeaderBar(_chat.RoomLabel, () => _ = LeaveAsync());

        // ---- Werkzeugleiste ----
        _members = new BevelButton(Loc.T("M_BTN_MEMBERS", 1), () => _ = Navigation.PushAsync(new MembersPage()), icon: "ico_contact", fontSize: 13);
        _local = new BevelButton(Loc.T("CHAT_BTN_LOCAL"), ProposeLocal, icon: "ico_lan", fontSize: 13);
        var leave = new BevelButton(Loc.T("CHAT_BTN_LEAVE"), () => _ = LeaveAsync(), icon: "ico_door", fontSize: 13);
        var tools = new Grid { ColumnSpacing = 6, Padding = new Thickness(8, 6, 8, 2) };
        for (var i = 0; i < 3; i++) tools.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        tools.Add(_members, 0, 0);
        tools.Add(_local, 1, 0);
        tools.Add(leave, 2, 0);

        // ---- Wechsel-Vorschlag ----
        _bannerText = Kit.Text("", 14);
        _yes = new BevelButton("", () => AnswerBanner(true), primary: true, fontSize: 14);
        _no = new BevelButton("", () => AnswerBanner(false), fontSize: 14);
        var answers = new Grid { ColumnSpacing = 8 };
        answers.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        answers.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        answers.Add(_yes, 0, 0);
        answers.Add(_no, 1, 0);
        _banner = new Bevel(new VerticalStackLayout { Spacing = 8, Padding = new Thickness(10), Children = { _bannerText, answers } }, face: Theme.SelectionSoft)
        {
            IsVisible = false,
            Margin = new Thickness(8, 6, 8, 0),
        };

        // ---- Verlauf ----
        _list = new CollectionView
        {
            ItemsSource = _rows,
            SelectionMode = SelectionMode.None,
            ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepLastItemInView,
            ItemTemplate = new DataTemplate(() => new ChatRowView(_fontSize)),
        };
        var history = new Bevel(_list, sunken: true) { Margin = new Thickness(8, 6) };

        // ---- Hinweise ----
        _hint = Kit.Text("", 12);
        _hint.Margin = new Thickness(12, 0, 12, 2);
        _hint.IsVisible = false;
        _hint.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(DismissHint) });
        _result = Kit.Text("", 13, bold: true, tone: Theme.Warn);
        _result.Margin = new Thickness(12, 0, 12, 2);
        _result.IsVisible = false;

        // ---- Eingabe ----
        _input = new Field(Loc.T("CHAT_PLACEHOLDER_MESSAGE"), ChatPayload.MaxTextLength);
        _input.Entry.Keyboard = Keyboard.Chat;
        _input.Entry.ReturnType = ReturnType.Send;
        _input.Entry.Completed += (_, _) => Send();
        _send = new BevelButton(Loc.T("CHAT_BTN_SEND"), Send, primary: true, icon: "ico_send");
        var inputRow = new Grid { ColumnSpacing = 8, Padding = new Thickness(8, 4, 8, 8) };
        inputRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        inputRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        inputRow.Add(_input, 0, 0);
        inputRow.Add(_send, 1, 0);

        var grid = new Grid { RowSpacing = 0 };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Star, GridLength.Auto, GridLength.Auto, GridLength.Auto })
            grid.RowDefinitions.Add(new RowDefinition(height));
        View[] rows = [_title, tools, _banner, history, _hint, _result, inputRow];
        for (var i = 0; i < rows.Length; i++) grid.Add(rows[i], 0, i);
        Content = grid;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _chat.Changed += Refresh;
        if (Application.Current is { } app) app.RequestedThemeChanged += OnThemeChanged;
        DeviceDisplay.Current.KeepScreenOn = AppHost.Settings.KeepScreenOn;

        _ticker = Dispatcher.CreateTimer();
        _ticker.Interval = TimeSpan.FromMilliseconds(500);
        _ticker.Tick += (_, _) => Tick();
        _ticker.Start();

        Refresh();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _chat.Changed -= Refresh;
        if (Application.Current is { } app) app.RequestedThemeChanged -= OnThemeChanged;
        DeviceDisplay.Current.KeepScreenOn = false;
        _ticker?.Stop();
        _ticker = null;
    }

    protected override bool OnBackButtonPressed()
    {
        _ = LeaveAsync();
        return true;   // selbst behandelt: erst nachfragen, dann verlassen
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        _chat.RestartFeed();   // Namensfarben neu setzen
        Refresh();
    }

    // ------------------------------------------------------------------
    // Anzeige
    // ------------------------------------------------------------------

    private void Refresh()
    {
        if (_closing) return;

        var status = _chat.GetStatus();
        var lamp = status.Level switch
        {
            ChatStatusLevel.Ok => Theme.Ok.Pick(dark: true),
            ChatStatusLevel.Warn => Theme.Warn.Pick(dark: true),
            _ => Color.FromArgb("#9aa0a6"),
        };
        _title.Title.Text = _chat.RoomLabel;
        _title.SetSubtitle(status.Text, lamp);

        _members.Button.Text = Loc.T("M_BTN_MEMBERS", _chat.GetMembers().Count);
        _local.IsEnabled = _chat.CanProposeLocal;

        var canWrite = _chat.CanWrite;
        _input.Entry.IsEnabled = canWrite;
        _send.IsEnabled = canWrite;

        // Hinweis unter dem Verlauf: Raum zu > Sperre/Offener Chat; ein Tipp blendet ihn aus
        string hint;
        Tone hintTone;
        if (_chat.Session is { State: ChatSessionState.Ended })
        {
            (hint, hintTone) = (Loc.T("M_ROOM_ENDED"), Theme.TextDim);
        }
        else
        {
            var (text, warn) = _chat.GetHint();
            (hint, hintTone) = (text == _dismissedHint ? "" : text, warn ? Theme.Warn : Theme.TextDim);
        }
        _hint.IsVisible = hint.Length > 0;
        if (hint.Length > 0)
        {
            _hint.Text = hint;
            _hint.Tint(Label.TextColorProperty, hintTone);
        }

        ApplyRows(_chat.ReadNewRows());
        UpdateBanner();
    }

    private void ApplyRows(FeedChange change)
    {
        if (change.Reset)
        {
            _rows = new ObservableCollection<ChatRow>(change.Rows);
            _list.ItemsSource = _rows;
            Dispatcher.Dispatch(ScrollToEnd);
            return;
        }
        foreach (var row in change.Rows) _rows.Add(row);
    }

    private void ScrollToEnd()
    {
        if (_rows.Count > 0) _list.ScrollTo(_rows.Count - 1, position: ScrollToPosition.End, animate: false);
    }

    private void UpdateBanner()
    {
        var banner = _chat.GetBanner();
        _banner.IsVisible = banner is not null;
        if (banner is null) return;

        _bannerText.Text = banner.Text;
        _yes.IsVisible = banner.YesLabel is not null;
        _no.IsVisible = banner.NoLabel is not null;
        if (banner.YesLabel is not null) _yes.Button.Text = banner.YesLabel;
        if (banner.NoLabel is not null) _no.Button.Text = banner.NoLabel;
    }

    /// <summary>Zweimal pro Sekunde: die Sekunden im Vorschlag zaehlen herunter, Meldungen verschwinden.</summary>
    private void Tick()
    {
        if (_closing) return;
        UpdateBanner();
        if (_result.IsVisible && DateTimeOffset.UtcNow >= _resultUntil) _result.IsVisible = false;
    }

    private void ShowResult(string text)
    {
        _result.Text = text;
        _result.IsVisible = true;
        _resultUntil = DateTimeOffset.UtcNow.AddSeconds(5);
    }

    private void DismissHint()
    {
        _dismissedHint = _hint.Text ?? "";
        _hint.IsVisible = false;
    }

    // ------------------------------------------------------------------
    // Aktionen
    // ------------------------------------------------------------------

    private void Send()
    {
        var result = _chat.Send(_input.Entry.Text ?? "");
        if (result == ChatResult.Ok) _input.Entry.Text = "";
        else if (ChatClient.ResultText(result) is { } message) ShowResult(message);
        _input.Entry.Focus();   // Tastatur bleibt offen, man schreibt gleich weiter
    }

    private void ProposeLocal()
    {
        var result = _chat.ProposeLocal();
        if (ChatClient.ResultText(result) is { } message) ShowResult(message);
        Refresh();
    }

    private void AnswerBanner(bool yes)
    {
        _chat.AnswerProposal(yes);
        Refresh();
    }

    private async Task LeaveAsync()
    {
        if (_closing) return;
        if (_chat.IsInRoom || _chat.IsDeriving)
        {
            var leave = await DisplayAlertAsync(Loc.T("M_LEAVE_TITLE"), Loc.T("M_LEAVE_TEXT"), Loc.T("CHAT_BTN_LEAVE"), Loc.T("M_BTN_STAY"));
            if (!leave) return;
        }

        _closing = true;
        _chat.Reset();   // Raum verlassen und den Verlauf vergessen
        await Navigation.PopAsync();
    }
}
