namespace Floppy.Core;

/// <summary>
/// Alle Einstellungen, mit denselben Standardwerten und INI-Schluesseln wie V1
/// (FloppyLauncher.ini). So teilen sich Konsolen-Variante und App eine Konfiguration.
/// </summary>
public sealed record FloppyOptions
{
    public string DriveLetter { get; init; } = "A:";
    public int PollSeconds { get; init; } = 3;

    public IReadOnlyList<string> ReferenceFileNames { get; init; } = ["game.txt", "floppy.txt", "launch.txt"];
    public IReadOnlyList<string> ExecutableExtensions { get; init; } = [".exe", ".bat", ".cmd"];

    /// <summary>Ordner, aus denen pcrun= nie starten darf (Umgebungsvariablen bereits aufgeloest).</summary>
    public IReadOnlyList<string> BlockedRoots { get; init; } = [ExpandPath("%SystemRoot%")];

    /// <summary>Wenn nicht leer: pcrun= nur unterhalb dieser Ordner.</summary>
    public IReadOnlyList<string> AllowedRoots { get; init; } = [];

    public int ConfirmTimeoutSeconds { get; init; } = 90;
    public bool NonInteractive { get; init; }

    public bool HubEnabled { get; init; } = true;
    public IReadOnlyList<string> HubKeys { get; init; } = ReferenceFile.DefaultHubKeys;

    /// <summary>Logdatei relativ zum Programmordner; leer = kein Log.</summary>
    public string LogFile { get; init; } = "FloppyLauncher.log";
    public int LogMaxKb { get; init; } = 512;
    public bool ClearLogOnHubExit { get; init; } = true;

    public string Theme { get; init; } = "green";
    public bool EasterEggs { get; init; } = true;

    public string DriveRoot => PathRules.DriveRoot(DriveLetter);

    public static FloppyOptions Default { get; } = new();

    /// <summary>Werte aus einer FloppyLauncher.ini uebernehmen; Fehlendes bleibt Standard.</summary>
    public static FloppyOptions FromIni(IniDocument ini)
    {
        var d = Default;
        return new FloppyOptions
        {
            DriveLetter = ini.Get("drive", "letter", d.DriveLetter)!,
            PollSeconds = Math.Clamp(ini.GetInt("drive", "poll_seconds", d.PollSeconds), 1, 3600),

            ReferenceFileNames = ini.GetList("reference", "file_names", d.ReferenceFileNames),
            ExecutableExtensions = ini.GetList("reference", "executable_extensions", d.ExecutableExtensions),

            BlockedRoots = ini.GetList("security", "blocked_roots", d.BlockedRoots).Select(ExpandPath).ToArray(),
            AllowedRoots = ini.GetList("security", "allowed_roots", []).Select(ExpandPath).ToArray(),
            ConfirmTimeoutSeconds = Math.Clamp(ini.GetInt("security", "confirm_timeout", d.ConfirmTimeoutSeconds), 5, 3600),
            NonInteractive = ini.GetBool("security", "non_interactive", d.NonInteractive),

            HubEnabled = ini.GetBool("hub", "enabled", d.HubEnabled),
            HubKeys = ini.GetList("hub", "key_names", d.HubKeys).Select(k => k.ToLowerInvariant()).ToArray(),

            LogFile = ini.GetRaw("log", "file") ?? d.LogFile,
            LogMaxKb = Math.Max(0, ini.GetInt("log", "max_kb", d.LogMaxKb)),
            ClearLogOnHubExit = ini.GetBool("log", "clear_on_hub_exit", d.ClearLogOnHubExit),

            Theme = ini.Get("ui", "theme", d.Theme)!,
            EasterEggs = ini.GetBool("ui", "easter_eggs", d.EasterEggs),
        };
    }

    /// <summary>%Variablen% ersetzen und umschliessende Anfuehrungszeichen entfernen.</summary>
    public static string ExpandPath(string value) =>
        Environment.ExpandEnvironmentVariables(PathRules.StripQuotes(value));
}
