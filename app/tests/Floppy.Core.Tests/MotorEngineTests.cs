namespace Floppy.Core.Tests;

public class MotorEngineTests
{
    private sealed class FakeActions : IMotorActions
    {
        public List<string> Calls { get; } = [];
        public bool AppInstalled { get; set; } = true;
        public bool FallbackAnswer { get; set; }

        public void OpenSteam(string appId) => Calls.Add($"steam:{appId}");
        public void StartProgram(string path, string? arguments) => Calls.Add($"start:{Path.GetFileName(path)}:{arguments}");
        public bool WakeApp(string command) { Calls.Add($"app:{command}"); return AppInstalled; }
        public bool AskWithoutApp(string target, string? arguments, string reason) { Calls.Add($"messagebox:{Path.GetFileName(target)}"); return FallbackAnswer; }
    }

    private sealed class Rig : IDisposable
    {
        public TempDisk Disk { get; } = new();
        public TempDisk Pc { get; } = new();
        public TempDisk Data { get; } = new();
        public FakeActions Actions { get; } = new();
        public List<string> LogLines { get; } = [];
        public TrustStore Trust { get; }
        public MotorEngine Engine { get; }

        public Rig(FloppyOptions? options = null, bool dryRun = false)
        {
            Trust = new TrustStore(Path.Combine(Data.Root, "trust.json"));
            var log = new LogFile(null) { Echo = LogLines.Add };
            Engine = new MotorEngine(options ?? FloppyOptions.Default, log, Trust, Actions, Disk.Root, dryRun);
        }

        public void Dispose() { Disk.Dispose(); Pc.Dispose(); Data.Dispose(); }
    }

    [Fact]
    public void Steam_startet_sofort_und_nur_einmal()
    {
        using var r = new Rig();
        r.Disk.GameTxt("id=220200");
        r.Engine.Tick();
        r.Engine.Tick();
        r.Engine.Tick();
        Assert.Equal(["steam:220200"], r.Actions.Calls);
        Assert.Contains(r.LogLines, l => l.Contains("Steam-ID 220200 gefunden"));
    }

    [Fact]
    public void Neues_Programm_weckt_die_App_zur_Rueckfrage()
    {
        using var r = new Rig();
        var exe = r.Pc.Write("tool.exe");
        r.Disk.GameTxt($"pcrun={exe}");
        var d = r.Engine.Tick();
        Assert.Equal(GateAction.Ask, d!.Action);
        Assert.Equal(["app:confirm"], r.Actions.Calls);
    }

    [Fact]
    public void Freigegebenes_pcrun_startet_sofort()
    {
        using var r = new Rig();
        var exe = r.Pc.Write("tool.exe");
        r.Disk.GameTxt($"pcrun=\"{exe}\"", "args=-fast");
        r.Trust.Add(exe, "-fast", "Tool", TrustStore.SourceWritten);

        r.Engine.Tick();
        Assert.Equal(["start:tool.exe:-fast"], r.Actions.Calls);
    }

    [Fact]
    public void Run_auf_der_Diskette_fragt_beim_ersten_Mal()
    {
        using var r = new Rig();
        r.Disk.Write("spiel.exe");
        r.Disk.GameTxt("run=spiel.exe");
        Assert.Equal(GateAction.Ask, r.Engine.Tick()!.Action);
    }

    [Fact]
    public void Hub_Diskette_oeffnet_App()
    {
        using var r = new Rig();
        r.Disk.GameTxt("hub=1");
        r.Engine.Tick();
        Assert.Equal(["app:hub"], r.Actions.Calls);
    }

    [Fact]
    public void Ohne_App_Windows_Rueckfrage_nur_bei_Ja_starten()
    {
        using var r = new Rig();
        r.Actions.AppInstalled = false;
        var exe = r.Pc.Write("tool.exe");
        r.Disk.GameTxt($"pcrun={exe}");

        r.Engine.Tick();
        Assert.Equal(["app:confirm", "messagebox:tool.exe"], r.Actions.Calls);

        r.Actions.Calls.Clear();
        r.Actions.FallbackAnswer = true;
        r.Disk.GameTxt($"pcrun={exe}", "# geaendert");   // neuer Inhalt -> neu behandeln
        r.Engine.Tick();
        Assert.Equal(["app:confirm", "messagebox:tool.exe", "start:tool.exe:"], r.Actions.Calls);
    }

    [Fact]
    public void Ohne_App_und_mehrere_EXE_wird_nichts_gestartet()
    {
        using var r = new Rig();
        r.Actions.AppInstalled = false;
        r.Disk.Write("a.exe");
        r.Disk.Write("b.exe");
        r.Engine.Tick();
        Assert.Equal(["app:confirm"], r.Actions.Calls);
        Assert.Contains(r.LogLines, l => l.Contains("[ERROR]") && l.Contains("nichts gestartet"));
    }

    [Fact]
    public void NonInteractive_fragt_nie_und_startet_nichts_Unbekanntes()
    {
        using var r = new Rig(FloppyOptions.Default with { NonInteractive = true });
        var exe = r.Pc.Write("tool.exe");
        r.Disk.GameTxt($"pcrun={exe}");
        r.Engine.Tick();
        Assert.Empty(r.Actions.Calls);
    }

    [Fact]
    public void DryRun_tut_nichts_und_protokolliert()
    {
        using var r = new Rig(dryRun: true);
        r.Disk.GameTxt("id=620");
        r.Engine.Tick();
        Assert.Empty(r.Actions.Calls);
        Assert.Contains(r.LogLines, l => l.Contains("[DRYRUN] wuerde starten: steam://rungameid/620"));
    }

