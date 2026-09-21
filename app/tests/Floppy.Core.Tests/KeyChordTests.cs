namespace Floppy.Core.Tests;

public class KeyChordTests
{
    private static KeyChord Parse(string text)
    {
        Assert.True(KeyChord.TryParse(text, out var chord), $"'{text}' sollte lesbar sein");
        return chord!;
    }

    [Theory]
    [InlineData("Ctrl+Alt+F12", "Ctrl+Alt+F12")]
    [InlineData("ctrl + alt + f12", "Ctrl+Alt+F12")]
    [InlineData("Strg+Umschalt+q", "Ctrl+Shift+Q")]
    [InlineData("Shift+Ctrl+Alt+Q", "Ctrl+Alt+Shift+Q")]
    [InlineData("control+escape", "Ctrl+Escape")]
    [InlineData("Ctrl+kp 5", "Ctrl+Kp 5")]
    [InlineData("Ctrl+BracketLeft", "Ctrl+BracketLeft")]
    [InlineData("f5", "F5")]
    [InlineData("Ctrl++", "Ctrl++")]
    [InlineData("Win+Q", "Win+Q")]
    public void Lesen_und_kanonisch_ausgeben(string text, string expected) =>
        Assert.Equal(expected, Parse(text).ToString());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Foo+Q")]
    [InlineData("Ctrl+Q+W")]
    [InlineData("Ctrl+Alt+Shift+Meta")]
    public void Ohne_gueltige_Taste_nicht_lesbar(string text) =>
        Assert.False(KeyChord.TryParse(text, out _));

    [Fact]
    public void Null_ist_nicht_lesbar() => Assert.False(KeyChord.TryParse(null, out _));

    [Fact]
    public void Umschalter_allein_ist_keine_Taste()
    {
        Assert.Null(KeyChord.Create(false, false, false, false, "Ctrl"));
        Assert.Null(KeyChord.Create(true, false, false, false, "Shift"));
        Assert.Null(KeyChord.Create(true, false, false, false, ""));
        Assert.Null(KeyChord.Create(true, false, false, false, null));
        Assert.Null(KeyChord.Create(true, false, false, false, "Zeile\nUmbruch"));
    }

    [Fact]
    public void Ausgabe_mit_eigenen_Umschalter_Namen()
    {
        var chord = Parse("Ctrl+Alt+Shift+F12");
        Assert.Equal("Strg+Alt+Umschalt+F12", chord.ToString("Strg", "Alt", "Umschalt", "Win"));
    }

    [Theory]
    [InlineData("Alt+F4")]
    [InlineData("alt+f4")]
    [InlineData("Ctrl+Alt+F4")]
    [InlineData("Alt+Shift+F4")]
    public void Alt_F4_ist_nie_erlaubt(string text) =>
        Assert.Equal(KeyChordProblem.AltF4, Parse(text).Validate());

    [Theory]
    [InlineData("Win+Q")]
    [InlineData("Ctrl+Win+F12")]
    public void Windows_Taste_wird_von_Windows_abgefangen(string text) =>
        Assert.Equal(KeyChordProblem.WindowsKey, Parse(text).Validate());

    [Theory]
    [InlineData("Alt+Tab")]
    [InlineData("Alt+Escape")]
    [InlineData("Alt+Space")]
    [InlineData("Ctrl+Escape")]
    [InlineData("Ctrl+Shift+Escape")]
    [InlineData("Ctrl+Alt+Delete")]
    public void Windows_eigene_Kombinationen_sind_reserviert(string text) =>
        Assert.Equal(KeyChordProblem.SystemReserved, Parse(text).Validate());

    [Theory]
    [InlineData("Q")]
    [InlineData("Shift+Q")]
    [InlineData("7")]
    [InlineData("Escape")]
    [InlineData("Space")]
    [InlineData("Enter")]
    public void Einzelne_Tasten_schliessen_sonst_beim_Tippen(string text) =>
        Assert.Equal(KeyChordProblem.NeedsModifier, Parse(text).Validate());

    [Theory]
    [InlineData("Ctrl+Alt+Q")]      // AltGr+Q = @
    [InlineData("Ctrl+Alt+E")]      // AltGr+E = €
    [InlineData("Ctrl+Alt+7")]      // AltGr+7 = {
    [InlineData("Ctrl+Alt+Shift+Q")]
    [InlineData("Ctrl+Alt+BracketLeft")]
    public void Strg_Alt_mit_Zeichentaste_kollidiert_mit_AltGr(string text) =>
        Assert.Equal(KeyChordProblem.AltGr, Parse(text).Validate());

    [Theory]
    [InlineData("F12")]
    [InlineData("Shift+F5")]
    [InlineData("Ctrl+F12")]
    [InlineData("Ctrl+Q")]
    [InlineData("Alt+Q")]
    [InlineData("Ctrl+Shift+Q")]
    [InlineData("Ctrl+Alt+F12")]     // Funktionstasten tippen kein Zeichen
    [InlineData("Ctrl+Alt+Home")]
    [InlineData("F24")]
    public void Taugliche_Kombinationen(string text) =>
        Assert.Equal(KeyChordProblem.None, Parse(text).Validate());

    [Fact]
    public void Von_der_App_belegte_Kombinationen_werden_abgelehnt()
    {
        string[] used = ["F1", "F5"];
        Assert.Equal(KeyChordProblem.InUse, Parse("F5").Validate(used));
        Assert.Equal(KeyChordProblem.InUse, Parse("f1").Validate(used));
        Assert.Equal(KeyChordProblem.None, Parse("F12").Validate(used));
        Assert.Equal(KeyChordProblem.None, Parse("Ctrl+F5").Validate(used));   // andere Kombination
        Assert.Equal(KeyChordProblem.None, Parse("F5").Validate());
    }

    [Theory]
    [InlineData("F0")]
    [InlineData("F25")]
    [InlineData("F05")]
    public void Keine_Funktionstasten_ausserhalb_F1_bis_F24(string key)
    {
        Assert.False(KeyChord.IsFunctionKey(key));
        Assert.Equal(KeyChordProblem.NeedsModifier, KeyChord.Create(false, false, false, false, key)!.Validate());
    }

    [Fact]
    public void Steht_so_in_der_INI_und_kommt_unveraendert_zurueck()
    {
        var chord = Parse("ctrl+alt+f12");
        var ini = IniDocument.Parse(["[app]", "first_run_done = true", $"quit_hotkey = {chord}"]);

        Assert.Equal("Ctrl+Alt+F12", ini.Get("app", "quit_hotkey", ""));
        Assert.Equal(chord, Parse(ini.Get("app", "quit_hotkey", "")!));
        Assert.False(KeyChord.TryParse(IniDocument.Parse(["[app]", "quit_hotkey ="]).Get("app", "quit_hotkey", ""), out _));   // leer = nicht festgelegt
    }

    [Fact]
    public void Gleichheit_ignoriert_Gross_und_Kleinschreibung()
    {
        var a = KeyChord.Create(true, false, false, false, "q")!;
        var b = Parse("Ctrl+Q");
        var c = Parse("Ctrl+Shift+Q");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
        Assert.False(a.Equals(null));
    }

    [Theory]
    [InlineData("Ctrl+F12")]
    [InlineData("Ctrl+Alt+Shift+F12")]
    [InlineData("Alt+Q")]
    [InlineData("Ctrl++")]
    [InlineData("Ctrl+Kp 5")]
    [InlineData("F9")]
    public void Ausgeben_und_wieder_lesen_ergibt_dasselbe(string text)
    {
        var chord = Parse(text);
        Assert.Equal(chord, Parse(chord.ToString()));
    }
}
