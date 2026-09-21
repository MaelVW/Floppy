using Floppy.Chat.Client;

namespace FloppyChat.Mobile.Ui;

/// <summary>Eine Farbe fuer den hellen und den dunklen Modus - die Oberflaeche folgt dem Handy (Einstellung "Dunkles Design").</summary>
internal readonly record struct Tone(Color Light, Color Dark)
{
    public static Tone Of(string light, string dark) => new(Color.FromArgb(light), Color.FromArgb(dark));

    public Color Pick(bool dark) => dark ? Dark : Light;

    /// <summary>Die Farbe fuer das gerade aktive Design.</summary>
    public Color Now => Pick(Theme.IsDark);
}

/// <summary>
/// Farben der Oberflaeche - dieselben Werte wie in der Desktop-App (Skin/Palette.cs):
/// hell = Windows-2000/WinRAR-Werkzeug, dunkel = Audacity/VLC-dunkel. Gleiche Formen, andere Farben.
/// </summary>
internal static class Theme
{
    public static readonly Tone Window = Tone.Of("#d8d5cf", "#2d2f33");
    public static readonly Tone Face = Tone.Of("#e2dfd9", "#414449");
    public static readonly Tone Highlight = Tone.Of("#ffffff", "#6b6e75");
    public static readonly Tone Light = Tone.Of("#e8e6e1", "#515459");
    public static readonly Tone Shadow = Tone.Of("#8e8a82", "#1c1d20");
    public static readonly Tone DarkShadow = Tone.Of("#3c3a36", "#0d0e0f");
    public static readonly Tone Field = Tone.Of("#ffffff", "#1e1f22");

    public static readonly Tone Text = Tone.Of("#141414", "#e9e9e6");
    public static readonly Tone TextDim = Tone.Of("#5c5850", "#a6a9ad");

    public static readonly Tone Selection = Tone.Of("#2f5fb3", "#3d6db5");
    public static readonly Tone SelectionSoft = Tone.Of("#cddcf3", "#33445c");
    public static readonly Tone TitleA = Tone.Of("#0a246a", "#18305a");
    public static readonly Tone TitleB = Tone.Of("#5b8bc9", "#3f6eab");
    public static readonly Tone Accent = Tone.Of("#2f5fb3", "#5b8fdb");

    public static readonly Tone Ok = Tone.Of("#2e9e3e", "#4cbb5e");
    public static readonly Tone Warn = Tone.Of("#c98208", "#e2a63a");
    public static readonly Tone Error = Tone.Of("#c0392b", "#e25a4b");

    public static readonly Color OnTitle = Color.FromArgb("#ffffff");
    public static readonly Color OnTitleDim = Color.FromArgb("#dbe6f7");

    public static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;

    /// <summary>Namensfarbe im Chat: 1 bis 8 aus der Palette (wie am PC), 0 = Akzentfarbe.</summary>
    public static Tone NameTone(int index) =>
        index <= 0 ? Accent : Tone.Of(ChatPalette.Hex(index, dark: false), ChatPalette.Hex(index, dark: true));
}

internal static class ThemeExtensions
{
    /// <summary>Farbe an das Design binden (wechselt von selbst, wenn das Handy zwischen hell und dunkel umschaltet).</summary>
    public static T Tint<T>(this T target, BindableProperty property, Tone tone) where T : BindableObject
    {
        target.SetAppThemeColor(property, tone.Light, tone.Dark);
        return target;
    }
}
