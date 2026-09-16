using System.Text;

namespace Floppy.Core.Tests;

public class IniAndOptionsTests
{
    [Fact]
    public void Standardwerte_ohne_INI()
    {
        var o = FloppyOptions.FromIni(new IniDocument());
        Assert.Equal("A:\\", o.DriveRoot);
        Assert.Equal(3, o.PollSeconds);
        Assert.Equal(90, o.ConfirmTimeoutSeconds);
        Assert.Contains(o.BlockedRoots, b => b.Equals(Environment.GetEnvironmentVariable("SystemRoot"), StringComparison.OrdinalIgnoreCase));
        Assert.Equal("FloppyLauncher.log", o.LogFile);
        Assert.True(o.ClearLogOnHubExit);
    }

    [Fact]
    public void Werte_aus_INI_wie_V1()
    {
        var ini = IniDocument.Parse([
            "; Kommentar",
            "[Drive]",
            "letter = B:",
            "poll_seconds = 5",
            "[security]",
            "blocked_roots = %SystemRoot%; C:\\Users\\Public",
            "allowed_roots = \"D:\\Spiele\"",
            "non_interactive = ja",
            "confirm_timeout = 1",
            "[hub]",
            "enabled = aus",
            "key_names = HUB, Menue",
            "[log]",
            "file =",
            "[ui]",
            "theme = amber",
        ]);
        var o = FloppyOptions.FromIni(ini);
        Assert.Equal("B:\\", o.DriveRoot);
        Assert.Equal(5, o.PollSeconds);
        Assert.Equal(2, o.BlockedRoots.Count);
        Assert.DoesNotContain(o.BlockedRoots, b => b.Contains('%'));
        Assert.Equal([@"D:\Spiele"], o.AllowedRoots);
        Assert.True(o.NonInteractive);
        Assert.Equal(5, o.ConfirmTimeoutSeconds);          // auf Minimum begrenzt
        Assert.False(o.HubEnabled);
        Assert.Equal(["hub", "menue"], o.HubKeys);
        Assert.Equal(string.Empty, o.LogFile);              // "file =" heisst: kein Log
        Assert.Equal("amber", o.Theme);
    }

    [Fact]
    public void Echte_FloppyLauncher_ini_aus_dem_Repo_ist_lesbar()
    {
        var path = FindRepoFile("FloppyLauncher.ini");
        if (path is null) return; // ausserhalb des Repos (z. B. anderes Arbeitsverzeichnis)
        var o = FloppyOptions.FromIni(IniDocument.Load(path));
        Assert.Equal("A:\\", o.DriveRoot);
        Assert.Contains("game.txt", o.ReferenceFileNames);
    }

    internal static string? FindRepoFile(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}

public class ReferenceWriterTests
{
    [Fact]
    public void Schreibt_Steam_aus_Link_mit_BOM_und_liest_es_wieder()
    {
        using var d = new TempDisk();
        var r = ReferenceWriter.Write(d.Root, ReferenceKind.Steam, "https://store.steampowered.com/app/220/", title: "Half-Life 2 – Überraschung");
        Assert.True(r.Success);

        var bytes = File.ReadAllBytes(r.Path!);
        Assert.True(bytes is [0xEF, 0xBB, 0xBF, ..]); // BOM fuer Windows PowerShell 5.1
        Assert.Contains("Überraschung", Encoding.UTF8.GetString(bytes));

        var plan = new LaunchPlanner(FloppyOptions.Default).Plan(d.Root).Plan!;
        Assert.Equal("220", plan.SteamId);
    }

    [Fact]
    public void Pcrun_Anfuehrungszeichen_werden_beim_Schreiben_entfernt()
    {
        using var d = new TempDisk();
        using var pc = new TempDisk();
        var exe = pc.Write(@"Mein Spiel\spiel.exe");
        var r = ReferenceWriter.Write(d.Root, ReferenceKind.PcRun, $"\"{exe}\"", "\"-fs\"");
        Assert.True(r.Success);
        var text = File.ReadAllText(r.Path!);
        Assert.Contains($"pcrun={exe}", text);
        Assert.DoesNotContain("\"", text);
    }

