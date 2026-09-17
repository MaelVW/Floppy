using System.Globalization;
using Floppy.Core;
using Floppy.Core.Minigame;
using Godot;

namespace FloppyHub.App.Core;

/// <summary>
/// Texte der Oberflaeche aus res://lang/&lt;sprache&gt;.lang.
/// Format: <c>SCHLUESSEL = Text</c>, <c>\n</c> fuer Zeilenumbruch, <c>#</c> Kommentar.
/// Eine neue Sprache ist nur eine neue Datei (Englisch folgt auf Befehl).
/// </summary>
public static class Loc
{
    private static Dictionary<string, string> _texts = new(StringComparer.Ordinal);
    private static Dictionary<string, string> _fallback = new(StringComparer.Ordinal);

    public static string Language { get; private set; } = "de";

    /// <summary>Sprachen, fuer die eine Datei existiert.</summary>
    public static bool IsAvailable(string language) => Godot.FileAccess.FileExists($"res://lang/{language}.lang");

    public static void Load(string language)
    {
        _fallback = Read("de");
        Language = IsAvailable(language) ? language : "de";
        _texts = Language == "de" ? _fallback : Read(Language);
    }

    public static string T(string key) =>
        _texts.TryGetValue(key, out var v) ? v : _fallback.TryGetValue(key, out var f) ? f : key;

    public static string T(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    public static bool Has(string key) => _texts.ContainsKey(key) || _fallback.ContainsKey(key);

    /// <summary>Meldung aus dem Kern uebersetzen (<c>CORE_&lt;Code&gt;</c>), sonst der deutsche Originaltext.</summary>
    public static string Message(PlanMessage m) =>
        m.Code is not null && Has("CORE_" + m.Code) ? T("CORE_" + m.Code, [.. (m.Args ?? []).Cast<object?>()]) : m.Text;

    /// <summary>Anzeigename eingebauter Level (<c>LEVELNAME_&lt;Level-ID&gt;</c>), sonst der Name aus dem Paket.</summary>
    public static string LevelName(string levelId, string name) =>
        Has("LEVELNAME_" + levelId.ToUpperInvariant()) ? T("LEVELNAME_" + levelId.ToUpperInvariant()) : name;

    /// <summary>Warum ein Level nicht spielbar ist (<c>LEVEL_&lt;Code&gt;</c>).</summary>
    public static string Level(LevelProblem p) => T("LEVEL_" + p.Code, [.. p.Args.Cast<object?>()]);

    private static Dictionary<string, string> Read(string language)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var file = Godot.FileAccess.Open($"res://lang/{language}.lang", Godot.FileAccess.ModeFlags.Read);
        if (file is null) return map;

        foreach (var raw in file.GetAsText().Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.TrimStart().StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim().Replace("\\n", "\n");
            map[key] = value;
        }
        return map;
    }
}
