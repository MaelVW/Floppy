namespace Floppy.Core.Tests;

public class TrustStoreTests
{
    private static TrustStore NewStore(TempDisk data) => new(Path.Combine(data.Root, "trust.json"));

    [Fact]
    public void Unbekannt_dann_freigegeben_dann_sofort_vertraut()
    {
        using var data = new TempDisk();
        using var pc = new TempDisk();
        var exe = pc.Write(@"Modrinth App\Modrinth App.exe", "version 1");
        var store = NewStore(data);

        Assert.Equal(TrustState.Unknown, store.Check(exe, "--minimized"));
        var entry = store.Add(exe, "--minimized", "Modrinth", TrustStore.SourceConfirmed);
        Assert.Equal(64, entry.Sha256.Length);

        Assert.Equal(TrustState.Trusted, store.Check(exe, "--minimized"));
        Assert.Equal(TrustState.Trusted, store.Check($"\"{exe.ToUpperInvariant()}\"", "  --minimized "));   // Anfuehrungszeichen, Gross/klein, Leerraum
    }

    [Fact]
    public void Andere_Argumente_gelten_als_neu()
    {
        using var data = new TempDisk();
        using var pc = new TempDisk();
        var exe = pc.Write("tool.exe");
        var store = NewStore(data);
        store.Add(exe, null, "Tool", TrustStore.SourceConfirmed);

        Assert.Equal(TrustState.Trusted, store.Check(exe, ""));
        Assert.Equal(TrustState.Unknown, store.Check(exe, "--delete-everything"));
    }

    [Fact]
    public void Veraenderte_Datei_wird_erkannt()
    {
        using var data = new TempDisk();
        using var pc = new TempDisk();
        var exe = pc.Write("spiel.exe", "original");
        var store = NewStore(data);
        store.Add(exe, null, "Spiel", TrustStore.SourceWritten);

        File.WriteAllText(exe, "ausgetauscht");
        Assert.Equal(TrustState.Changed, store.Check(exe, null, out var match));
        Assert.NotNull(match);

        // Neu freigeben ersetzt den alten Eintrag statt ihn zu verdoppeln.
        store.Add(exe, null, "Spiel", TrustStore.SourceConfirmed);
        Assert.Equal(TrustState.Trusted, store.Check(exe, null));
        Assert.Single(store.Load());
    }

    [Fact]
    public void Kaputte_Datei_heisst_fragen_und_wird_gesichert()
    {
        using var data = new TempDisk();
        using var pc = new TempDisk();
        var exe = pc.Write("x.exe");
        var file = data.Write("trust.json", "{ das ist kein json");
        var store = new TrustStore(file);

        Assert.Empty(store.Load());
        Assert.Equal(TrustState.Unknown, store.Check(exe, null));

        store.Add(exe, null, "X", TrustStore.SourceConfirmed);
        Assert.True(File.Exists(file + ".kaputt"));
        Assert.Equal(TrustState.Trusted, store.Check(exe, null));
    }

    [Fact]
    public void Fehlende_Datei_ist_unbekannt_statt_Absturz()
    {
        using var data = new TempDisk();
        var store = NewStore(data);
        Assert.Equal(TrustState.Unknown, store.Check(@"C:\gibt\es\nicht.exe", null));
    }

    [Fact]
    public void Entfernen()
    {
        using var data = new TempDisk();
        using var pc = new TempDisk();
        var exe = pc.Write("x.exe");
        var store = NewStore(data);
        store.Add(exe, "-a", "X", TrustStore.SourceConfirmed);

        Assert.False(store.Remove(exe, "-b"));
        Assert.True(store.Remove(exe, "-a"));
        Assert.Equal(TrustState.Unknown, store.Check(exe, "-a"));
    }
}

public class LaunchGateTests
{
    private static LaunchPlan Process(params string[] candidates) =>
        new(LaunchKind.Process, null, candidates, "-x", false, "game.txt");

    [Fact]
    public void Steam_und_Hub_ohne_Vertrauenspruefung()
    {
        var never = new Func<string, string?, TrustState>((_, _) => throw new InvalidOperationException("darf nicht gefragt werden"));
        Assert.Equal(GateAction.StartSteam, LaunchGate.Decide(new(LaunchKind.Steam, "220", [], null, false, null), never).Action);
        Assert.Equal(GateAction.OpenHub, LaunchGate.Decide(new(LaunchKind.Hub, null, [], null, false, null), never).Action);
        Assert.Equal(GateAction.None, LaunchGate.Decide(null, never).Action);
    }

    [Theory]
    [InlineData(TrustState.Trusted, GateAction.Start)]
    [InlineData(TrustState.Unknown, GateAction.Ask)]
    [InlineData(TrustState.Changed, GateAction.Ask)]
    public void Ein_Programm_nach_Vertrauen(TrustState state, GateAction expected)
    {
        string? askedArgs = null;
        var d = LaunchGate.Decide(Process(@"C:\a.exe"), (_, a) => { askedArgs = a; return state; });
        Assert.Equal(expected, d.Action);
        Assert.Equal(@"C:\a.exe", d.Target);
        Assert.Equal("-x", askedArgs);   // Argumente gehoeren zur Freigabe
    }

    [Fact]
    public void Mehrere_Programme_immer_fragen()
    {
        var d = LaunchGate.Decide(Process(@"A:\a.exe", @"A:\b.exe"), (_, _) => TrustState.Trusted);
        Assert.Equal(GateAction.Ask, d.Action);
        Assert.Null(d.Target);
    }

    [Fact]
    public void Fehler_bei_der_Pruefung_heisst_fragen()
    {
        var d = LaunchGate.Decide(Process(@"C:\a.exe"), (_, _) => throw new IOException("gesperrt"));
        Assert.Equal(GateAction.Ask, d.Action);
    }

    [Fact]
    public void Gesperrter_Systemordner_bleibt_gesperrt_auch_wenn_vertraut()
    {
        // Der Planner lehnt ab, bevor die Vertrauensliste ueberhaupt gefragt wird.
        using var d = new TempDisk();
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        d.GameTxt($"pcrun={cmd}");
        var plan = new LaunchPlanner(FloppyOptions.Default).Plan(d.Root).Plan;
        Assert.Equal(GateAction.None, LaunchGate.Decide(plan, (_, _) => TrustState.Trusted).Action);
    }

    [Fact]
    public void Ende_zu_Ende_Diskette_bespielt_und_freigegeben_startet_sofort()
    {
        using var disk = new TempDisk();
        using var pc = new TempDisk();
        using var data = new TempDisk();
        var exe = pc.Write(@"Spiele\game.exe", "binary");
        var store = new TrustStore(Path.Combine(data.Root, "trust.json"));

        Assert.True(ReferenceWriter.Write(disk.Root, ReferenceKind.PcRun, exe, "-fullscreen").Success);
        var planner = new LaunchPlanner(FloppyOptions.Default);

        var first = LaunchGate.Decide(planner.Plan(disk.Root).Plan, store.Check);
        Assert.Equal(GateAction.Ask, first.Action);

        store.Add(exe, "-fullscreen", "Game", TrustStore.SourceWritten);
        var second = LaunchGate.Decide(planner.Plan(disk.Root).Plan, store.Check);
        Assert.Equal(GateAction.Start, second.Action);
    }
}
