using System.Reflection;
using System.Runtime.Versioning;
using Floppy.Core;

// Der Motor ist ein reines Windows-Programm (Laufwerk A:, Autostart, MessageBox).
[assembly: SupportedOSPlatform("windows")]

namespace FloppyLauncher;

internal static class Program
{
    /// <summary>
    /// Gleiche Sperre wie FloppyLauncher.ps1 (V1): Konsole und Motor koennen nie
    /// gleichzeitig laufen - sonst wuerde jede Diskette doppelt starten.
    /// </summary>
    private const string MutexName = @"Local\FloppyLauncherSingleInstance";

    /// <summary>Signal zum sauberen Beenden (FloppyLauncher.exe --stop, z. B. vom Setup).</summary>
    private const string StopEventName = @"Local\FloppyLauncherStop";

    private static int Main(string[] argv)
    {
        var args = MotorArgs.Parse(argv);
        if (args.Stop) return StopRunningMotor();

        Starter.SuppressDriveErrorDialogs();

        var paths = new FloppyPaths(args.Home ?? AppContext.BaseDirectory, args.UserData);
        var options = paths.LoadOptions(out var iniProblem);
        var log = new LogFile(args.LogFile ?? paths.ResolveLogFile(options), options.LogMaxKb);
        if (args.Console) log.Echo = System.Console.Out.WriteLine;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.Write(LogLevel.Error, $"Absturz: {(e.ExceptionObject as Exception)?.Message ?? e.ExceptionObject}");

        Mutex? mutex = null;
        if (!args.AllowMultiple && !args.Once)
        {
            mutex = new Mutex(initiallyOwned: true, MutexName, out var isNew);
            if (!isNew)
            {
                log.Write(LogLevel.Warn, "Es laeuft bereits ein Floppy Launcher (Konsole oder Motor). Dieser Start wird beendet.");
                mutex.Dispose();
                return 1;
            }
        }

        try
        {
            using var stop = new EventWaitHandle(false, EventResetMode.AutoReset, StopEventName);
            var root = args.Drive ?? options.DriveRoot;

            var mode = new List<string>();
            if (args.DryRun) mode.Add("DryRun");
            if (args.Once) mode.Add("Once");
            if (options.NonInteractive) mode.Add("NonInteractive");
            if (args.Drive is not null) mode.Add("Testordner");
            var modeText = mode.Count > 0 ? $" [{string.Join(", ", mode)}]" : "";

            log.Write(LogLevel.Info, $"Floppy Launcher (Motor) gestartet. Warte auf Diskette in {root} ...{modeText}");
            log.Write(LogLevel.Info, $"Motor {Version} | .NET {Environment.Version} | Programmordner: {paths.Home}");
            if (iniProblem is not null) log.Write(LogLevel.Warn, iniProblem);
            foreach (var problem in args.Problems) log.Write(LogLevel.Warn, problem);
            foreach (var problem in CheckRootLists(options)) log.Write(LogLevel.Warn, problem);

            var trust = new TrustStore(paths.TrustFile);
            var engine = new MotorEngine(options, log, trust, new SystemActions(paths, args, log), root, args.DryRun,
                guardDir: paths.UserData);
            engine.Run(stop, args.Once);

            log.Write(LogLevel.Info, "Floppy Launcher (Motor) beendet.");
            return 0;
        }
        finally
        {
            if (mutex is not null)
            {
                try { mutex.ReleaseMutex(); } catch { }
                mutex.Dispose();
            }
        }
    }

    private static int StopRunningMotor()
    {
        if (!EventWaitHandle.TryOpenExisting(StopEventName, out var handle)) return 2;   // laeuft nicht
        using (handle) handle.Set();
        return 0;
    }

    /// <summary>Wie Resolve-RootList in V1: kaputte Eintraege melden statt abstuerzen.</summary>
    private static IEnumerable<string> CheckRootLists(FloppyOptions o)
    {
        foreach (var b in o.BlockedRoots.Where(b => PathRules.TryGetFullPath(b) is null))
            yield return $"BlockedRoots-Eintrag ungueltig und ignoriert: '{b}'";
        foreach (var a in o.AllowedRoots.Where(a => PathRules.TryGetFullPath(a) is null))
            yield return $"AllowedRoots-Eintrag ungueltig und ignoriert: '{a}'";
    }

    private static string Version =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "?";
}
