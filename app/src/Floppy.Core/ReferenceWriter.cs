using System.Globalization;
using System.Text;

namespace Floppy.Core;

public enum ReferenceKind { Steam, Run, PcRun, Hub }

public sealed record WriteResult(string? Path, IReadOnlyList<PlanMessage> Messages)
{
    public bool Success => Path is not null;
}

/// <summary>Geprüfter Inhalt einer game.txt, bevor er geschrieben wird (Vorschau in der App).</summary>
/// <param name="Lines">Zeilen der Datei, <c>null</c> = ungueltig.</param>
/// <param name="Value">Bereinigter Wert (Steam-ID aus Link, Pfad ohne Anfuehrungszeichen).</param>
public sealed record ReferencePreview(IReadOnlyList<string>? Lines, IReadOnlyList<PlanMessage> Messages, string Value, string Arguments)
{
    public bool IsValid => Lines is not null;
    public string Text => Lines is null ? string.Empty : string.Join("\r\n", Lines) + "\r\n";
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

    /// <summary>Gedachte Wurzel, um run=-Pfade ohne echte Diskette zu pruefen.</summary>
    private const string VirtualRoot = @"Z:\floppy";

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
        WriteResult Fail(string text, IEnumerable<PlanMessage>? before = null)
        {
            var messages = new List<PlanMessage>(before ?? []) { new(MessageLevel.Error, text) };
            return new WriteResult(null, messages);
        }

        if (!Directory.Exists(root)) return Fail($"Ziel nicht erreichbar: {root} (Diskette eingelegt?)");

        var target = Path.Combine(root, fileName);
        if (File.Exists(target) && !force) return Fail($"{target} existiert bereits - zum Ersetzen bestaetigen.");

        var preview = Prepare(kind, value, arguments, title, options, root, now);
        if (!preview.IsValid) return new WriteResult(null, preview.Messages);

        var log = preview.Messages.ToList();
        try
        {
            File.WriteAllText(target, preview.Text, FileEncoding);
        }
        catch (Exception ex)
        {
            return Fail($"Schreiben fehlgeschlagen: {ex.Message}", log);
        }

        log.Add(new(MessageLevel.Ok, $"geschrieben: {target}"));
        return new WriteResult(target, log);
    }

    /// <summary>
    /// Prueft die Angaben und baut den Dateiinhalt - schreibt nichts.
    /// Mit <paramref name="root"/> wird zusaetzlich geprueft, ob eine run=-Datei auf der Diskette liegt.
    /// </summary>
    public static ReferencePreview Prepare(
        ReferenceKind kind,
        string? value = null,
        string? arguments = null,
        string? title = null,
        FloppyOptions? options = null,
        string? root = null,
        DateTime? now = null)
    {
        options ??= FloppyOptions.Default;
        var log = new List<PlanMessage>();
        var v = PathRules.StripQuotes(value);
        var a = PathRules.StripQuotes(arguments);

        ReferencePreview Fail(string text)
        {
            log.Add(new(MessageLevel.Error, text));
            return new ReferencePreview(null, log, v, a);
        }

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
                if (!PathRules.IsUnder(Path.Combine(VirtualRoot, v), VirtualRoot))
                    return Fail($"run= darf nicht aus der Diskette heraus zeigen: {v}");
                if (!PathRules.HasExecutableExtension(v, options.ExecutableExtensions))
                    return Fail($"run=-Datei ist nicht ausfuehrbar (erlaubt: {string.Join(", ", options.ExecutableExtensions)}): {v}");
                if (root is not null && !File.Exists(Path.Combine(root, v)))
                    log.Add(new(MessageLevel.Warn, $"'{v}' liegt (noch) nicht auf der Diskette - der Launcher wird es sonst ablehnen."));
                break;

            case ReferenceKind.PcRun:
                if (v.Length == 0) return Fail("pcrun= braucht einen Pfad.");
                if (!PathRules.IsRooted(v)) return Fail($"pcrun= braucht einen ABSOLUTEN Pfad (z. B. D:\\Spiele\\x.exe): {v}");
                if (!PathRules.HasExecutableExtension(v, options.ExecutableExtensions))
                    return Fail($"pcrun=-Datei ist nicht ausfuehrbar (erlaubt: {string.Join(", ", options.ExecutableExtensions)}): {v}");
                if (!File.Exists(v))
                    log.Add(new(MessageLevel.Warn, $"'{v}' existiert derzeit nicht - der Launcher wird es spaeter ablehnen."));
                var blocked = options.BlockedRoots.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b) && PathRules.IsUnder(v, b));
                if (blocked is not null)
                    log.Add(new(MessageLevel.Warn, $"'{v}' liegt in einem gesperrten Systemordner ({blocked}). Der Start wird ABGELEHNT."));
                break;
        }

        var stamp = (now ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
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

        return new ReferencePreview(lines, log, v, kind == ReferenceKind.Hub ? string.Empty : a);
    }

    private static bool ContainsNewline(string s) => s.Contains('\n') || s.Contains('\r');
}
