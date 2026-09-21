using Floppy.Chat.Client;
using FloppyChat.Mobile.Services;
using FloppyChat.Mobile.Ui;

namespace FloppyChat.Mobile.Pages;

/// <summary>Kurzes Startbild, waehrend die Chat-ID aus dem Schluesselbund geladen wird - dann geht es zur Raumwahl.</summary>
internal sealed class BootPage : ContentPage
{
    private readonly Label _status;
    private bool _started;

    public BootPage()
    {
        Kit.Prepare(this);
        _status = Kit.Dim(Loc.T("M_LOADING"), 14);
        _status.HorizontalTextAlignment = TextAlignment.Center;
        Content = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            Padding = new Thickness(24),
            Spacing = 14,
            Children =
            {
                Kit.Icon("ico_floppy", 72),
                Kit.Text(Loc.T("M_APP_TITLE"), 22, bold: true).Also(l => l.HorizontalTextAlignment = TextAlignment.Center),
                _status,
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started) return;
        _started = true;

        try
        {
            await AppHost.StartAsync();
        }
        catch (Exception ex)
        {
            // Lieber sichtbar scheitern als weisser Bildschirm: so laesst sich der Fehler vom Handy ablesen.
            _status.Text = ex.ToString();
            return;
        }

        if (Window is { } window) window.Page = new NavigationPage(new ConnectPage());
    }
}

internal static class ViewExtensions
{
    /// <summary>Kleine Einstellung im selben Ausdruck (fuer Objektinitialisierer-Listen).</summary>
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
