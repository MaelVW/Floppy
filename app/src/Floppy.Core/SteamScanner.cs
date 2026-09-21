using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace Floppy.Core;

/// <summary>Ein installiertes Steam-Spiel, so wie es in <c>steamapps\appmanifest_&lt;AppID&gt;.acf</c> steht.</summary>
/// <param name="AppId">Steam-AppID ohne fuehrende Nullen - damit wird das Spiel per <c>steam://rungameid/</c> gestartet.</param>
/// <param name="Name">Anzeigename (schon gesaeubert: kein Steuerzeichen, hoechstens 120 Zeichen).</param>
/// <param name="InstallDir">Ordnername unter <c>steamapps\common</c>; leer, wenn die Angabe fehlt oder unbrauchbar ist.</param>
/// <param name="LibraryPath">Bibliotheksordner, in dem das Spiel liegt (z. B. <c>D:\SteamLibrary</c>).</param>
/// <param name="SizeOnDisk">Belegter Platz in Bytes, 0 = unbekannt.</param>
/// <param name="Complete">Steam meldet "vollstaendig installiert" (auch mit einem ausstehenden Update); sonst laeuft noch ein Download.</param>
public sealed record SteamGame(string AppId, string Name, string InstallDir, string LibraryPath, long SizeOnDisk, bool Complete)
{
    /// <summary>Ordner des Spiels (<c>steamapps\common\&lt;installdir&gt;</c>); leer, wenn der Ordnername unbekannt ist.</summary>
    public string InstallPath => InstallDir.Length == 0 ? "" : Path.Combine(LibraryPath, "steamapps", "common", InstallDir);
}

/// <summary>Ergebnis einer Suche in den Steam-Dateien.</summary>
/// <param name="SteamRoot">Steam-Ordner; <c>null</c> = Steam nicht gefunden.</param>
/// <param name="Libraries">Durchsuchte Bibliotheksordner (Steam legt Spiele auf beliebig vielen Laufwerken ab).</param>
/// <param name="Unreachable">In Steam eingetragen, aber nicht erreichbar (z. B. die abgesteckte USB-Platte).</param>
/// <param name="Games">Die gefundenen Spiele, nach Name sortiert, jede AppID nur einmal.</param>
/// <param name="Tools">So viele Steam-Laufzeiten (z. B. "Steamworks Common Redistributables") wurden ausgelassen.</param>
/// <param name="Damaged">So viele Manifeste waren nicht lesbar (kaputt oder gerade halb geschrieben).</param>
public sealed record SteamScan(
    string? SteamRoot,
    IReadOnlyList<string> Libraries,
    IReadOnlyList<string> Unreachable,
    IReadOnlyList<SteamGame> Games,
    int Tools,
    int Damaged)
{
    public static SteamScan NotFound { get; } = new(null, [], [], [], 0, 0);

    public bool SteamFound => SteamRoot is not null;
}

/// <summary>
/// Geht durch die Dateien von Steam und findet alle installierten Spiele - ohne Konto, ohne Anmeldung und
/// ohne Internet. Genutzt werden nur Dateien, die Steam selbst pflegt: <c>libraryfolders.vdf</c> (welche
/// Ordner/Laufwerke es gibt) und je Spiel ein <c>appmanifest_&lt;AppID&gt;.acf</c> (Name, AppID, Zustand).
/// Es wird ausschliesslich gelesen; gestartet wird spaeter wie immer ueber <see cref="Starter.OpenSteam"/>.
/// </summary>
public static class SteamScanner
{
    /// <summary>Vermerk in der Bibliothek, damit man sieht, woher ein Eintrag kommt.</summary>
    public const string ImportNote = "via Steam-Scan";

    public const int MaxNameLength = 120;

    private const int MaxLibraries = 32;
    private const int MaxManifestsPerLibrary = 20_000;
    private const int StateFullyInstalled = 4;
    private const string ManifestPrefix = "appmanifest_";

    // ------------------------------------------------------------------
    // Steam finden
    // ------------------------------------------------------------------

