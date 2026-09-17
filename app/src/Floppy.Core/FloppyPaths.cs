namespace Floppy.Core;

/// <summary>
/// Wo liegt was? Gleiche Regeln wie V1 (Get-FloppyHome, Get-FloppyDataHome,
/// Get-FloppyLibraryPath, Get-FloppyLogPath in FloppyLib.ps1), damit Konsole und
/// App dieselben Dateien meinen.
/// </summary>
public sealed class FloppyPaths
{
    public const string ConfigFileName = "FloppyLauncher.ini";
    public const string LibraryFileName = "library.csv";

    private string? _dataHome;

    /// <param name="home">Programmordner (dort liegen FloppyLauncher.exe bzw. die Skripte und die INI).</param>
    /// <param name="userData">Benutzerordner; Standard <c>%LOCALAPPDATA%\FloppyHub</c> (fuer Tests austauschbar).</param>
    public FloppyPaths(string home, string? userData = null)
    {
        Home = Path.GetFullPath(home);
        UserData = Path.GetFullPath(userData ?? DefaultUserData);
    }

    public static string DefaultUserData => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloppyHub");

    /// <summary>Programmordner.</summary>
    public string Home { get; }

    /// <summary>
    /// Immer benutzereigen: Vertrauensliste, Cover-Cache, App-Einstellungen.
    /// Bewusst NICHT der Programmordner - den koennten andere Konten beschreiben.
    /// </summary>
    public string UserData { get; }

    public string ConfigFile => Path.Combine(Home, ConfigFileName);
    public string TrustFile => Path.Combine(UserData, "trust.json");
    public string CoverCacheDir => Path.Combine(UserData, "covers");
    public string AppSettingsFile => Path.Combine(UserData, "app.ini");

    /// <summary>Chat: eigene Identitaet + Kontakte (nie der Verlauf).</summary>
    public string ChatDir => Path.Combine(UserData, "chat");

    /// <summary>Minispiel: lokale Bestenlisten (eine Datei pro Levelpaket).</summary>
    public string ScoresDir => Path.Combine(UserData, "scores");

    /// <summary>Minispiel: eigene Level aus dem Editor.</summary>
    public string LevelsDir => Path.Combine(UserData, "levels");

    /// <summary>Programmordner, wenn beschreibbar - sonst <see cref="UserData"/> (V1: Get-WritableDir).</summary>
    public string DataHome => _dataHome ??= IsWritable(Home) ? Home : EnsureDirectory(UserData);

    /// <summary>Vorhandene library.csv im Programmordner gewinnt (Bestandsdaten), sonst der Datenordner.</summary>
    public string LibraryFile
    {
        get
        {
            var inHome = Path.Combine(Home, LibraryFileName);
            return File.Exists(inHome) ? inHome : Path.Combine(DataHome, LibraryFileName);
        }
    }

    /// <summary>Logdatei laut Optionen; <c>null</c> = Logging abgeschaltet (<c>file =</c> leer).</summary>
    public string? ResolveLogFile(FloppyOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.LogFile)) return null;
        var clean = FloppyOptions.ExpandPath(options.LogFile);
        if (clean.Length == 0) return null;
        if (PathRules.IsRooted(clean)) return PathRules.TryGetFullPath(clean) ?? clean;

        var inHome = Path.Combine(Home, clean);
        return File.Exists(inHome) ? inHome : Path.Combine(DataHome, clean);
    }

    /// <summary>
    /// Liest die INI aus dem Programmordner. Fehlt sie oder ist sie kaputt, gelten die
    /// Standardwerte - ein Tippfehler in der INI darf den Motor nie lahmlegen.
    /// </summary>
    public FloppyOptions LoadOptions(out string? problem)
    {
        problem = null;
        if (!File.Exists(ConfigFile)) return FloppyOptions.Default;
        try
        {
            return FloppyOptions.FromIni(IniDocument.Load(ConfigFile));
        }
        catch (Exception ex)
        {
            problem = $"{ConfigFileName} nicht lesbar, Standardwerte aktiv: {ex.Message}";
            return FloppyOptions.Default;
        }
    }

    public static string EnsureDirectory(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch { /* Fehler zeigt sich beim Schreiben */ }
        return dir;
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".floppy-write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
