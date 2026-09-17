namespace Floppy.Core;

/// <summary>
/// Entscheidet, was mit einer eingelegten Diskette passiert.
/// 1:1-Portierung von Get-LaunchPlan aus FloppyLauncher.ps1 (V1), damit sich
/// Konsole und App exakt gleich verhalten. Reihenfolge:
/// Steam-ID -&gt; Hub -&gt; pcrun= -&gt; run= -&gt; Suche nach ausfuehrbaren Dateien.
/// </summary>
public sealed class LaunchPlanner(FloppyOptions options)
{
    /// <summary>So tief wird ohne Referenzdatei nach EXE gesucht (wie Get-ChildItem -Depth 2).</summary>
    public const int SearchDepth = 2;

    /// <summary>Hoechstens so viele Treffer werden angeboten.</summary>
    public const int MaxCandidates = 20;

    private readonly FloppyOptions _o = options ?? throw new ArgumentNullException(nameof(options));

    public PlanResult Plan(string root)
    {
        var log = new List<PlanMessage>();
        var plan = BuildPlan(root, log);
        return new PlanResult(plan, log);
    }

    private LaunchPlan? BuildPlan(string root, List<PlanMessage> log)
    {
        var refPath = ReferenceFile.Find(root, _o.ReferenceFileNames);
        if (refPath is not null)
        {
            IReadOnlyDictionary<string, string> map;
            try { map = ReferenceFile.Load(refPath); }
            catch (Exception ex)
            {
                log.Add(PlanMessage.Of(MessageLevel.Error, "REF_UNREADABLE", $"Referenzdatei nicht lesbar: {refPath} ({ex.Message})", refPath, ex.Message));
                return null;
            }

            // --- Steam-ID ---
            var steamId = ReferenceFile.FirstValue(map, ReferenceFile.SteamKeys);
            if (steamId is not null)
            {
                if (!IsDigits(steamId))
                {
                    log.Add(PlanMessage.Of(MessageLevel.Error, "STEAM_INVALID", $"Ungueltige Steam-ID in {refPath}: '{steamId}'", refPath, steamId));
                    return null;
                }
                return new LaunchPlan(LaunchKind.Steam, steamId, [], null, false, refPath);
            }

            // --- Hub-Diskette ---
            if (_o.HubEnabled && ReferenceFile.FirstValue(map, _o.HubKeys) is not null)
            {
                log.Add(PlanMessage.Of(MessageLevel.Ok, "HUB_DISC", "Hub-Diskette erkannt - oeffne Floppy Hub."));
                return new LaunchPlan(LaunchKind.Hub, null, [], null, false, refPath);
            }

            // --- Minispiel-Diskette (App-Variante): nur ein Levelpaket, nie Programmcode ---
            var gameRef = ReferenceFile.FirstValue(map, ReferenceFile.GameKeys);
            if (gameRef is not null)
            {
                var pack = ResolveOnDrive(root, gameRef, log, Minigame.LevelPack.Extensions, "minigame");
                if (pack is null) return null;
                log.Add(PlanMessage.Of(MessageLevel.Ok, "GAME_DISC", "Minispiel-Diskette erkannt - oeffne Floppy Hub."));
                return new LaunchPlan(LaunchKind.Game, null, [pack], null, false, refPath);
            }

            map.TryGetValue(ReferenceFile.ArgsKey, out var args);

            // --- EXE auf dem PC: immer mit Rueckfrage ---
            var pcRef = ReferenceFile.FirstValue(map, ReferenceFile.PcRunKeys);
            if (pcRef is not null)
            {
                var pcPath = ResolvePcPath(pcRef, log);
                return pcPath is null
                    ? null
                    : new LaunchPlan(LaunchKind.Process, null, [pcPath], args, true, refPath);
            }

            // --- EXE auf der Diskette: startet sofort ---
            var runRef = ReferenceFile.FirstValue(map, ReferenceFile.RunKeys);
            if (runRef is not null)
            {
                var exePath = ResolveOnDrive(root, runRef, log);
                return exePath is null
                    ? null
                    : new LaunchPlan(LaunchKind.Process, null, [exePath], args, false, refPath);
            }

            log.Add(PlanMessage.Of(MessageLevel.Warn, "REF_NOTHING", $"Referenzdatei {refPath} enthaelt keine verwertbare Angabe (id= / run= / pcrun=).", refPath));
        }

        // --- Keine (brauchbare) Referenzdatei: ausfuehrbare Dateien suchen ---
        var found = FindExecutables(root).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Take(MaxCandidates).ToArray();
        if (found.Length == 0) return null;
        if (found.Length == 1) return new LaunchPlan(LaunchKind.Process, null, found, null, false, "EXE-Suche");

        var duplicateNames = found.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
        log.Add(duplicateNames
            ? PlanMessage.Of(MessageLevel.Warn, "EXE_DUPLICATES", "Gleicher Dateiname mehrfach auf der Diskette - jede EXE wird einzeln abgefragt.")
            : PlanMessage.Of(MessageLevel.Warn, "EXE_MULTIPLE", "Mehrere ausfuehrbare Dateien auf der Diskette - jede wird einzeln abgefragt."));
        return new LaunchPlan(LaunchKind.Process, null, found, null, true, "EXE-Suche");
    }

    /// <summary>run=: Pfad relativ zur Diskette, muss auf der Diskette bleiben.</summary>
    public string? ResolveOnDrive(string root, string value, List<PlanMessage> log) =>
        ResolveOnDrive(root, value, log, _o.ExecutableExtensions, "run");

