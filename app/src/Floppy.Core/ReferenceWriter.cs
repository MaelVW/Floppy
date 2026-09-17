using System.Globalization;
using System.Text;

namespace Floppy.Core;

public enum ReferenceKind { Steam, Run, PcRun, Hub, Game }

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
        WriteResult Fail(string code, string text, IEnumerable<PlanMessage>? before, params string[] args)
        {
            var messages = new List<PlanMessage>(before ?? []) { PlanMessage.Of(MessageLevel.Error, code, text, args) };
            return new WriteResult(null, messages);
        }

        if (!Directory.Exists(root)) return Fail("WRITE_NO_TARGET", $"Ziel nicht erreichbar: {root} (Diskette eingelegt?)", null, root);

        var target = Path.Combine(root, fileName);
        if (File.Exists(target) && !force) return Fail("WRITE_EXISTS", $"{target} existiert bereits - zum Ersetzen bestaetigen.", null, target);

        var preview = Prepare(kind, value, arguments, title, options, root, now);
        if (!preview.IsValid) return new WriteResult(null, preview.Messages);

        var log = preview.Messages.ToList();
        try
        {
            File.WriteAllText(target, preview.Text, FileEncoding);
        }
        catch (Exception ex)
        {
            return Fail("WRITE_FAILED", $"Schreiben fehlgeschlagen: {ex.Message}", log, ex.Message);
        }

        log.Add(PlanMessage.Of(MessageLevel.Ok, "WRITE_DONE", $"geschrieben: {target}", target));
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

        ReferencePreview Fail(string code, string text, params string[] args)
        {
            log.Add(PlanMessage.Of(MessageLevel.Error, code, text, args));
            return new ReferencePreview(null, log, v, a);
        }

        // Zeilenumbrueche wuerden zusaetzliche Schluessel einschleusen (z. B. "\npcrun=...").
        if (ContainsNewline(v) || ContainsNewline(a)) return Fail("VALUE_NEWLINE", "Werte duerfen keine Zeilenumbrueche enthalten.");

        switch (kind)
        {
            case ReferenceKind.Steam:
                if (!SteamApps.TryResolveAppId(v, out var appId)) return Fail("STEAM_UNRECOGNIZED", $"Keine Steam-AppID erkennbar: '{v}'", v);
                v = appId;
                break;

            case ReferenceKind.Run:
                if (v.Length == 0) return Fail("RUN_EMPTY", "run= braucht einen Pfad.");
                if (PathRules.IsRooted(v)) return Fail("RUN_ABSOLUTE", $"run= erwartet einen Pfad RELATIV zur Diskette. Fuer PC-Pfade pcrun= verwenden: {v}", v);
                if (!PathRules.IsUnder(Path.Combine(VirtualRoot, v), VirtualRoot))
                    return Fail("RUN_OUTSIDE", $"run= darf nicht aus der Diskette heraus zeigen: {v}", v);
                if (!PathRules.HasExecutableExtension(v, options.ExecutableExtensions))
                    return Fail("RUN_NOT_EXE_ALLOWED", $"run=-Datei ist nicht ausfuehrbar (erlaubt: {string.Join(", ", options.ExecutableExtensions)}): {v}", string.Join(", ", options.ExecutableExtensions), v);
                if (root is not null && !File.Exists(Path.Combine(root, v)))
                    log.Add(PlanMessage.Of(MessageLevel.Warn, "RUN_NOT_ON_DISC", $"'{v}' liegt (noch) nicht auf der Diskette - der Launcher wird es sonst ablehnen.", v));
                break;

            case ReferenceKind.Game:
                if (v.Length == 0) return Fail("GAME_EMPTY", "minigame= braucht eine Leveldatei.");
                if (PathRules.IsRooted(v) || !PathRules.IsUnder(Path.Combine(VirtualRoot, v), VirtualRoot))
                    return Fail("GAME_NOT_RELATIVE", $"minigame= erwartet eine Datei auf der Diskette (relativer Pfad): {v}", v);
                if (!PathRules.HasExecutableExtension(v, Minigame.LevelPack.Extensions))
                    return Fail("GAME_NOT_TEXT", $"minigame= erwartet eine Textdatei ({string.Join(", ", Minigame.LevelPack.Extensions)}): {v}", string.Join(", ", Minigame.LevelPack.Extensions), v);
                if (root is not null && !File.Exists(Path.Combine(root, v)))
                    log.Add(PlanMessage.Of(MessageLevel.Warn, "GAME_NOT_ON_DISC", $"'{v}' liegt (noch) nicht auf der Diskette.", v));
                break;

            case ReferenceKind.PcRun:
                if (v.Length == 0) return Fail("PCRUN_EMPTY", "pcrun= braucht einen Pfad.");
                if (!PathRules.IsRooted(v)) return Fail("PCRUN_NEEDS_ABSOLUTE", $"pcrun= braucht einen ABSOLUTEN Pfad (z. B. D:\\Spiele\\x.exe): {v}", v);
                if (!PathRules.HasExecutableExtension(v, options.ExecutableExtensions))
                    return Fail("PCRUN_NOT_EXE_ALLOWED", $"pcrun=-Datei ist nicht ausfuehrbar (erlaubt: {string.Join(", ", options.ExecutableExtensions)}): {v}", string.Join(", ", options.ExecutableExtensions), v);
                if (!File.Exists(v))
                    log.Add(PlanMessage.Of(MessageLevel.Warn, "PCRUN_MISSING_NOW", $"'{v}' existiert derzeit nicht - der Launcher wird es spaeter ablehnen.", v));
                var blocked = options.BlockedRoots.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b) && PathRules.IsUnder(v, b));
                if (blocked is not null)
                    log.Add(PlanMessage.Of(MessageLevel.Warn, "PCRUN_BLOCKED_WARN", $"'{v}' liegt in einem gesperrten Systemordner ({blocked}). Der Start wird ABGELEHNT.", v, blocked));
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
            ReferenceKind.Game => $"minigame={v}",
            _ => "hub=1",
        });
        var takesArgs = kind is ReferenceKind.Steam or ReferenceKind.Run or ReferenceKind.PcRun;
        if (a.Length > 0 && takesArgs) lines.Add($"args={a}");

        return new ReferencePreview(lines, log, v, takesArgs ? a : string.Empty);
    }

    private static bool ContainsNewline(string s) => s.Contains('\n') || s.Contains('\r');
}
