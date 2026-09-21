using Floppy.Chat.Client;
using Floppy.Core.Chat;

namespace FloppyChat.Mobile.Services;

/// <summary>Die Einstellungen der App (Android: SharedPreferences). Nichts davon ist geheim.</summary>
internal sealed class ChatSettings : IChatSettings
{
    private static IPreferences Prefs => Preferences.Default;

    /// <summary>Anzeigename wie eingegeben; leer = keiner.</summary>
    public string? Alias
    {
        get => Prefs.Get("alias", "");
        set => Prefs.Set("alias", value ?? "");
    }

    /// <summary>0 = automatisch, 1 bis 8.</summary>
    public int Color
    {
        get => ChatProfile.CleanColor(Prefs.Get("color", 0));
        set => Prefs.Set("color", ChatProfile.CleanColor(value));
    }

    /// <summary>Kommt spaeter (dafuer braucht Android einen Hintergrunddienst) - bis dahin aus.</summary>
    public ChatNotifyMode Notify => ChatNotifyMode.Off;

    public bool HighlightMentions
    {
        get => Prefs.Get("highlight", true);
        set => Prefs.Set("highlight", value);
    }

    /// <summary>ntfy-Adresse; leer = Standard (ntfy.sh).</summary>
    public string Server
    {
        get => Prefs.Get("server", "");
        set => Prefs.Set("server", (value ?? "").Trim());
    }

    public ChatFontSize FontSize
    {
        get => ChatProfile.ParseFontSize(Prefs.Get("font", nameof(ChatFontSize.Normal)));
        set => Prefs.Set("font", value.ToString());
    }

    /// <summary>Bildschirm im Chat anlassen (sonst schlaeft das Handy ein und die Verbindung ruht).</summary>
    public bool KeepScreenOn
    {
        get => Prefs.Get("keepscreen", true);
        set => Prefs.Set("keepscreen", value);
    }
}