    [Theory]
    [InlineData(ReferenceKind.Steam, "abc", "Keine Steam-AppID")]
    [InlineData(ReferenceKind.Run, @"C:\abs.exe", "RELATIV")]
    [InlineData(ReferenceKind.Run, "", "braucht einen Pfad")]
    [InlineData(ReferenceKind.PcRun, "relativ.exe", "ABSOLUTEN")]
    [InlineData(ReferenceKind.PcRun, "C:\\a.exe\npcrun=C:\\boese.exe", "Zeilenumbrueche")]
    public void Ungueltiges_wird_nicht_geschrieben(ReferenceKind kind, string value, string fragment)
    {
        using var d = new TempDisk();
        var r = ReferenceWriter.Write(d.Root, kind, value);
        Assert.False(r.Success);
        Assert.Contains(r.Messages, m => m.Level == MessageLevel.Error && m.Text.Contains(fragment));
        Assert.False(File.Exists(Path.Combine(d.Root, "game.txt")));
    }

    [Fact]
    public void Warnt_bei_gesperrtem_Systemordner_schreibt_aber()
    {
        using var d = new TempDisk();
        var r = ReferenceWriter.Write(d.Root, ReferenceKind.PcRun, Path.Combine(Environment.SystemDirectory, "cmd.exe"));
        Assert.True(r.Success);
        Assert.Contains(r.Messages, m => m.Level == MessageLevel.Warn && m.Text.Contains("gesperrten Systemordner"));
    }

    [Fact]
    public void Bestehende_Datei_nur_mit_force()
    {
        using var d = new TempDisk();
        d.GameTxt("id=1");
        Assert.False(ReferenceWriter.Write(d.Root, ReferenceKind.Hub).Success);
        Assert.True(ReferenceWriter.Write(d.Root, ReferenceKind.Hub, force: true).Success);
        Assert.Equal(LaunchKind.Hub, new LaunchPlanner(FloppyOptions.Default).Plan(d.Root).Plan!.Kind);
    }
}

public class LibraryCsvTests
{
    [Fact]
    public void Liest_V1_Format_mit_BOM_Anfuehrungszeichen_und_Kommas()
    {
        using var d = new TempDisk();
        var path = d.Write("library.csv", "\uFEFF\"Label\",\"Kind\",\"Value\",\"Added\",\"Notes\"\r\n" +
                                          "\"Kerbal Space Program\",\"steam\",\"220200\",\"2026-09-09\",\"Beispiel, mit Komma\"\r\n" +
                                          "\"Zitat \"\"x\"\"\",\"pcrun\",\"C:\\A B\\c.exe\",\"2026-09-11\",\"\"\r\n");
        var rows = LibraryCsv.Read(path);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Beispiel, mit Komma", rows[0].Notes);
        Assert.Equal("Zitat \"x\"", rows[1].Label);
        Assert.Equal(@"C:\A B\c.exe", rows[1].Value);
    }

    [Fact]
    public void Schreiben_und_Lesen_ergibt_dasselbe_und_Upsert_ersetzt()
    {
        using var d = new TempDisk();
        var path = Path.Combine(d.Root, "library.csv");
        var list = LibraryCsv.Upsert([], new("Portal 2", "steam", "620", "2026-09-16", "a"));
        list = LibraryCsv.Upsert(list, new("Tool \"X\"", "pcrun", @"C:\T, ools\x.exe", "2026-09-16"));
        list = LibraryCsv.Upsert(list, new("Portal 2 (neu)", "STEAM", "620", "2026-09-17", "b"));
        LibraryCsv.Write(path, list);

        var back = LibraryCsv.Read(path);
        Assert.Equal(2, back.Count);
        Assert.Equal("Portal 2 (neu)", back.Single(e => e.Value == "620").Label);
        Assert.Equal(list, back);
        Assert.True(File.ReadAllBytes(path) is [0xEF, 0xBB, 0xBF, ..]);
    }

    [Fact]
    public void Echte_library_csv_aus_dem_Repo_ist_lesbar()
    {
        var path = IniAndOptionsTests.FindRepoFile("library.csv");
        if (path is null) return;
        var rows = LibraryCsv.Read(path);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.DoesNotContain("\"", r.Value));
    }
}

public class DiskSignatureTests
{
    [Fact]
    public void Aendert_sich_bei_neuem_Inhalt()
    {
        using var d = new TempDisk();
        Assert.Equal(DiskSignature.Empty, DiskSignature.Compute(d.Root));

        d.GameTxt("id=1");
        var a = DiskSignature.Compute(d.Root);
        Assert.NotEqual(DiskSignature.Empty, a);
        Assert.Equal(a, DiskSignature.Compute(d.Root));

        d.GameTxt("id=22222");
        Assert.NotEqual(a, DiskSignature.Compute(d.Root));
    }

    [Fact]
    public void Nicht_vorhandenes_Laufwerk_ergibt_leer() =>
        Assert.Equal(DiskSignature.Empty, DiskSignature.Compute(@"Q:\gibt\es\nicht\"));
}
