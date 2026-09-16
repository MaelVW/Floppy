namespace Floppy.Core.Tests;

public class LaunchPlannerTests
{
    private static PlanResult Plan(TempDisk disk, FloppyOptions? options = null) =>
        new LaunchPlanner(options ?? FloppyOptions.Default).Plan(disk.Root);

    private static void AssertRejected(PlanResult r, MessageLevel level, string fragment)
    {
        Assert.Null(r.Plan);
        Assert.Contains(r.Messages, m => m.Level == level && m.Text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------- Steam ----------------

    [Fact]
    public void Steam_Id_startet_ohne_Rueckfrage()
    {
        using var d = new TempDisk();
        d.GameTxt("id=220200");
        var p = Plan(d).Plan!;
        Assert.Equal(LaunchKind.Steam, p.Kind);
        Assert.Equal("steam://rungameid/220200", p.SteamUri);
        Assert.False(p.NeedsConfirmation);
    }

    [Fact]
    public void Steam_Id_in_Anfuehrungszeichen_funktioniert()
    {
        using var d = new TempDisk();
        d.GameTxt("steam=\"3772810\"");
        Assert.Equal("3772810", Plan(d).Plan!.SteamId);
    }

    [Fact]
    public void Ungueltige_Steam_Id_wird_abgelehnt()
    {
        using var d = new TempDisk();
        d.GameTxt("id=abc");
        AssertRejected(Plan(d), MessageLevel.Error, "Ungueltige Steam-ID");
    }

    [Fact]
    public void Referenzdatei_alternativer_Name()
    {
        using var d = new TempDisk();
        d.Write("floppy.txt", "gameid=70");
        Assert.Equal(LaunchKind.Steam, Plan(d).Plan!.Kind);
    }

    // ---------------- Hub ----------------

    [Theory]
    [InlineData("hub=1")]
    [InlineData("hubmenu = 1")]
    [InlineData("FloppyHub: ja")]
    public void Hub_Diskette_und_Aliasse(string line)
    {
        using var d = new TempDisk();
        d.GameTxt(line);
        Assert.Equal(LaunchKind.Hub, Plan(d).Plan!.Kind);
    }

    [Fact]
    public void Hub_abgeschaltet_faellt_durch()
    {
        using var d = new TempDisk();
        d.GameTxt("hub=1");
        var r = Plan(d, FloppyOptions.Default with { HubEnabled = false });
        AssertRejected(r, MessageLevel.Warn, "keine verwertbare Angabe");
    }

    // ---------------- Reihenfolge ----------------

    [Fact]
    public void Reihenfolge_Steam_vor_Hub_vor_pcrun_vor_run()
    {
        using var d = new TempDisk();
        d.Write("spiel.exe");
        d.GameTxt("run=spiel.exe", "hub=1", "id=220");
        Assert.Equal(LaunchKind.Steam, Plan(d).Plan!.Kind);

        d.GameTxt("run=spiel.exe", "hub=1");
        Assert.Equal(LaunchKind.Hub, Plan(d).Plan!.Kind);

        var pcExe = d.Write(@"pc\tool.exe");
        d.GameTxt("run=spiel.exe", $"pcrun={pcExe}");
        var p = Plan(d).Plan!;
        Assert.True(p.NeedsConfirmation);
        Assert.Equal(pcExe, p.Candidates.Single(), ignoreCase: true);
    }

    // ---------------- run= ----------------

    [Fact]
    public void Run_relativ_startet_sofort_mit_Argumenten()
    {
        using var d = new TempDisk();
        var exe = d.Write(@"bin\spiel.exe");
        d.GameTxt("run=\"bin\\spiel.exe\"", "args=-win -fullscreen");
        var p = Plan(d).Plan!;
        Assert.Equal(LaunchKind.Process, p.Kind);
        Assert.False(p.NeedsConfirmation);
        Assert.Equal(exe, p.Candidates.Single(), ignoreCase: true);
        Assert.Equal("-win -fullscreen", p.Arguments);
    }

    [Fact]
    public void Run_mit_absolutem_Pfad_wird_abgelehnt()
    {
        using var d = new TempDisk();
        d.GameTxt(@"run=C:\Windows\System32\calc.exe");
        AssertRejected(Plan(d), MessageLevel.Error, "relativ zur Diskette");
    }

    [Fact]
    public void Run_darf_nicht_aus_der_Diskette_heraus()
    {
        using var d = new TempDisk();
        d.GameTxt(@"run=..\..\Windows\notepad.exe");
        AssertRejected(Plan(d), MessageLevel.Warn, "aus der Diskette heraus");
    }

    [Fact]
    public void Run_fehlende_Datei_und_falsche_Endung()
    {
        using var d = new TempDisk();
        d.GameTxt("run=gibtsnicht.exe");
        AssertRejected(Plan(d), MessageLevel.Error, "fehlt auf der Diskette");

        d.Write("start.ps1");
        d.GameTxt("run=start.ps1");
        AssertRejected(Plan(d), MessageLevel.Error, "nicht ausfuehrbar");
    }

    // ---------------- pcrun= ----------------

    [Fact]
    public void Pcrun_in_Anfuehrungszeichen_im_gesperrten_Systemordner()
    {
        // Genau der Fall, der V1 frueher mit "Illegales Zeichen im Pfad" abgeschossen hat.
        using var d = new TempDisk();
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        d.GameTxt($"pcrun=\"{cmd}\"");
        AssertRejected(Plan(d), MessageLevel.Error, "gesperrten Systemordner");
    }

    [Fact]
    public void Pcrun_mit_Leerzeichen_braucht_Bestaetigung()
    {
        using var d = new TempDisk();
        using var pc = new TempDisk();
        var exe = pc.Write(@"Modrinth App\Modrinth App.exe");
        d.GameTxt($"pcrun=\"{exe}\"", "args=--minimized");
        var p = Plan(d).Plan!;
        Assert.Equal(LaunchKind.Process, p.Kind);
        Assert.True(p.NeedsConfirmation);
        Assert.Equal(exe, p.Candidates.Single(), ignoreCase: true);
        Assert.Equal("--minimized", p.Arguments);
    }

    [Theory]
    [InlineData("pcrun=spiel.exe", "absoluten Pfad")]
    [InlineData(@"pcrun=C:\gibtsnicht\x.exe", "nicht gefunden")]
    public void Pcrun_relativ_oder_fehlend_wird_abgelehnt(string line, string fragment)
    {
        using var d = new TempDisk();
        d.GameTxt(line);
        AssertRejected(Plan(d), MessageLevel.Error, fragment);
    }

    [Fact]
    public void Pcrun_ausserhalb_der_Positivliste_wird_abgelehnt()
    {
        using var d = new TempDisk();
        using var pc = new TempDisk();
        var exe = pc.Write("tool.exe");
        d.GameTxt($"pcrun={exe}");
        var options = FloppyOptions.Default with { AllowedRoots = [@"D:\Nur\Hier"] };
        AssertRejected(Plan(d, options), MessageLevel.Error, "ausserhalb der erlaubten");

        var erlaubt = FloppyOptions.Default with { AllowedRoots = [pc.Root] };
        Assert.NotNull(Plan(d, erlaubt).Plan);
    }

    // ---------------- ohne Referenzdatei ----------------

    [Fact]
    public void Leere_Diskette_ergibt_nichts_ohne_Meldung()
    {
        using var d = new TempDisk();
        var r = Plan(d);
        Assert.Null(r.Plan);
        Assert.Empty(r.Messages);
    }

    [Fact]
    public void Eine_EXE_startet_automatisch()
    {
        using var d = new TempDisk();
        var exe = d.Write("MeinProgramm.exe");
        var p = Plan(d).Plan!;
        Assert.False(p.NeedsConfirmation);
        Assert.Equal(exe, p.Candidates.Single(), ignoreCase: true);
    }

    [Fact]
    public void Mehrere_EXE_mit_gleichem_Namen_brauchen_Auswahl()
    {
        using var d = new TempDisk();
        d.Write("a.exe"); d.Write("b.bat"); d.Write(@"sub\a.exe");
        var r = Plan(d);
        Assert.True(r.Plan!.NeedsConfirmation);
        Assert.Equal(3, r.Plan.Candidates.Count);
        Assert.Contains(r.Messages, m => m.Text.Contains("Gleicher Dateiname"));
    }

    [Fact]
    public void Suche_geht_zwei_Ordnerebenen_tief()
    {
        using var d = new TempDisk();
        var ok = d.Write(@"a\b\ok.exe");
        d.Write(@"a\b\c\zutief.exe");
        Assert.Equal(ok, Plan(d).Plan!.Candidates.Single(), ignoreCase: true);
    }

    [Fact]
    public void Unbrauchbare_Referenzdatei_faellt_auf_EXE_Suche_zurueck()
    {
        using var d = new TempDisk();
        d.GameTxt("# nur Kommentar", "titel=nix");
        d.Write("game.exe");
        var r = Plan(d);
        Assert.Equal(LaunchKind.Process, r.Plan!.Kind);
        Assert.Contains(r.Messages, m => m.Level == MessageLevel.Warn && m.Text.Contains("keine verwertbare Angabe"));
    }
}
