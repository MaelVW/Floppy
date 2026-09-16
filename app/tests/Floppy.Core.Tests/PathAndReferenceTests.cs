namespace Floppy.Core.Tests;

public class PathRulesTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\x.exe\"", "C:\\Program Files\\App\\x.exe")]
    [InlineData("'C:\\a\\b.exe'", "C:\\a\\b.exe")]
    [InlineData("  \"C:\\x.exe\"  ", "C:\\x.exe")]
    [InlineData("C:\\normal.exe", "C:\\normal.exe")]
    [InlineData("\"\"C:\\doppelt.exe\"\"", "C:\\doppelt.exe")]
    [InlineData("C:\\Users\\mael\\Modrinth App.exe\"\"", "C:\\Users\\mael\\Modrinth App.exe")] // kaputte library.csv-Zeile
    [InlineData(null, "")]
    public void StripQuotes_entfernt_Anfuehrungszeichen(string? input, string expected) =>
        Assert.Equal(expected, PathRules.StripQuotes(input));

    [Fact]
    public void IsUnder_erkennt_Unterordner_und_ignoriert_Namensgleichheit()
    {
        Assert.True(PathRules.IsUnder(@"C:\Games\x.exe", @"C:\Games"));
        Assert.True(PathRules.IsUnder(@"c:\games\SUB\x.exe", @"C:\Games\"));
        Assert.True(PathRules.IsUnder(@"C:\Games", @"C:\Games"));
        Assert.False(PathRules.IsUnder(@"C:\GamesExtra\x.exe", @"C:\Games")); // nicht nur Praefix!
        Assert.False(PathRules.IsUnder(@"D:\Games\x.exe", @"C:\Games"));
    }

    [Theory]
    [InlineData("a", "A:\\")]
    [InlineData("A:", "A:\\")]
    [InlineData("b:\\", "B:\\")]
    public void DriveRoot_normiert(string input, string expected) => Assert.Equal(expected, PathRules.DriveRoot(input));

    [Fact]
    public void Executable_Endungen_ohne_Gross_Kleinschreibung()
    {
        string[] ext = [".exe", ".bat", ".cmd"];
        Assert.True(PathRules.HasExecutableExtension(@"C:\x\GAME.EXE", ext));
        Assert.False(PathRules.HasExecutableExtension(@"C:\x\start.ps1", ext));
        Assert.False(PathRules.HasExecutableExtension(@"C:\x\ohne", ext));
    }
}

public class ReferenceFileTests
{
    [Fact]
    public void Parse_liest_Schluessel_Kommentare_und_nackte_Id()
    {
        var map = ReferenceFile.Parse([
            "# Kommentar",
            "; auch Kommentar",
            "",
            "PCRUN = \"C:\\Program Files\\App\\app.exe\"",
            "args: -fullscreen",
        ]);
        Assert.Equal(@"C:\Program Files\App\app.exe", map["pcrun"]);
        Assert.Equal("-fullscreen", map["args"]);
        Assert.False(map.ContainsKey("#"));

        var bare = ReferenceFile.Parse(["   220200   "]);
        Assert.Equal("220200", bare["id"]);
    }

    [Fact]
    public void Parse_leerer_Wert_wird_nicht_als_Schluessel_erkannt()
    {
        // Wie V1: "(.+?)" verlangt einen Wert.
        Assert.Empty(ReferenceFile.Parse(["pcrun="]));
    }
}

public class SteamAppsTests
{
    [Theory]
    [InlineData("220", "220")]
    [InlineData("https://store.steampowered.com/app/620/Portal_2/", "620")]
    [InlineData("steam://rungameid/3772810", "3772810")]
    [InlineData("\"220200\"", "220200")]
    public void TryResolveAppId_erkennt_Varianten(string input, string expected)
    {
        Assert.True(SteamApps.TryResolveAppId(input, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void TryResolveAppId_lehnt_Unsinn_ab(string? input) => Assert.False(SteamApps.TryResolveAppId(input, out _));

    [Fact]
    public void Bekannte_Namen() => Assert.Equal("Kerbal Space Program", SteamApps.TryGetKnownName("220200"));
}
