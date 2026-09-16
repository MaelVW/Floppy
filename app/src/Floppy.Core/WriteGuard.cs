using System.Globalization;

namespace Floppy.Core;

/// <summary>
/// Beschreibt die App selbst eine Diskette ("Bespielen"), sieht der Motor neuen
/// Inhalt - und wuerde ihn sofort starten. Die App hinterlaesst deshalb kurz eine
/// Notiz mit dem neuen Fingerabdruck; passt er, ueberspringt der Motor genau diesen Zustand.
/// </summary>
public static class WriteGuard
{
    public const string FileName = "motor-skip.txt";

    /// <summary>Aeltere Notizen gelten nicht mehr.</summary>
    public static TimeSpan MaxAge { get; } = TimeSpan.FromMinutes(2);

    public static void Note(string userData, string signature, DateTime? now = null)
    {
        FloppyPaths.EnsureDirectory(userData);
        var stamp = (now ?? DateTime.UtcNow).ToString("O", CultureInfo.InvariantCulture);
        File.WriteAllText(Path.Combine(userData, FileName), stamp + "\n" + signature);
    }

    /// <summary>true = dieser Disketten-Zustand stammt von der App und soll nicht gestartet werden.</summary>
    public static bool Consume(string userData, string? signature, DateTime? now = null)
    {
        var file = Path.Combine(userData, FileName);
        if (signature is null || !File.Exists(file)) return false;
        try
        {
            var text = File.ReadAllText(file);
            var split = text.IndexOf('\n');
            if (split < 0) { File.Delete(file); return false; }

            var fresh = DateTime.TryParse(text[..split], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var written)
                        && (now ?? DateTime.UtcNow) - written.ToUniversalTime() <= MaxAge;
            var match = fresh && text[(split + 1)..] == signature;
            if (match || !fresh) File.Delete(file);
            return match;
        }
        catch
        {
            return false;
        }
    }
}
