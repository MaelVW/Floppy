namespace Floppy.Core;

/// <summary>
/// Einfacher INI-Leser, verhaelt sich wie Import-FloppyIni aus FloppyLib.ps1:
/// Abschnitte und Schluessel ohne Beachtung der Gross-/Kleinschreibung,
/// ";" und "#" am Zeilenanfang = Kommentar, umschliessende " werden entfernt.
/// </summary>
public sealed class IniDocument
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections =
        new(StringComparer.OrdinalIgnoreCase) { [string.Empty] = new(StringComparer.OrdinalIgnoreCase) };

    public static IniDocument Parse(IEnumerable<string> lines)
    {
        var doc = new IniDocument();
        var section = string.Empty;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                if (!doc._sections.ContainsKey(section))
                    doc._sections[section] = new(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            if (val.Length >= 2 && val[0] == '"' && val[^1] == '"') val = val[1..^1];
            doc._sections[section][key] = val;
        }
        return doc;
    }

    /// <summary>Datei laden; fehlt sie, entsteht ein leeres Dokument (alle Standardwerte).</summary>
    public static IniDocument Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllLines(path)) : new IniDocument();

    public bool Has(string section, string key) =>
        _sections.TryGetValue(section, out var s) && s.ContainsKey(key);

    /// <summary>Wert oder <paramref name="fallback"/>, wenn fehlend oder leer.</summary>
    public string? Get(string section, string key, string? fallback = null) =>
        _sections.TryGetValue(section, out var s) && s.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;

    /// <summary>Wert wie er dasteht - auch leer (z. B. "file =" = Logging aus).</summary>
    public string? GetRaw(string section, string key) =>
        _sections.TryGetValue(section, out var s) && s.TryGetValue(key, out var v) ? v : null;

    public bool GetBool(string section, string key, bool fallback) =>
        Get(section, key)?.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "ja" or "yes" or "on" or "an" or "y" or "j" => true,
            "0" or "false" or "nein" or "no" or "off" or "aus" or "n" => false,
            _ => fallback,
        };

    public int GetInt(string section, string key, int fallback) =>
        int.TryParse(Get(section, key), out var i) ? i : fallback;

    /// <summary>"a, b ; c" -&gt; [a, b, c]. Fehlend oder leer -&gt; <paramref name="fallback"/>.</summary>
    public IReadOnlyList<string> GetList(string section, string key, IReadOnlyList<string> fallback)
    {
        var v = Get(section, key);
        if (v is null) return fallback;
        var items = v.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return items.Length == 0 ? fallback : items;
    }
}