    /// <summary>
    /// Sucht den Steam-Ordner: erst <paramref name="preferred"/> (eine Auswahl des Benutzers), dann die Angaben,
    /// die Steam selbst in die Registrierung schreibt, zuletzt die Standardordner. <c>null</c> = Steam nicht gefunden.
    /// </summary>
    public static string? FindSteamRoot(string? preferred = null)
    {
        foreach (var candidate in Candidates(preferred))
            if (IsSteamRoot(candidate)) return NormalizeFolder(candidate);
        return null;
    }

    /// <summary>Liegt dort ein Steam? (<c>steam.exe</c> oder ein Ordner <c>steamapps</c>.)</summary>
    public static bool IsSteamRoot(string? path)
    {
        var folder = NormalizeFolder(path);
        return folder is not null &&
               (Directory.Exists(Path.Combine(folder, "steamapps")) || File.Exists(Path.Combine(folder, "steam.exe")));
    }

    private static IEnumerable<string?> Candidates(string? preferred)
    {
        yield return preferred;
        if (OperatingSystem.IsWindows())
        {
            yield return RegistryText(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath", RegistryView.Default);
            yield return RegistryText(RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath", RegistryView.Registry32);
            yield return RegistryText(RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath", RegistryView.Registry64);
        }
        foreach (var programs in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            var folder = Environment.GetFolderPath(programs);
            if (folder.Length > 0) yield return Path.Combine(folder, "Steam");
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? RegistryText(RegistryHive hive, string subKey, string name, RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(name) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;   // kein Zugriff auf die Registrierung: dann eben die naechste Quelle
        }
    }

    // ------------------------------------------------------------------
    // Suchen
    // ------------------------------------------------------------------

    /// <summary>Durchsucht Steam samt allen eingetragenen Bibliotheksordnern. <c>null</c> oder ein fehlender Ordner ergibt <see cref="SteamScan.NotFound"/>.</summary>
    public static SteamScan Scan(string? steamRoot)
    {
        var root = NormalizeFolder(steamRoot);
        if (root is null || !Directory.Exists(root)) return SteamScan.NotFound;

        var libraries = new List<string>();
        var unreachable = new List<string>();
        foreach (var folder in LibraryFolders(root))
            (Directory.Exists(Path.Combine(folder, "steamapps")) ? libraries : unreachable).Add(folder);

        var games = new Dictionary<string, SteamGame>(StringComparer.Ordinal);
        int tools = 0, damaged = 0;
        foreach (var library in libraries)
        {
            foreach (var file in Manifests(library))
            {
                var read = ReadManifest(library, file);
                if (read.Tool) { tools++; continue; }
                if (read.Game is not { } game) { damaged++; continue; }

                // Dieselbe AppID in zwei Ordnern (Reste einer Verschiebung): die vollstaendige gewinnt, sonst die erste.
                if (!games.TryGetValue(game.AppId, out var known) || (!known.Complete && game.Complete))
                    games[game.AppId] = game;
            }
        }

        return new SteamScan(root, libraries, unreachable,
            games.Values
                .OrderBy(g => g.Name, StringComparer.InvariantCultureIgnoreCase)
                .ThenBy(g => g.AppId, StringComparer.Ordinal)
                .ToList(),
            tools, damaged);
    }

    /// <summary>
    /// Alle Bibliotheksordner von Steam. Neue Steam-Versionen tragen sie als Block mit <c>"path"</c> ein, alte als
    /// einfachen Text; beides wird gelesen. Der Steam-Ordner selbst gehoert immer dazu.
    /// </summary>
    internal static List<string> LibraryFolders(string steamRoot)
    {
        var found = new List<string>();

        void Add(string? raw)
        {
            var folder = NormalizeFolder(raw);
            if (folder is null || found.Count >= MaxLibraries) return;
            if (!found.Contains(folder, StringComparer.OrdinalIgnoreCase)) found.Add(folder);
        }

        foreach (var file in new[]
                 {
                     Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
                     Path.Combine(steamRoot, "config", "libraryfolders.vdf"),   // aeltere Steam-Versionen
                 })
        {
            if (!Vdf.TryLoad(file, out var doc) || doc.Find("libraryfolders") is not { IsBlock: true } folders) continue;
            foreach (var entry in folders.Children)
            {
                // Nur die nummerierten Eintraege sind Ordner ("0", "1", ...); daneben stehen z. B. "ContentStatsID".
                if (entry.Key.Length == 0 || !entry.Key.All(char.IsAsciiDigit)) continue;
                Add(entry.IsBlock ? entry.GetString("path") : entry.Value);
            }
        }

        Add(steamRoot);
        return found;
    }

    private static List<string> Manifests(string library)
    {
        try
        {
            return Directory.EnumerateFiles(Path.Combine(library, "steamapps"), ManifestPrefix + "*.acf")
                .Order(StringComparer.OrdinalIgnoreCase)
                .Take(MaxManifestsPerLibrary)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];   // Laufwerk waehrend der Suche abgesteckt o. ae.
        }
    }

    private readonly record struct ManifestRead(SteamGame? Game, bool Tool);

    private static ManifestRead ReadManifest(string library, string file)
    {
        if (!Vdf.TryLoad(file, out var doc) || doc.Find("AppState") is not { IsBlock: true } state)
            return new ManifestRead(null, false);

        // Die AppID steht im Dateinamen und in der Datei; die Angabe in der Datei gilt, der Name ist nur der Notnagel.
        var stem = Path.GetFileNameWithoutExtension(file);
        var fromName = stem.Length > ManifestPrefix.Length ? NormalizeAppId(stem[ManifestPrefix.Length..]) : null;
        var appId = NormalizeAppId(state.GetString("appid")) ?? fromName;
        if (appId is null) return new ManifestRead(null, false);

        var installDir = CleanFolderName(state.GetString("installdir"));
        var name = CleanName(state.GetString("name"));
        if (name.Length == 0) name = installDir.Length > 0 ? installDir : "Steam-App " + appId;

        if (IsTool(appId, name, library, installDir)) return new ManifestRead(null, true);

        _ = int.TryParse(state.GetString("StateFlags"), NumberStyles.None, CultureInfo.InvariantCulture, out var flags);
        _ = long.TryParse(state.GetString("SizeOnDisk"), NumberStyles.None, CultureInfo.InvariantCulture, out var size);
        return new ManifestRead(new SteamGame(appId, name, installDir, library, size, (flags & StateFullyInstalled) != 0), false);
    }

    /// <summary>
    /// Steam-Laufzeiten liegen als "Spiel" im selben Ordner, sind aber keines: die gemeinsamen Bibliotheken jedes
    /// Spiels (Steamworks Common Redistributables) und - unter Linux - Proton und die Steam Linux Runtimes.
    /// Bei Proton entscheidet die <c>toolmanifest.vdf</c>, damit ein Spiel, das nur so heisst, nicht verschwindet.
    /// </summary>
    private static bool IsTool(string appId, string name, string library, string installDir)
    {
        if (appId == "228980") return true;
        if (name.StartsWith("Steamworks Common Redistributables", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase)) return true;
        return name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase) && installDir.Length > 0 &&
               File.Exists(Path.Combine(library, "steamapps", "common", installDir, "toolmanifest.vdf"));
    }

    // ------------------------------------------------------------------
    // In die Bibliothek uebernehmen
    // ------------------------------------------------------------------

    /// <summary>
    /// AppIDs, die die Bibliothek schon kennt. Auch ein Eintrag, dessen Wert ein Store-Link statt der nackten
    /// Zahl ist, zaehlt (wie beim Starten, siehe <see cref="SteamApps.TryResolveAppId"/>).
    /// </summary>
    public static HashSet<string> KnownAppIds(IEnumerable<LibraryEntry> library)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in library)
        {
            if (!entry.Kind.Equals("steam", StringComparison.OrdinalIgnoreCase)) continue;
            if (SteamApps.TryResolveAppId(entry.Value, out var id) && NormalizeAppId(id) is { } normalized) ids.Add(normalized);
        }
        return ids;
    }

