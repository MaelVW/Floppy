namespace FloppyLauncher;

/// <summary>
/// Kommandozeile des Motors. Im Autostart braucht es keine davon - sie sind fuer
/// Tests, Entwicklung und das Setup (--stop).
/// </summary>
internal sealed record MotorArgs
{
    /// <summary>Nichts starten, nur protokollieren (wie -DryRun in V1).</summary>
    public bool DryRun { get; init; }

    /// <summary>Genau einmal nachsehen und beenden (wie -RunOnce in V1).</summary>
    public bool Once { get; init; }

    /// <summary>Laufenden Motor sauber beenden.</summary>
    public bool Stop { get; init; }

    public bool AllowMultiple { get; init; }

    /// <summary>Log-Zeilen zusaetzlich auf die Standardausgabe schreiben.</summary>
    public bool Console { get; init; }

    /// <summary>Nur zum Testen: Ordner statt Laufwerk A: beobachten.</summary>
    public string? Drive { get; init; }

    public string? Home { get; init; }

    /// <summary>Benutzerordner (Vertrauensliste). Standard: %LOCALAPPDATA%\FloppyHub.</summary>
    public string? UserData { get; init; }

    public string? LogFile { get; init; }

    /// <summary>Andere App-EXE (Entwicklung: Godot-Editor).</summary>
    public string? AppExe { get; init; }

    /// <summary>Argumente vor dem Befehl, z. B. "--path C:\...\FloppyHub.App ++ --home C:\Floppy".</summary>
    public string? AppArgs { get; init; }

    public IReadOnlyList<string> Problems { get; init; } = [];

    public static MotorArgs Parse(IReadOnlyList<string> args)
    {
        var result = new MotorArgs();
        var problems = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            string? Value()
            {
                if (i + 1 < args.Count) return args[++i];
                problems.Add($"{a} braucht einen Wert.");
                return null;
            }

            switch (a.ToLowerInvariant())
            {
                case "--dry-run": result = result with { DryRun = true }; break;
                case "--once": result = result with { Once = true }; break;
                case "--stop": result = result with { Stop = true }; break;
                case "--allow-multiple": result = result with { AllowMultiple = true }; break;
                case "--console": result = result with { Console = true }; break;
                case "--drive": result = result with { Drive = Value() }; break;
                case "--home": result = result with { Home = Value() }; break;
                case "--user-data": result = result with { UserData = Value() }; break;
                case "--log": result = result with { LogFile = Value() }; break;
                case "--app": result = result with { AppExe = Value() }; break;
                case "--app-args": result = result with { AppArgs = Value() }; break;
                default: problems.Add($"Unbekannter Parameter ignoriert: {a}"); break;
            }
        }
        return result with { Problems = problems };
    }

    /// <summary>Argumente fuer die App: Godot reicht alles nach "++" an das Programm weiter.</summary>
    public static string BuildAppArguments(string? prefix, string command)
    {
        var p = (prefix ?? string.Empty).Trim();
        var hasSeparator = p.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("++");
        return $"{p} {(hasSeparator ? "" : "++ ")}--{command}".Trim();
    }
}
