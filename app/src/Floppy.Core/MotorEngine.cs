namespace Floppy.Core;

/// <summary>Was der Motor nach aussen tut - austauschbar fuer Tests und Testbetrieb.</summary>
public interface IMotorActions
{
    void OpenSteam(string appId);
    void StartProgram(string path, string? arguments);

    /// <summary>App wecken oder starten. false = keine App vorhanden.</summary>
    bool WakeApp(string command);

    /// <summary>Notfall-Rueckfrage, wenn keine App installiert ist. true = einmal starten.</summary>
    bool AskWithoutApp(string target, string? arguments, string reason);
}

/// <summary>
/// Herz von FloppyLauncher.exe: schaut alle PollSeconds auf Laufwerk A: (Standard) und,
/// nur in der App, auf bis zu zwei weitere ausgewaehlte Laufwerke (<see cref="FloppyOptions.ExtraDriveLetters"/>),
/// wertet neue Disketten aus und entscheidet ueber die Vertrauensliste.
/// Meldungen im selben Wortlaut wie V1, damit Logs vergleichbar bleiben.
/// </summary>
public sealed class MotorEngine
{
    private readonly FloppyOptions _options;
    private readonly LogFile _log;
    private readonly TrustStore _trust;
    private readonly IMotorActions _actions;
    private readonly LaunchPlanner _planner;
    private readonly bool _dryRun;
    private readonly string? _guardDir;

    /// <param name="rootOverride">Nur EIN Testordner statt der echten Laufwerke (z. B. --drive, Tests).</param>
    /// <param name="watchers">Fertige Watcher statt aus <paramref name="rootOverride"/>/options gebaut (Tests mit mehreren Laufwerken).</param>
    /// <param name="guardDir">Ordner fuer <see cref="WriteGuard"/>-Notizen der App (meist <see cref="FloppyPaths.UserData"/>).</param>
    public MotorEngine(FloppyOptions options, LogFile log, TrustStore trust, IMotorActions actions,
        string? rootOverride = null, bool dryRun = false, IReadOnlyList<DiscWatcher>? watchers = null, string? guardDir = null)
    {
        _options = options;
        _log = log;
        _trust = trust;
        _actions = actions;
        _dryRun = dryRun;
        _guardDir = guardDir;
        _planner = new LaunchPlanner(options);
        Watchers = watchers ?? BuildWatchers(rootOverride, options);
    }

    /// <summary>Ein Watcher pro ueberwachtem Laufwerk - normalerweise A: plus die gewaehlten Extras.</summary>
    public IReadOnlyList<DiscWatcher> Watchers { get; }

    private static IReadOnlyList<DiscWatcher> BuildWatchers(string? rootOverride, FloppyOptions options)
    {
        if (rootOverride is not null) return [new DiscWatcher(rootOverride)];

        var roots = new List<string> { options.DriveRoot };
        foreach (var root in options.ExtraDriveRoots)
            if (!roots.Contains(root, StringComparer.OrdinalIgnoreCase)) roots.Add(root);
        return roots.Select(r => new DiscWatcher(r)).ToArray();
    }

