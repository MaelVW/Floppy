using System.Text;

namespace Floppy.Core.Tests;

public class DiscWatcherTests
{
    [Fact]
    public void Verhaelt_sich_wie_die_V1_Hauptschleife()
    {
        var ready = false;
        var content = "a";
        var w = new DiscWatcher(@"A:\", _ => ready, _ => content);

        Assert.False(w.Poll());                 // keine Diskette
        ready = true;
        Assert.True(w.Poll());                  // eingelegt -> einmal behandeln
        Assert.False(w.Poll());                 // bleibt drin -> nicht nochmal
        Assert.False(w.Poll());
        content = "b";
        Assert.True(w.Poll());                  // Inhalt geaendert -> neu behandeln
        ready = false;
        Assert.False(w.Poll());                 // raus
        Assert.Null(w.LastSignature);
        ready = true;
        Assert.True(w.Poll());                  // dieselbe Diskette wieder rein -> neu behandeln
    }

    [Fact]
    public void MarkHandled_verhindert_Start_nach_eigenem_Schreiben()
    {
        var content = "alt";
        var w = new DiscWatcher(@"A:\", _ => true, _ => content);
        Assert.True(w.Poll());
        content = "von der App geschrieben";
        w.MarkHandled();
        Assert.False(w.Poll());
    }

    [Fact]
    public void Fehler_beim_Laufwerk_zaehlt_als_keine_Diskette()
    {
        var w = new DiscWatcher(@"A:\", _ => throw new IOException("Geraet nicht bereit"), _ => "x");
        Assert.False(w.Poll());
        Assert.False(w.DiscPresent);
    }

    [Fact]
    public void Testordner_statt_Laufwerk()
    {
        using var d = new TempDisk();
        Assert.True(DiscWatcher.IsReady(d.Root));
        Assert.False(DiscWatcher.IsReady(Path.Combine(d.Root, "fehlt")));
    }
}

public class LogFileTests
{
    private static readonly DateTime T = new(2026, 9, 16, 13, 5, 9);

    [Fact]
    public void Format_wie_V1()
    {
        Assert.Equal("2026-09-16 13:05:09 [INFO ] Hallo", LogFile.Format(T, LogLevel.Info, "Hallo"));
        Assert.Equal("2026-09-16 13:05:09 [OK   ] x", LogFile.Format(T, LogLevel.Ok, "x"));
        Assert.Equal("2026-09-16 13:05:09 [ERROR] x", LogFile.Format(T, LogLevel.Error, "x"));
    }

    [Fact]
    public void BOM_nur_einmal_Zeilen_mit_CRLF_Umlaute_ok()
    {
        using var d = new TempDisk();
        var path = Path.Combine(d.Root, "sub", "FloppyLauncher.log");
        var log = new LogFile(path, clock: () => T);
        log.Write(LogLevel.Info, "Diskette eingelegt – Übung");
        log.Write(LogLevel.Warn, "zwei\nZeilen");

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes is [0xEF, 0xBB, 0xBF, ..]);
        Assert.Equal(1, CountBom(bytes));
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Equal(
            "2026-09-16 13:05:09 [INFO ] Diskette eingelegt – Übung\r\n" +
            "2026-09-16 13:05:09 [WARN ] zwei Zeilen\r\n", text);
    }

    [Fact]
    public void Rotation_ab_Maximalgroesse()
    {
        using var d = new TempDisk();
        var path = Path.Combine(d.Root, "x.log");
        var log = new LogFile(path, maxKb: 1, clock: () => T);
        for (var i = 0; i < 50; i++) log.Write(LogLevel.Info, new string('x', 40));

        Assert.True(File.Exists(path + ".old"));
        var lines = LogFile.ReadTail(path);
        Assert.Single(lines);
        Assert.Contains("Log rotiert", lines[0]);
    }

    [Fact]
    public void Leeren_mit_Sicherung_und_ReadTail()
    {
        using var d = new TempDisk();
        var path = Path.Combine(d.Root, "x.log");
        var log = new LogFile(path, clock: () => T);
        for (var i = 1; i <= 5; i++) log.Write(LogLevel.Info, $"Zeile {i}");

        Assert.Equal(["2026-09-16 13:05:09 [INFO ] Zeile 4", "2026-09-16 13:05:09 [INFO ] Zeile 5"], LogFile.ReadTail(path, 2));

        var (cleared, bytes, _) = LogFile.Clear(path, now: T);
        Assert.True(cleared);
        Assert.True(bytes > 0);
        Assert.Contains("Zeile 5", File.ReadAllText(path + ".old"));
        Assert.Contains("Log geleert", Assert.Single(LogFile.ReadTail(path)));
    }

    [Fact]
    public void Ohne_Pfad_passiert_nichts()
    {
        var echo = new List<string>();
        var log = new LogFile(null, clock: () => T) { Echo = echo.Add };
        log.Write(LogLevel.Ok, "nur Echo");
        Assert.Single(echo);
        Assert.False(LogFile.Clear(null).Cleared);
        Assert.Empty(LogFile.ReadTail(null));
    }

    private static int CountBom(byte[] b)
    {
        var n = 0;
        for (var i = 0; i + 2 < b.Length; i++)
            if (b[i] == 0xEF && b[i + 1] == 0xBB && b[i + 2] == 0xBF) n++;
        return n;
    }
}

public class FloppyPathsTests
{
    [Fact]
    public void Bibliothek_und_Log_wie_V1()
    {
        using var home = new TempDisk();
        using var user = new TempDisk();
        var paths = new FloppyPaths(home.Root, user.Root);

        // beschreibbarer Programmordner -> Daten dort
        Assert.Equal(Path.Combine(home.Root, "library.csv"), paths.LibraryFile, ignoreCase: true);
        Assert.Equal(Path.Combine(home.Root, "FloppyLauncher.log"), paths.ResolveLogFile(FloppyOptions.Default), ignoreCase: true);

        // benutzereigene Dateien immer im Benutzerordner
        Assert.StartsWith(user.Root.TrimEnd('\\'), paths.TrustFile, StringComparison.OrdinalIgnoreCase);

        Assert.Null(paths.ResolveLogFile(FloppyOptions.Default with { LogFile = "" }));
        var rooted = Path.Combine(user.Root, "abs.log");
        Assert.Equal(rooted, paths.ResolveLogFile(FloppyOptions.Default with { LogFile = $"\"{rooted}\"" }), ignoreCase: true);
    }

    [Fact]
    public void Fehlende_INI_ergibt_Standardwerte()
    {
        using var home = new TempDisk();
        var o = new FloppyPaths(home.Root).LoadOptions(out var problem);
        Assert.Null(problem);
        Assert.Equal("A:\\", o.DriveRoot);
    }
}

public class HubPipeTests
{
    [Fact]
    public void Ohne_App_schlaegt_Senden_schnell_fehl()
    {
        var name = "FloppyHub.Test." + Guid.NewGuid().ToString("N");
        Assert.False(HubPipe.TrySend(name, HubPipe.Show, 100));
    }

    [Fact]
    public void Unbekannte_Befehle_werden_nie_gesendet()
    {
        Assert.Throws<ArgumentException>(() => HubPipe.TrySend(@"pcrun=C:\boese.exe"));
    }

    [Fact]
    public async Task Server_empfaengt_nur_gueltige_Befehle()
    {
        var name = "FloppyHub.Test." + Guid.NewGuid().ToString("N");
        var received = new List<string>();
        using var cts = new CancellationTokenSource();
        var gotTwo = new TaskCompletionSource();
        var server = HubPipe.ServeAsync(name, cmd =>
        {
            lock (received) { received.Add(cmd); if (received.Count == 2) gotTwo.TrySetResult(); }
        }, cts.Token);

        Assert.True(await SendWithRetry(name, HubPipe.Confirm));
        await SendRaw(name, "run C:\\boese.exe\n");      // wird ignoriert
        Assert.True(await SendWithRetry(name, HubPipe.Hub));

        await gotTwo.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([HubPipe.Confirm, HubPipe.Hub], received);

        cts.Cancel();
        await server.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task<bool> SendWithRetry(string name, string cmd)
    {
        for (var i = 0; i < 20; i++)
        {
            if (HubPipe.TrySend(name, cmd, 200)) return true;
            await Task.Delay(50);
        }
        return false;
    }

    private static async Task SendRaw(string name, string text)
    {
        for (var i = 0; i < 20; i++)
        {
            try
            {
                using var c = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.Out);
                c.Connect(200);
                c.Write(Encoding.ASCII.GetBytes(text));
                return;
            }
            catch
            {
                await Task.Delay(50);
            }
        }
    }
}

public class DriveSnapshotTests
{
    [Theory]
    [InlineData(DriveType.Removable, @"A:\", 1_474_560L, MediaKind.Floppy)]
    [InlineData(DriveType.Removable, @"A:\", 0L, MediaKind.Floppy)]
    [InlineData(DriveType.Removable, @"E:\", 1_474_560L, MediaKind.Floppy)]
    [InlineData(DriveType.Removable, @"E:\", 16_000_000_000L, MediaKind.Usb)]
    [InlineData(DriveType.CDRom, @"D:\", 0L, MediaKind.Optical)]
    [InlineData(DriveType.Fixed, @"C:\", 1L, MediaKind.Fixed)]
    public void Erkennt_Datentraeger(DriveType type, string root, long total, MediaKind expected) =>
        Assert.Equal(expected, DriveSnapshot.Classify(type, root, total));

    [Fact]
    public void Lesen_wirft_nie()
    {
        var s = DriveSnapshot.Read("C:");
        Assert.Equal(@"C:\", s.Root);
        Assert.True(s.Ready);
        Assert.InRange(s.UsedRatio, 0, 1);
    }
}
