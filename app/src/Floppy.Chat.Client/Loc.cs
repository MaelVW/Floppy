using System.Globalization;
using System.Reflection;

namespace Floppy.Chat.Client;

/// <summary>
/// Texte aus den eingebetteten <c>.lang</c>-Dateien. Format: <c>SCHLUESSEL = Text</c>, <c>{0}</c> = Platzhalter,
/// <c>\n</c> = Zeilenumbruch, <c>#</c> = Kommentar. Die Chat-Texte stammen aus derselben Datei wie in der
/// Desktop-App (<c>lang/de.lang</c>), die Handy-eigenen aus <c>mobile.de.lang</c> (sie gewinnen bei gleichem Schluessel).
/// </summary>
public static class Loc
{
    public static readonly IReadOnlyList<string> Languages = ["de", "en"];

    private static Dictionary<string, string> _texts = new(StringComparer.Ordinal);
    private static Dictionary<string, string> _fallback = new(StringComparer.Ordinal);

    static Loc() => Load("de");

    public static string Language { get; private set; } = "de";

    /// <summary>Sprache waehlen; unbekannte (oder null) = Deutsch. Akzeptiert auch "de-DE".</summary>
    public static void Load(string? language)
    {
        var code = (language ?? "").Trim().ToLowerInvariant();
        if (code.Length > 2) code = code[..2];
        if (!Languages.Contains(code)) code = "de";

        var fallback = Read("de");
        Language = code;
        _fallback = fallback;
        _texts = code == "de" ? fallback : Read(code);
    }

    public static string T(string key) =>
        _texts.TryGetValue(key, out var v) ? v : _fallback.TryGetValue(key, out var f) ? f : key;

    public static string T(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    public static bool Has(string key) => _texts.ContainsKey(key) || _fallback.ContainsKey(key);

    /// <summary>Alle Schluessel einer Sprache (fuer Tests: fehlt in Englisch etwas?).</summary>
    internal static IReadOnlyCollection<string> Keys(string language) => Read(language).Keys;

    private static Dictionary<string, string> Read(string language)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        Merge(map, $"lang.{language}.lang");
        Merge(map, $"lang.mobile.{language}.lang");
        return map;
    }

    private static void Merge(Dictionary<string, string> map, string resource)
    {
        using var stream = typeof(Loc).Assembly.GetManifestResourceStream(resource);
        if (stream is null) return;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

        while (reader.ReadLine() is { } raw)
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.TrimStart().StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            map[line[..eq].Trim()] = line[(eq + 1)..].Trim().Replace("\\n", "\n");
        }
    }
}
