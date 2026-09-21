using Floppy.Chat.Client;
using FloppyChat.Mobile.Services;
using FloppyChat.Mobile.Ui;

namespace FloppyChat.Mobile.Pages;

/// <summary>Wer gerade im Raum ist (aktualisiert sich von selbst, wenn jemand kommt oder geht).</summary>
internal sealed class MembersPage : ContentPage
{
    private readonly HeaderBar _title;
    private readonly VerticalStackLayout _list = new() { Spacing = 6, Padding = new Thickness(12) };

    public MembersPage()
    {
        Kit.Prepare(this);
        _title = new HeaderBar(Loc.T("CHAT_MEMBERS", 0), () => _ = Navigation.PopAsync());
        Content = Kit.Screen(_title, new ScrollView { Content = _list });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        AppHost.Chat.Changed += Refresh;
        Refresh();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        AppHost.Chat.Changed -= Refresh;
    }

    private void Refresh()
    {
        var members = AppHost.Chat.GetMembers();
        _title.Title.Text = Loc.T("CHAT_MEMBERS", members.Count);
        _list.Clear();
        foreach (var member in members) _list.Add(Row(member));
    }

    private static View Row(MemberRow member)
    {
        var texts = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                Kit.Text(member.Name, 16, bold: true, tone: Theme.NameTone(member.ColorIndex)),
                Kit.Dim(Loc.T("CHAT_ID", member.Id)),
            },
        };

        var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(8, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.Add(Kit.Icon(member.IsSelf ? "ico_floppy" : "ico_led_on", 22), 0, 0);
        grid.Add(texts, 1, 0);
        if (member.ModeText.Length > 0) grid.Add(Kit.Dim(member.ModeText, 13).Also(l => l.VerticalOptions = LayoutOptions.Center), 2, 0);
        return new Bevel(grid);
    }
}
