using System.Globalization;
using Floppy.Chat.Client;
using FloppyChat.Mobile.Pages;
using FloppyChat.Mobile.Services;

namespace FloppyChat.Mobile;

public sealed class App : Application
{
    public App()
    {
        Loc.Load(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);   // Deutsch oder Englisch, nach dem Handy
        UserAppTheme = AppTheme.Unspecified;                                // hell/dunkel wie das Handy
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new BootPage());
        window.Destroying += (_, _) => AppHost.Shutdown();   // tschuess sagen, wenn die App beendet wird
        return window;
    }
}
