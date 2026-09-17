using FloppyHub.App.Core;

namespace FloppyHub.App.Fun;

/// <summary>
/// Kleine Spielereien fuer Mael und den Freundeskreis. Abschaltbar ueber
/// <c>[ui] easter_eggs = false</c> in FloppyLauncher.ini.
/// Grundregel: NIE in Sicherheits-Rueckfragen, nie etwas, das Daten veraendert.
///
/// Uebersicht (nicht weitersagen):
///  1. Ueber-Dialog: Diskette 3x anklicken
///  2. Konami-Code (hoch hoch runter runter links rechts links rechts B A): gruener Roehrenmonitor
///  3. Zauberwoerter in der Bibliothekssuche (xyzzy, 42, sudo, joshua, floppy)
///  4. Besondere Tage aendern den Spruch neben dem Logo
///  5. Grosse Diskette in der Disketten-Ansicht 5x anklicken: Auswurf
///  6. CD-Laufwerk in der Laufwerke-Ansicht 3x anklicken
///  7. Klick auf "FLOPPY HUB": andere Windows-Versionen
///  8. Diskette mit dem Namen FLOPPYHUB einlegen
///  9. Manchmal beim Start: "Wusstest du?"
/// </summary>
public static class EasterEggs
{
    public static bool Enabled { get; set; } = true;

    /// <summary>Nur fuer Tests: anderes "heute".</summary>
    public static DateTime? Today { get; set; }

    private static DateTime Now => Today ?? DateTime.Today;

    // ---- 4. besondere Tage ----
    public static string Tagline()
    {
        if (!Enabled) return Loc.T("BRAND_TAGLINE");
        var d = Now;
        var key = (d.Month, d.Day) switch
        {
            (12, >= 24 and <= 26) => "EGG_DAY_XMAS",
            (12, 31) or (1, 1) => "EGG_DAY_NEWYEAR",
            (4, 1) => "EGG_DAY_APRIL",
            (5, 4) => "EGG_DAY_STARWARS",
            (10, 31) => "EGG_DAY_HALLOWEEN",
            (3, 14) => "EGG_DAY_PI",
            _ => "BRAND_TAGLINE",
        };
        return Loc.T(key);
    }

    // ---- 3. Zauberwoerter ----
    public static string? SearchWord(string text)
    {
        if (!Enabled) return null;
        return text.Trim().ToLowerInvariant() switch
        {
            "xyzzy" => Loc.T("EGG_WORD_XYZZY"),
            "42" => Loc.T("EGG_WORD_42"),
            "sudo" => Loc.T("EGG_WORD_SUDO"),
            "joshua" => Loc.T("EGG_WORD_JOSHUA"),
            "floppy" => Loc.T("EGG_WORD_FLOPPY"),
            _ => null,
        };
    }

    // ---- 7. Windows-Versionen ----
    private static readonly string[] Editions = ["", " 95", " 98", " ME", " 2000", " XP"];
    private static int _edition;

    public static (string Name, string? Comment) NextEdition()
    {
        _edition = (_edition + 1) % Editions.Length;
        var name = "FLOPPY HUB" + Editions[_edition];
        var comment = Editions[_edition] == " ME" ? Loc.T("EGG_EDITION_ME") : null;
        return (name, comment);
    }

    // ---- 8. offizielle Diskette ----
    public static bool IsOfficialDisc(string? volumeLabel) =>
        Enabled && string.Equals(volumeLabel?.Trim(), "FLOPPYHUB", StringComparison.OrdinalIgnoreCase);

    // ---- 9. Wusstest du? ----
    public static string? StartupFact(Random rng)
    {
        if (!Enabled || rng.Next(8) != 0) return null;
        return Loc.T("EGG_FACT_" + (rng.Next(6) + 1));
    }
}