    /// <summary>Datei relativ zur Diskette (run= oder minigame=), muss auf der Diskette bleiben.</summary>
    private static string? ResolveOnDrive(string root, string value, List<PlanMessage> log, IReadOnlyList<string> extensions, string key)
    {
        var rel = PathRules.StripQuotes(value);
        if (rel.Length == 0)
        {
            log.Add(PlanMessage.Of(MessageLevel.Error, "KEY_EMPTY", $"{key}= ist leer.", key));
            return null;
        }
        if (PathRules.IsRooted(rel))
        {
            log.Add(key == "run"
                ? PlanMessage.Of(MessageLevel.Error, "RUN_ROOTED", $"run= erwartet einen Pfad relativ zur Diskette. Fuer PC-Pfade bitte pcrun= verwenden: {rel}", rel)
                : PlanMessage.Of(MessageLevel.Error, "KEY_ROOTED", $"{key}= erwartet einen Pfad relativ zur Diskette: {rel}", key, rel));
            return null;
        }

        var full = PathRules.TryGetFullPath(Path.Combine(root, rel));
        if (full is null) return null;

        if (!PathRules.IsUnder(full, root))
        {
            log.Add(PlanMessage.Of(MessageLevel.Warn, "KEY_OUTSIDE", $"{key}= zeigt aus der Diskette heraus, ignoriert: {rel}", key, rel));
            return null;
        }
        if (!File.Exists(full))
        {
            log.Add(PlanMessage.Of(MessageLevel.Error, "KEY_MISSING", $"{key}=-Datei fehlt auf der Diskette: {rel}", key, rel));
            return null;
        }
        if (!PathRules.HasExecutableExtension(full, extensions))
        {
            log.Add(key == "run"
                ? PlanMessage.Of(MessageLevel.Error, "RUN_NOT_EXE", $"run=-Datei ist nicht ausfuehrbar: {rel}", rel)
                : PlanMessage.Of(MessageLevel.Error, "KEY_WRONG_EXT", $"{key}=-Datei hat die falsche Endung (erlaubt: {string.Join(", ", extensions)}): {rel}",
                    key, string.Join(", ", extensions), rel));
            return null;
        }
        return full;
    }

    /// <summary>pcrun=: absoluter PC-Pfad, geprueft gegen Sperr- und Positivliste.</summary>
    public string? ResolvePcPath(string value, List<PlanMessage> log)
    {
        var input = PathRules.StripQuotes(value);
        if (input.Length == 0)
        {
            log.Add(PlanMessage.Of(MessageLevel.Error, "KEY_EMPTY", "pcrun= ist leer.", "pcrun"));
            return null;
        }
        if (!PathRules.IsRooted(input))
        {
            log.Add(PlanMessage.Of(MessageLevel.Error, "PCRUN_NOT_ABSOLUTE", $"pcrun= benoetigt einen absoluten Pfad (z. B. C:\\Spiele\\x.exe): {input}", input));
            return null;
        }

        var full = PathRules.TryGetFullPath(input);
        if (full is null)
        {
            log.Add(PlanMessage.Of(MessageLevel.Error, "PCRUN_INVALID", $"pcrun= ist kein gueltiger Pfad: {input}", input));
            return null;
        }
        if (!File.Exists(full))
        {
            log.Add(PlanMessage.Of(MessageLevel.Error, "PCRUN_NOT_FOUND", $"pcrun=-Datei nicht gefunden: {full}", full));
            return null;
        }
        if (!PathRules.HasExecutableExtension(full, _o.ExecutableExtensions))
        {
            log.Add(PlanMessage.Of(MessageLevel.Error, "PCRUN_NOT_EXE", $"pcrun=-Datei ist nicht ausfuehrbar: {full}", full));
            return null;
        }
        foreach (var blocked in _o.BlockedRoots.Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            if (PathRules.IsUnder(full, blocked))
            {
                log.Add(PlanMessage.Of(MessageLevel.Error, "PCRUN_BLOCKED", $"pcrun= liegt in einem gesperrten Systemordner ({blocked}) und wird abgelehnt: {full}", blocked, full));
                return null;
            }
        }
        var allowed = _o.AllowedRoots.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
        if (allowed.Length > 0 && !allowed.Any(a => PathRules.IsUnder(full, a)))
        {
            log.Add(PlanMessage.Of(MessageLevel.Error, "PCRUN_NOT_ALLOWED", $"pcrun= liegt ausserhalb der erlaubten Ordner und wird abgelehnt: {full}", full));
            return null;
        }
        return full;
    }

    /// <summary>Ausfuehrbare Dateien im Wurzelverzeichnis und bis zu <see cref="SearchDepth"/> Unterordner-Ebenen.</summary>
    public IEnumerable<string> FindExecutables(string root)
    {
        var result = new List<string>();
        Walk(root, 0);
        return result;

        void Walk(string dir, int depth)
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                    if (PathRules.HasExecutableExtension(f, _o.ExecutableExtensions)) result.Add(f);
            }
            catch { /* z. B. "System Volume Information" - ueberspringen */ }

            if (depth >= SearchDepth) return;
            try
            {
                foreach (var d in Directory.EnumerateDirectories(dir)) Walk(d, depth + 1);
            }
            catch { }
        }
    }

    private static bool IsDigits(string s) => s.Length > 0 && s.All(char.IsAsciiDigit);
}
