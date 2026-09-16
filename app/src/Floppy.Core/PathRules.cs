namespace Floppy.Core;

/// <summary>
/// Pfad-Helfer mit denselben Regeln wie die Konsolen-Variante (FloppyLauncher.ps1).
/// </summary>
public static class PathRules
{
    /// <summary>
    /// Entfernt umschliessende Anfuehrungszeichen und Leerraum.
    /// Ein im Explorer per "Als Pfad kopieren" geholter Pfad bringt " mit - daran
    /// ist V1 frueher mit "Illegales Zeichen im Pfad" abgestuerzt.
    /// </summary>
    public static string StripQuotes(string? value)
    {
        if (value is null) return string.Empty;
        var v = value.Trim();
        while (v.Length >= 2 &&
               ((v[0] == '"' && v[^1] == '"') || (v[0] == '\'' && v[^1] == '\'')))
        {
            v = v[1..^1].Trim();
        }
        return v.Replace("\"", string.Empty).Trim();
    }

    /// <summary>Wie <see cref="Path.IsPathRooted(string)"/>, wirft aber nie.</summary>
    public static bool IsRooted(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return Path.IsPathRooted(path); }
        catch { return false; }
    }

    /// <summary>Vollstaendiger Pfad oder null, wenn der Pfad ungueltig ist.</summary>
    public static string? TryGetFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path); }
        catch { return null; }
    }

    /// <summary>
    /// true, wenn <paramref name="path"/> gleich <paramref name="root"/> ist oder darunter liegt
    /// (Gross-/Kleinschreibung egal). Schutz gegen "..\..\" aus der Diskette heraus.
    /// </summary>
    public static bool IsUnder(string path, string root)
    {
        var p = TryGetFullPath(path)?.TrimEnd('\\', '/');
        var r = TryGetFullPath(root)?.TrimEnd('\\', '/');
        if (p is null || r is null) return false;
        if (p.Equals(r, StringComparison.OrdinalIgnoreCase)) return true;
        return p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Hat die Datei eine der erlaubten Endungen (z. B. .exe, .bat, .cmd)?</summary>
    public static bool HasExecutableExtension(string path, IEnumerable<string> extensions)
    {
        string ext;
        try { ext = Path.GetExtension(path); }
        catch { return false; }
        return !string.IsNullOrEmpty(ext) &&
               extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Normiert einen Laufwerksbuchstaben ("a", "A:", "A:\") zu "A:\".</summary>
    public static string DriveRoot(string driveLetter)
    {
        var l = (driveLetter ?? string.Empty).Trim().TrimEnd(':', '\\', '/');
        if (l.Length == 0 || !char.IsLetter(l[0]))
            throw new ArgumentException($"Ungueltiger Laufwerksbuchstabe: '{driveLetter}'", nameof(driveLetter));
        return char.ToUpperInvariant(l[0]) + ":\\";
    }
}
