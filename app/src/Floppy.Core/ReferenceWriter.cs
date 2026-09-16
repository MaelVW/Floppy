using System.Text;

namespace Floppy.Core;

public enum ReferenceKind { Steam, Run, PcRun, Hub }

public sealed record WriteResult(string? Path, IReadOnlyList<PlanMessage> Messages)
{
    public bool Success => Path is not null;
}

/// <summary>
/// Schreibt eine Referenzdatei (game.txt) auf eine Diskette - mit denselben
/// Pruefungen wie Write-FloppyReference in V1, BEVOR etwas geschrieben wird.
/// </summary>
public static class ReferenceWriter
{
    /// <summary>
    /// UTF-8 MIT BOM: Windows PowerShell 5.1 (V1) liest Dateien ohne BOM als ANSI -
    /// Umlaute im Titel waeren dort sonst kaputt.
    /// </summary>
    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static WriteResult Write(
        string root,
        ReferenceKind kind,
        string? value = null,
        string? arguments = null,
        string? title = null,
        FloppyOptions? options = null,
        string fileName = "game.txt",
        bool force = false,
        DateTime? now = null)
    {
        options ??= FloppyOptions.Default;
        var log = new List<PlanMessage>();
        WriteResult Fail(string text)
        {
            log.Add(new(MessageLevel.Error, text));
            return new WriteResult(null, log);
        }

        if (!Directory.Exists(root)) return Fail($"Ziel nicht erreichbar: {root} (Diskette eingelegt?)");

        var target = Path.Combine(root, fileName);
        if (File.Exists(target) && !force) return Fail($"{target} existiert bereits - zum Ersetzen bestaetigen.");

        var v = PathRules.StripQuotes(value);
        var a = PathRules.StripQuotes(arguments);

        // Zeilenumbrueche wuerden zusaetzliche Schluessel einschleusen (z. B. "\npcrun=...").
        if (ContainsNewline(v) || ContainsNewline(a)) return Fail("Werte duerfen keine Zeilenumbrueche enthalten.");

        switch (kind)
        {
            case ReferenceKind.Steam:
                if (!SteamApps.TryResolveAppId(v, out var appId)) return Fail($"Keine Steam-AppID erkennbar: '{v}'");
                v = appId;
                break;

            case ReferenceKind.Run:
                if (v.Length == 0) return Fail("run= braucht einen Pfad.");
                if (PathRules.IsRooted(v)) return Fail($"run= erwartet einen Pfad RELATIV zur Diskette. Fuer PC-Pfade pcrun= verwenden: {v}");
                break;

            case ReferenceKind.PcRun:
                if (v.Length == 0) return Fail("pcrun= braucht einen Pfad.");
                if (!PathRules.IsRooted(v)) return Fail($"pcrun= braucht einen ABSOLUTEN Pfad (z. B. D:\\Spiele\\x.exe): {v}");
                if (!File.Exists(v))
                    log.Add(new(MessageLevel.Warn, $"'{v}' existiert derzeit nicht - der Launcher wird es spaeter ablehnen."));
                var blocked = options.BlockedRoots.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b) && PathRules.IsUnder(v, b));
                if (blocked is not null)
                    log.Add(new(MessageLevel.Warn, $"'{v}' liegt in einem gesperrten Systemordner ({blocked}). Der Start wird ABGELEHNT."));
                break;
        }

        var stamp = (now ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm");
        var lines = new List<string> { $"# geschrieben von Floppy Hub  {stamp}" };
        if (!string.IsNullOrWhiteSpace(title)) lines.Add("# " + title.Replace('\r', ' ').Replace('\n', ' ').Trim());
        lines.Add(kind switch
        {
            ReferenceKind.Steam => $"id={v}",
            ReferenceKind.Run => $"run={v}",
            ReferenceKind.PcRun => $"pcrun={v}",
            _ => "hub=1",
        });
        if (a.Length > 0 && kind != ReferenceKind.Hub) lines.Add($"args={a}");

        try
        {
            File.WriteAllText(target, string.Join("\r\n", lines) + "\r\n", FileEncoding);
        }
        catch (Exception ex)
        {
            return Fail($"Schreiben fehlgeschlagen: {ex.Message}");
        }

        log.Add(new(MessageLevel.Ok, $"geschrieben: {target}"));
        return new WriteResult(target, log);
    }

    private static bool ContainsNewline(string s) => s.Contains('\n') || s.Contains('\r');
}