    /// <summary>Laeuft, bis <paramref name="stop"/> gesetzt wird (oder genau einmal).</summary>
    public void Run(WaitHandle stop, bool once = false)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollSeconds));
        do
        {
            Tick();
            if (once) return;
        }
        while (!stop.WaitOne(interval));
    }

    /// <summary>Ein Durchgang der Hauptschleife ueber alle Watcher. Wirft nie.</summary>
    public GateDecision? Tick()
    {
        try
        {
            GateDecision? last = null;
            foreach (var watcher in Watchers)
                if (watcher.Poll()) last = HandleDisc(watcher);
            return last;
        }
        catch (Exception ex)
        {
            _log.Write(LogLevel.Error, $"Unerwarteter Fehler: {ex.Message}");
            return null;
        }
    }

    public GateDecision HandleDisc(DiscWatcher watcher)
    {
        switch (_guardDir is null ? GuardState.None : WriteGuard.Check(_guardDir, watcher.LastSignature))
        {
            case GuardState.Pending:
                watcher.Forget();   // App schreibt noch - beim naechsten Durchgang erneut ansehen
                return new GateDecision(GateAction.None, null, null, TrustState.Unknown, "App schreibt gerade");
            case GuardState.Written:
                _log.Write(LogLevel.Info, "Diskette wurde gerade von der Floppy Hub App beschrieben - wird nicht gestartet.");
                return new GateDecision(GateAction.None, null, null, TrustState.Unknown, "von der App beschrieben");
        }

        PlanResult result;
        try
        {
            result = _planner.Plan(watcher.Root);
        }
        catch (Exception ex)
        {
            _log.Write(LogLevel.Error, $"Fehler beim Auswerten der Diskette: {ex.Message}");
            return new GateDecision(GateAction.None, null, null, TrustState.Unknown, ex.Message);
        }

        foreach (var message in result.Messages) _log.Write(message);

        var decision = LaunchGate.Decide(result.Plan, _trust.Check);
        try
        {
            Execute(decision);
        }
        catch (Exception ex)
        {
            _log.Write(LogLevel.Error, $"Start fehlgeschlagen: {ex.Message}");
        }
        return decision;
    }

    private void Execute(GateDecision d)
    {
        var plan = d.Plan;
        switch (d.Action)
        {
            case GateAction.StartSteam when plan?.SteamId is { } id:
                if (_dryRun) { _log.Write(LogLevel.Ok, $"[DRYRUN] wuerde starten: {SteamApps.RunUri(id)}"); return; }
                _log.Write(LogLevel.Ok, $"Steam-ID {id} gefunden. Starte Steam...");
                _actions.OpenSteam(id);
                return;

            case GateAction.OpenHub:
                if (_dryRun) { _log.Write(LogLevel.Ok, "[DRYRUN] wuerde die Floppy Hub App oeffnen."); return; }
                if (!_actions.WakeApp(HubPipe.Hub))
                    _log.Write(LogLevel.Warn, "Hub-Diskette erkannt, aber die Floppy Hub App ist nicht installiert.");
                return;

            case GateAction.OpenGame:
                if (_dryRun) { _log.Write(LogLevel.Ok, "[DRYRUN] wuerde das Minispiel in der Floppy Hub App oeffnen."); return; }
                if (!_actions.WakeApp(HubPipe.Game))
                    _log.Write(LogLevel.Warn, "Minispiel-Diskette erkannt, aber die Floppy Hub App ist nicht installiert.");
                return;

            case GateAction.Start when d.Target is not null:
                if (_dryRun) { _log.Write(LogLevel.Ok, $"[DRYRUN] wuerde starten (freigegeben): {d.Target} {plan?.Arguments}".TrimEnd()); return; }
                _log.Write(LogLevel.Ok, $"Freigegeben - starte Programm: {d.Target} {plan?.Arguments}".TrimEnd());
                _actions.StartProgram(d.Target, plan?.Arguments);
                return;

            case GateAction.Ask when plan is not null:
                Ask(d, plan);
                return;
        }
    }

    private void Ask(GateDecision d, LaunchPlan plan)
    {
        var what = d.Target ?? string.Join(" | ", plan.Candidates);
        if (_dryRun)
        {
            _log.Write(LogLevel.Ask, $"[DRYRUN] wuerde nachfragen ({d.Reason}): {what}");
            return;
        }
        if (_options.NonInteractive)
        {
            _log.Write(LogLevel.Warn, $"Rueckfrage noetig ({d.Reason}), aber non_interactive = true - nicht gestartet: {what}");
            return;
        }

        _log.Write(LogLevel.Ask, $"Rueckfrage ({d.Reason}): {what}");
        if (_actions.WakeApp(HubPipe.Confirm)) return;

        // Keine App installiert: einfache Windows-Rueckfrage, nur fuer EIN Programm.
        if (d.Target is null)
        {
            _log.Write(LogLevel.Error, "Mehrere Programme, aber keine Floppy Hub App zum Auswaehlen - nichts gestartet.");
            return;
        }
        if (_actions.AskWithoutApp(d.Target, plan.Arguments, d.Reason) && File.Exists(d.Target))
        {
            _log.Write(LogLevel.Ok, $"Bestaetigt (einmalig) - starte Programm: {d.Target} {plan.Arguments}".TrimEnd());
            _actions.StartProgram(d.Target, plan.Arguments);
        }
        else
        {
            _log.Write(LogLevel.Info, "Kein Programm gestartet (nichts bestaetigt).");
        }
    }
}