    [Fact]
    public void Fehler_einer_Aktion_stoppt_den_Motor_nicht()
    {
        using var r = new Rig();
        var throwing = new ThrowingActions();
        var engine = new MotorEngine(FloppyOptions.Default, new LogFile(null) { Echo = r.LogLines.Add }, r.Trust, throwing, r.Disk.Root);
        r.Disk.GameTxt("id=1");
        Assert.NotNull(engine.Tick());
        Assert.Contains(r.LogLines, l => l.Contains("Start fehlgeschlagen"));
    }

    [Fact]
    public void Gesperrter_Ordner_wird_abgelehnt_und_protokolliert()
    {
        using var r = new Rig();
        r.Disk.GameTxt($"pcrun={Path.Combine(Environment.SystemDirectory, "cmd.exe")}");
        Assert.Equal(GateAction.None, r.Engine.Tick()!.Action);
        Assert.Empty(r.Actions.Calls);
        Assert.Contains(r.LogLines, l => l.Contains("gesperrten Systemordner"));
    }

    [Fact]
    public void Von_der_App_beschriebene_Diskette_startet_nicht_sofort()
    {
        using var r = new Rig();
        var engine = new MotorEngine(FloppyOptions.Default, new LogFile(null) { Echo = r.LogLines.Add }, r.Trust, r.Actions,
            r.Disk.Root, guardDir: r.Data.Root);

        r.Disk.GameTxt("id=1");
        engine.Tick();
        Assert.Equal(["steam:1"], r.Actions.Calls);

        // App bespielt die Diskette neu und hinterlaesst die Notiz
        r.Disk.GameTxt("id=2");
        WriteGuard.Note(r.Data.Root, DiskSignature.Compute(r.Disk.Root));
        engine.Tick();
        Assert.Equal(["steam:1"], r.Actions.Calls);
        Assert.False(File.Exists(Path.Combine(r.Data.Root, WriteGuard.FileName)));

        // Inhalt aendert sich danach erneut: startet wieder normal
        r.Disk.GameTxt("id=2", "# neu eingelegt");
        engine.Tick();
        Assert.Equal(["steam:1", "steam:2"], r.Actions.Calls);
    }

    [Fact]
    public void Waehrend_die_App_schreibt_wird_gewartet()
    {
        using var r = new Rig();
        var engine = new MotorEngine(FloppyOptions.Default, new LogFile(null) { Echo = r.LogLines.Add }, r.Trust, r.Actions,
            r.Disk.Root, guardDir: r.Data.Root);

        // App beginnt zu schreiben, der Motor sieht schon den halben Zustand
        WriteGuard.Begin(r.Data.Root);
        r.Disk.GameTxt("id=7");
        Assert.Equal("App schreibt gerade", engine.Tick()!.Reason);
        Assert.Equal("App schreibt gerade", engine.Tick()!.Reason);   // vergessen -> erneut angesehen
        Assert.Empty(r.Actions.Calls);

        // App ist fertig
        WriteGuard.Note(r.Data.Root, DiskSignature.Compute(r.Disk.Root));
        Assert.Equal("von der App beschrieben", engine.Tick()!.Reason);
        Assert.Null(engine.Tick());   // danach Ruhe
        Assert.Empty(r.Actions.Calls);
    }

    [Fact]
    public void Zweites_Laufwerk_wird_genauso_ausgewertet_wie_das_erste()
    {
        using var r = new Rig();
        using var extra = new TempDisk();
        var engine = new MotorEngine(FloppyOptions.Default, new LogFile(null) { Echo = r.LogLines.Add }, r.Trust, r.Actions,
            watchers: [new DiscWatcher(r.Disk.Root), new DiscWatcher(extra.Root)]);

        // nur die Diskette im zweiten Laufwerk hat etwas - das erste bleibt leer
        extra.GameTxt("id=42");
        engine.Tick();
        Assert.Equal(["steam:42"], r.Actions.Calls);

        // dieselbe Diskette bleibt beim naechsten Tick unbehandelt (schon gesehen)
        engine.Tick();
        Assert.Equal(["steam:42"], r.Actions.Calls);

        // jetzt legt jemand auch im ersten Laufwerk etwas ein
        r.Disk.GameTxt("id=7");
        engine.Tick();
        Assert.Equal(["steam:42", "steam:7"], r.Actions.Calls);
    }

    [Fact]
    public void Alte_oder_fremde_Notiz_wird_ignoriert()
    {
        using var data = new TempDisk();
        WriteGuard.Note(data.Root, "sig", now: DateTime.UtcNow.AddMinutes(-10));
        Assert.Equal(GuardState.None, WriteGuard.Check(data.Root, "sig"));
        Assert.False(File.Exists(Path.Combine(data.Root, WriteGuard.FileName)));

        WriteGuard.Note(data.Root, "sig-a");
        Assert.Equal(GuardState.None, WriteGuard.Check(data.Root, "sig-b"));
        Assert.Equal(GuardState.Written, WriteGuard.Check(data.Root, "sig-a"));

        // Abgestuerzte App: "schreibe gerade" verfaellt
        WriteGuard.Begin(data.Root, now: DateTime.UtcNow.AddMinutes(-5));
        Assert.Equal(GuardState.None, WriteGuard.Check(data.Root, "sig"));
    }

    private sealed class ThrowingActions : IMotorActions
    {
        public void OpenSteam(string appId) => throw new InvalidOperationException("Steam kaputt");
        public void StartProgram(string path, string? arguments) => throw new InvalidOperationException();
        public bool WakeApp(string command) => throw new InvalidOperationException();
        public bool AskWithoutApp(string target, string? arguments, string reason) => false;
    }
}
