using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Floppy.Core;

public enum TrustState
{
    /// <summary>Noch nie freigegeben.</summary>
    Unknown,

    /// <summary>Freigegeben und unveraendert - darf sofort starten.</summary>
    Trusted,

    /// <summary>Pfad + Argumente sind bekannt, aber die Datei ist eine andere (Update? Austausch?).</summary>
    Changed,
}

/// <summary>Ein freigegebenes Programm.</summary>
public sealed record TrustEntry
{
    public string Path { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string Label { get; init; } = "";

    /// <summary><see cref="TrustStore.SourceConfirmed"/> oder <see cref="TrustStore.SourceWritten"/>.</summary>
    public string Source { get; init; } = "";
    public DateTime Added { get; init; }
}

internal sealed class TrustDocument
{
    public int Version { get; set; } = 1;
    public List<TrustEntry> Entries { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TrustDocument))]
internal sealed partial class TrustJsonContext : JsonSerializerContext;

/// <summary>
/// Vertrauensliste der App-Variante: Programme, die ohne Rueckfrage starten duerfen.
/// Ein Eintrag gilt nur fuer genau diese Datei (SHA-256) mit genau diesen Argumenten.
/// Gesperrte Ordner (C:\Windows) prueft der <see cref="LaunchPlanner"/> VORHER -
/// Vertrauen kann das nicht aufheben.
/// </summary>
public sealed class TrustStore(string filePath)
{
    public const string SourceConfirmed = "bestaetigt";
    public const string SourceWritten = "bespielt";

    public string FilePath { get; } = filePath;

    /// <summary>Alle Eintraege. Fehlende oder kaputte Datei = leer (dann wird eben gefragt).</summary>
    public IReadOnlyList<TrustEntry> Load() => LoadDocument(out _).Entries;

    public TrustState Check(string path, string? arguments) => Check(path, arguments, out _);

    public TrustState Check(string path, string? arguments, out TrustEntry? match)
    {
        match = null;
        var full = Normalize(path);
        var args = NormalizeArgs(arguments);
        var candidates = Load().Where(e => SameTarget(e, full, args)).ToArray();
        if (candidates.Length == 0) return TrustState.Unknown;

        string hash;
        try { hash = ComputeSha256(full); }
        catch { return TrustState.Unknown; }   // nicht lesbar -> lieber fragen

        match = candidates.FirstOrDefault(e => string.Equals(e.Sha256, hash, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return TrustState.Trusted;
        match = candidates[0];
        return TrustState.Changed;
    }

    /// <summary>Datei freigeben (ersetzt einen alten Eintrag fuer denselben Pfad + Argumente).</summary>
    public TrustEntry Add(string path, string? arguments, string label, string source, DateTime? now = null)
    {
        var full = Normalize(path);
        var entry = new TrustEntry
        {
            Path = full,
            Arguments = NormalizeArgs(arguments),
            Sha256 = ComputeSha256(full),
            Label = label.Trim(),
            Source = source,
            Added = now ?? DateTime.Now,
        };

        var doc = LoadDocument(out var corrupt);
        if (corrupt) BackupCorruptFile();
        doc.Entries.RemoveAll(e => SameTarget(e, entry.Path, entry.Arguments));
        doc.Entries.Add(entry);
        Save(doc);
        return entry;
    }

    public bool Remove(string path, string? arguments)
    {
        var doc = LoadDocument(out var corrupt);
        if (corrupt) return false;
        var removed = doc.Entries.RemoveAll(e => SameTarget(e, Normalize(path), NormalizeArgs(arguments)));
        if (removed > 0) Save(doc);
        return removed > 0;
    }

    public static string ComputeSha256(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    private static bool SameTarget(TrustEntry e, string fullPath, string args) =>
        string.Equals(Normalize(e.Path), fullPath, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(NormalizeArgs(e.Arguments), args, StringComparison.Ordinal);

    private static string Normalize(string path) =>
        PathRules.TryGetFullPath(PathRules.StripQuotes(path)) ?? path;

    private static string NormalizeArgs(string? arguments) => (arguments ?? string.Empty).Trim();

    private TrustDocument LoadDocument(out bool corrupt)
    {
        corrupt = false;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!File.Exists(FilePath)) return new TrustDocument();
                var json = File.ReadAllText(FilePath);
                var doc = JsonSerializer.Deserialize(json, TrustJsonContext.Default.TrustDocument);
                if (doc?.Entries is null) { corrupt = true; return new TrustDocument(); }
                doc.Entries.RemoveAll(e => e is null || string.IsNullOrWhiteSpace(e.Path) || string.IsNullOrWhiteSpace(e.Sha256));
                return doc;
            }
            catch (IOException)
            {
                Thread.Sleep(20);   // App speichert gerade
            }
            catch (Exception)
            {
                corrupt = true;
                return new TrustDocument();
            }
        }
        return new TrustDocument();
    }

    private void Save(TrustDocument doc)
    {
        FloppyPaths.EnsureDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(doc, TrustJsonContext.Default.TrustDocument));
        File.Move(temp, FilePath, overwrite: true);   // nie eine halb geschriebene Liste
    }

    private void BackupCorruptFile()
    {
        try { File.Copy(FilePath, FilePath + ".kaputt", overwrite: true); } catch { }
    }
}
