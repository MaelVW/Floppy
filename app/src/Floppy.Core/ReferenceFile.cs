using System.Text.RegularExpressions;

namespace Floppy.Core;

/// <summary>
/// Liest die Referenzdatei einer Diskette (game.txt / floppy.txt / launch.txt).
/// Format wie in V1: eine Angabe pro Zeile, "#" oder ";" am Zeilenanfang = Kommentar,
/// "schluessel=wert" oder "schluessel: wert", eine nackte Zahl gilt als Steam-ID.
/// </summary>
public static partial class ReferenceFile
{
    public static readonly string[] SteamKeys = ["id", "steam", "steamid", "gameid"];
    public static readonly string[] DefaultHubKeys = ["hub", "hubmenu", "menu", "floppyhub"];
    public static readonly string[] PcRunKeys = ["pcrun", "localrun", "pcexe"];
    public static readonly string[] RunKeys = ["run", "exe", "program", "path"];

    /// <summary>App-Variante: Levelpaket fuer das Minispiel (nur Daten, kein Programm).</summary>
    public static readonly string[] GameKeys = ["minigame"];
    public const string ArgsKey = "args";

    [GeneratedRegex(@"^\s*([A-Za-z_]+)\s*[:=]\s*(.+?)\s*$")]
    private static partial Regex KeyValueLine();

    [GeneratedRegex(@"^\s*(\d{3,})\s*$")]
    private static partial Regex BareIdLine();

    /// <summary>Zeilen in ein Woerterbuch (Schluessel klein geschrieben) uebersetzen.</summary>
    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;

            var kv = KeyValueLine().Match(line);
            if (kv.Success)
            {
                // Anfuehrungszeichen weg - sonst scheitert spaeter jede Pfadpruefung.
                map[kv.Groups[1].Value.ToLowerInvariant()] = PathRules.StripQuotes(kv.Groups[2].Value);
                continue;
            }

            var id = BareIdLine().Match(line);
            if (id.Success) map["id"] = id.Groups[1].Value;
        }
        return map;
    }

    /// <summary>Datei lesen und parsen.</summary>
    public static IReadOnlyDictionary<string, string> Load(string path) => Parse(File.ReadAllLines(path));

    /// <summary>Erster vorhandener Schluessel aus <paramref name="keys"/> oder null.</summary>
    public static string? FirstValue(IReadOnlyDictionary<string, string> map, IEnumerable<string> keys)
    {
        foreach (var k in keys)
            if (map.TryGetValue(k, out var v)) return v;
        return null;
    }

    /// <summary>Erste existierende Referenzdatei im Wurzelverzeichnis oder null.</summary>
    public static string? Find(string root, IEnumerable<string> fileNames)
    {
        foreach (var name in fileNames)
        {
            var p = Path.Combine(root, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
