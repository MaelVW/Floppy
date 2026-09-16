using System.Globalization;
using System.Text;

namespace Floppy.Core;

public enum LogLevel { Info, Ok, Warn, Error, Ask }

/// <summary>
/// Launcher-Log im V1-Format (<c>2026-09-16 12:00:00 [INFO ] Text</c>), damit
/// Konsole, Hub und App dieselbe Datei lesen und schreiben koennen.
/// </summary>
public sealed class LogFile
{
    /// <summary>Wie Add-Content -Encoding UTF8 in PowerShell 5.1: BOM nur beim Anlegen.</summary>
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly Func<DateTime> _clock;
    private readonly Lock _gate = new();
    private int _writes;

    public LogFile(string? path, int maxKb = 512, Func<DateTime>? clock = null)
    {
        FilePath = path;
        MaxKb = maxKb;
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary><c>null</c> = nichts in eine Datei schreiben.</summary>
    public string? FilePath { get; }
    public int MaxKb { get; }

    /// <summary>Wird zusaetzlich fuer jede Zeile aufgerufen (z. B. Konsole im Testbetrieb).</summary>
    public Action<string>? Echo { get; set; }

    public static string Format(DateTime time, LogLevel level, string message) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{time:yyyy-MM-dd HH:mm:ss} [{LevelText(level),-5}] {message}");

    public static string LevelText(LogLevel level) => level switch
    {
        LogLevel.Ok => "OK",
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Ask => "ASK",
        _ => "INFO",
    };

    public static LogLevel FromMessage(MessageLevel level) => level switch
    {
        MessageLevel.Ok => LogLevel.Ok,
        MessageLevel.Warn => LogLevel.Warn,
        MessageLevel.Error => LogLevel.Error,
        _ => LogLevel.Info,
    };

    public void Write(LogLevel level, string message)
    {
        var line = Format(_clock(), level, message.Replace('\r', ' ').Replace('\n', ' '));
        Echo?.Invoke(line);
        if (FilePath is null) return;

        lock (_gate)
        {
            AppendLine(FilePath, line);
            // Groesse nur alle 50 Zeilen pruefen (wie V1, spart Datei-I/O).
            if (++_writes % 50 == 0) RotateIfNeeded();
        }
    }

    public void Write(PlanMessage message) => Write(FromMessage(message.Level), message.Text);

    /// <summary>Ab <see cref="MaxKb"/> einmalig nach &lt;name&gt;.old rotieren (V1: Limit-LogSize).</summary>
    public void RotateIfNeeded()
    {
        if (FilePath is null || MaxKb <= 0) return;
        try
        {
            var info = new FileInfo(FilePath);
            if (!info.Exists || info.Length < MaxKb * 1024L) return;
            File.Copy(FilePath, FilePath + ".old", overwrite: true);
            File.WriteAllBytes(FilePath, Bom);
            AppendLine(FilePath, Format(_clock(), LogLevel.Info,
                string.Create(CultureInfo.InvariantCulture,
                    $"Log rotiert (war {Math.Round(info.Length / 1024.0, 1)} KB), alte Zeilen in {Path.GetFileName(FilePath)}.old")));
        }
        catch
        {
            // Logging darf nie den Motor stoppen.
        }
    }

    /// <summary>
    /// Leert das Log; der alte Inhalt wandert einmalig nach &lt;name&gt;.old
    /// (V1: Clear-FloppyLog). Leeren statt Loeschen, damit ein laufender Motor weiterschreibt.
    /// </summary>
    public static (bool Cleared, long Bytes, string Message) Clear(string? path, bool backup = true, DateTime? now = null)
    {
        if (path is null) return (false, 0, "Logging ist in der INI abgeschaltet.");
        if (!File.Exists(path)) return (false, 0, "Keine Logdatei vorhanden.");
        try
        {
            var bytes = new FileInfo(path).Length;
            if (backup && bytes > 0) File.Copy(path, path + ".old", overwrite: true);
            File.WriteAllBytes(path, Bom);
            AppendLine(path, Format(now ?? DateTime.Now, LogLevel.Info,
                string.Create(CultureInfo.InvariantCulture, $"Log geleert (vorher {Math.Round(bytes / 1024.0, 1)} KB).")));
            return (true, bytes, "Log geleert.");
        }
        catch (Exception ex)
        {
            return (false, 0, $"Log konnte nicht geleert werden: {ex.Message}");
        }
    }

    /// <summary>Die letzten <paramref name="maxLines"/> Zeilen (fuer die Log-Ansicht).</summary>
    public static IReadOnlyList<string> ReadTail(string? path, int maxLines = 500)
    {
        if (path is null || !File.Exists(path)) return [];
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var queue = new Queue<string>(Math.Min(maxLines, 1024));
            while (reader.ReadLine() is { } line)
            {
                if (queue.Count == maxLines) queue.Dequeue();
                queue.Enqueue(line);
            }
            return queue.ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static void AppendLine(string path, string line)
    {
        // Motor und App schreiben evtl. gleichzeitig: kurz wiederholen statt Zeile verlieren.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length == 0) fs.Write(Bom);
                fs.Write(Utf8.GetBytes(line + "\r\n"));
                return;
            }
            catch (DirectoryNotFoundException)
            {
                FloppyPaths.EnsureDirectory(Path.GetDirectoryName(path)!);
            }
            catch (IOException)
            {
                Thread.Sleep(15);
            }
            catch
            {
                return;
            }
        }
    }
}
