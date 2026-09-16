namespace FloppyHub.App.Core;

/// <summary>
/// Programm-Argumente (nach "++"). Der Motor benutzt nur --confirm und --hub,
/// der Rest ist fuer Entwicklung und Tests.
/// </summary>
public sealed record AppArgs
{
    /// <summary>Motor: Diskette braucht eine Rueckfrage.</summary>
    public bool Confirm { get; init; }

    /// <summary>Motor: Hub-Diskette eingelegt.</summary>
    public bool Hub { get; init; }

    /// <summary>Programmordner (INI, library.csv, Log). Standard: Ordner der EXE.</summary>
    public string? Home { get; init; }

    /// <summary>Benutzerordner (Einstellungen, Vertrauensliste, Cover). Standard: %LOCALAPPDATA%\FloppyHub.</summary>
    public string? UserData { get; init; }

    /// <summary>Nur Test: Ordner statt Laufwerk A: auswerten.</summary>
    public string? Drive { get; init; }

    /// <summary>Test: Bildschirmfoto speichern und beenden.</summary>
    public string? Screenshot { get; init; }

    public string? Theme { get; init; }
    public string? View { get; init; }
    public float? Scale { get; init; }

    /// <summary>Platzhalter-Icons als PNG nach assets/icons schreiben und beenden.</summary>
    public bool ForgeIcons { get; init; }

    /// <summary>Test: Willkommensdialog erzwingen.</summary>
    public bool FirstRun { get; init; }

    public static AppArgs Parse(IReadOnlyList<string> args)
    {
        var r = new AppArgs();
        for (var i = 0; i < args.Count; i++)
        {
            string? Next() => i + 1 < args.Count ? args[++i] : null;
            switch (args[i].ToLowerInvariant())
            {
                case "--confirm": r = r with { Confirm = true }; break;
                case "--hub": r = r with { Hub = true }; break;
                case "--home": r = r with { Home = Next() }; break;
                case "--user-data": r = r with { UserData = Next() }; break;
                case "--drive": r = r with { Drive = Next() }; break;
                case "--screenshot": r = r with { Screenshot = Next() }; break;
                case "--theme": r = r with { Theme = Next() }; break;
                case "--view": r = r with { View = Next() }; break;
                case "--scale":
                    if (float.TryParse(Next(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var s))
                        r = r with { Scale = s };
                    break;
                case "--forge-icons": r = r with { ForgeIcons = true }; break;
                case "--first-run": r = r with { FirstRun = true }; break;
            }
        }
        return r;
    }
}