    /// <summary>
    /// Haengt die Spiele an die Bibliothek an, die dort noch fehlen. Bestehende Eintraege bleiben unangetastet
    /// (auch ein selbst geaenderter Name oder eine Notiz); jede AppID kommt hoechstens einmal dazu.
    /// </summary>
    /// <param name="added">Datum fuer die Spalte "Added" (wie bei den anderen Eintraegen <c>yyyy-MM-dd</c>).</param>
    /// <param name="count">So viele Eintraege sind neu.</param>
    public static IReadOnlyList<LibraryEntry> AddToLibrary(
        IEnumerable<LibraryEntry> library, IEnumerable<SteamGame> games, string added, out int count)
    {
        var list = library.ToList();
        var known = KnownAppIds(list);
        count = 0;
        foreach (var game in games)
        {
            if (!known.Add(game.AppId)) continue;
            list.Add(new LibraryEntry(game.Name, "steam", game.AppId, added, ImportNote));
            count++;
        }
        return list;
    }

    /// <summary>
    /// Traegt die Spiele in die library.csv ein. Die Datei wird dafuer frisch gelesen (nicht der Stand der
    /// Oberflaeche), damit nichts ueberschrieben wird, was ein anderes Programm inzwischen geaendert hat; vor dem
    /// Schreiben gibt es eine Sicherung (<c>library.csv.bak</c>). Gibt zurueck, wie viele Eintraege neu sind -
    /// 0 heisst: nichts war neu, die Datei blieb unberuehrt.
    /// </summary>
    public static int AddToLibraryFile(string libraryFile, IEnumerable<SteamGame> games, string added)
    {
        var current = File.Exists(libraryFile) ? LibraryCsv.Read(libraryFile) : [];
        var merged = AddToLibrary(current, games, added, out var count);
        if (count == 0) return 0;

        FloppyPaths.EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(libraryFile))!);
        LibraryCsv.Backup(libraryFile);
        LibraryCsv.Write(libraryFile, merged);
        return count;
    }

    // ------------------------------------------------------------------
    // Kleinkram
    // ------------------------------------------------------------------

    /// <summary>AppID als Zahl ohne fuehrende Nullen; <c>null</c>, wenn es keine gueltige (positive, 32-Bit-) Zahl ist.</summary>
    public static string? NormalizeAppId(string? text)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t) || t.Length > 10 || !t.All(char.IsAsciiDigit)) return null;
        return uint.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// Anzeigename: Steuer-, Format- und Zeilentrenner-Zeichen werden zu Leerzeichen, Leerraum wird zusammengezogen,
    /// bei <see cref="MaxNameLength"/> ist Schluss. (Ein Name landet in library.csv und in Listen - er darf nichts anstellen.)
    /// </summary>
    internal static string CleanName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var sb = new StringBuilder(Math.Min(raw.Length, MaxNameLength + 1));
        var pendingSpace = false;
        foreach (var ch in raw)
        {
            var blank = char.IsWhiteSpace(ch) || char.IsControl(ch) ||
                        CharUnicodeInfo.GetUnicodeCategory(ch) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
            if (blank)
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace) sb.Append(' ');
            pendingSpace = false;
            sb.Append(ch);
        }

        if (sb.Length > MaxNameLength)
        {
            sb.Length = MaxNameLength;
            if (char.IsHighSurrogate(sb[^1])) sb.Length--;   // kein halbes Zeichen stehen lassen
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Ein Ordnername, kein Pfad: sonst leer (der Ordner wird nur zum Nachsehen gebraucht, nie zum Starten).</summary>
    private static string CleanFolderName(string? raw)
    {
        var name = (raw ?? "").Trim();
        if (name.Length == 0 || name.Length > 240 || name is "." or "..") return "";
        return name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? "" : name;
    }

    /// <summary>Vollstaendiger Ordnerpfad ohne Endstrich (ein Laufwerk wie <c>D:\</c> behaelt ihn); <c>null</c> bei ungueltigem Pfad.</summary>
    private static string? NormalizeFolder(string? raw)
    {
        var text = PathRules.StripQuotes(raw);
        if (text.Length == 0 || PathRules.TryGetFullPath(text) is not { } full) return null;
        var root = Path.GetPathRoot(full) ?? "";
        return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }
}
