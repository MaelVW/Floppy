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
                log.Add(new(MessageLevel.Error, $"Referenzdatei nicht lesbar: {refPath} ({ex.Message})"));
                return null;
            }

            // --- Steam-ID ---
            var steamId = ReferenceFile.FirstValue(map, ReferenceFile.SteamKeys);
            if (steamId is not null)
            {
                if (!IsDigits(steamId))
                {
                    log.Add(new(MessageLevel.Error, $"Ungueltige Steam-ID in {refPath}: '{steamId}'"));
                    return null;
                }
                return new LaunchPlan(LaunchKind.Steam, steamId, [], null, false, refPath);
            }

            // --- Hub-Diskette ---
            if (_o.HubEnabled && ReferenceFile.FirstValue(map, _o.HubKeys) is not null)
            {
                log.Add(new(MessageLevel.Ok, "Hub-Diskette erkannt - oeffne Floppy Hub."));
                return new LaunchPlan(LaunchKind.Hub, null, [], null, false, refPath);
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

            log.Add(new(MessageLevel.Warn, $"Referenzdatei {refPath} enthaelt keine verwertbare Angabe (id= / run= / pcrun=)."));
        }

        // --- Keine (brauchbare) Referenzdatei: ausfuehrbare Dateien suchen ---
        var found = FindExecutables(root).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Take(MaxCandidates).ToArray();
        if (found.Length == 0) return null;
        if (found.Length == 1) return new LaunchPlan(LaunchKind.Process, null, found, null, false, "EXE-Suche");

        var duplicateNames = found.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
        log.Add(new(MessageLevel.Warn, duplicateNames
            ? "Gleicher Dateiname mehrfach auf der Diskette - jede EXE wird einzeln abgefragt."
            : "Mehrere ausfuehrbare Dateien auf der Diskette - jede wird einzeln abgefragt."));
        return new LaunchPlan(LaunchKind.Process, null, found, null, true, "EXE-Suche");
    }

    /// <summary>run=: Pfad relativ zur Diskette, muss auf der Diskette bleiben.</summary>
    public string? ResolveOnDrive(string root, string value, List<PlanMessage> log)
    {
        var rel = PathRules.StripQuotes(value);
        if (rel.Length == 0)
        {
            log.Add(new(MessageLevel.Error, "run= ist leer."));
            return null;
        }
        if (PathRules.IsRooted(rel))
        {
            log.Add(new(MessageLevel.Error, $"run= erwartet einen Pfad relativ zur Diskette. Fuer PC-Pfade bitte pcrun= verwenden: {rel}"));
            return null;
        }

        var full = PathRules.TryGetFullPath(Path.Combine(root, rel));
        if (full is null) return null;

        if (!PathRules.IsUnder(full, root))
        {
            log.Add(new(MessageLevel.Warn, $"run= zeigt aus der Diskette heraus, ignoriert: {rel}"));
            return null;
        }
        if (!File.Exists(full))
        {
            log.Add(new(MessageLevel.Error, $"run=-Datei fehlt auf der Diskette: {rel}"));
            return null;
        }
        if (!PathRules.HasExecutableExtension(full, _o.ExecutableExtensions))
        {
            log.Add(new(MessageLevel.Error, $"run=-Datei ist nicht ausfuehrbar: {rel}"));
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
            log.Add(new(MessageLevel.Error, "pcrun= ist leer."));
            return null;
        }
        if (!PathRules.IsRooted(input))
        {
            log.Add(new(MessageLevel.Error, $"pcrun= benoetigt einen absoluten Pfad (z. B. C:\\Spiele\\x.exe): {input}"));
            return null;
        }

        var full = PathRules.TryGetFullPath(input);
        if (full is null)
        {
            log.Add(new(MessageLevel.Error, $"pcrun= ist kein gueltiger Pfad: {input}"));
            return null;
        }
        if (!File.Exists(full))
        {
            log.Add(new(MessageLevel.Error, $"pcrun=-Datei nicht gefunden: {full}"));
            return null;
        }
        if (!PathRules.HasExecutableExtension(full, _o.ExecutableExtensions))
        {
            log.Add(new(MessageLevel.Error, $"pcrun=-Datei ist nicht ausfuehrbar: {full}"));
            return null;
        }
        foreach (var blocked in _o.BlockedRoots.Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            if (PathRules.IsUnder(full, blocked))
            {
                log.Add(new(MessageLevel.Error, $"pcrun= liegt in einem gesperrten Systemordner ({blocked}) und wird abgelehnt: {full}"));
                return null;
            }
        }
        var allowed = _o.AllowedRoots.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
        if (allowed.Length > 0 && !allowed.Any(a => PathRules.IsUnder(full, a)))
        {
            log.Add(new(MessageLevel.Error, $"pcrun= liegt ausserhalb der erlaubten Ordner und wird abgelehnt: {full}"));
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
