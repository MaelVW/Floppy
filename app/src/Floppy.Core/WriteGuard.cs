using System.Globalization;

namespace Floppy.Core;

public enum GuardState
{
    /// <summary>Keine (gueltige) Notiz - Diskette normal behandeln.</summary>
    None,

    /// <summary>Die App schreibt gerade - noch nichts tun, gleich nochmal nachsehen.</summary>
    Pending,

    /// <summary>Dieser Zustand stammt von der App - nicht starten.</summary>
    Written,
}

/// <summary>
/// Beschreibt die App selbst eine Diskette ("Bespielen"), sieht der Motor neuen
/// Inhalt - und wuerde ihn sofort starten. Die App hinterlaesst deshalb eine Notiz:
/// erst "schreibe gerade" (<see cref="Begin"/>), danach den neuen Fingerabdruck
/// (<see cref="Note"/>). Passt er, ueberspringt der Motor genau diesen Zustand.
/// </summary>
public static class WriteGuard
{
    public const string FileName = "motor-skip.txt";
    private const string PendingMarker = "pending";

    /// <summary>Aeltere Notizen gelten nicht mehr.</summary>
    public static TimeSpan MaxAge { get; } = TimeSpan.FromMinutes(2);

    /// <summary>So lange darf "schreibe gerade" hoechstens dauern (Absturz der App).</summary>
    public static TimeSpan PendingMaxAge { get; } = TimeSpan.FromSeconds(30);

    public static void Begin(string userData, DateTime? now = null) => Save(userData, PendingMarker, now);

    public static void Note(string userData, string signature, DateTime? now = null) => Save(userData, signature, now);

    public static void Cancel(string userData)
    {
        try { File.Delete(Path.Combine(userData, FileName)); } catch { }
    }

    public static GuardState Check(string userData, string? signature, DateTime? now = null)
    {
        var file = Path.Combine(userData, FileName);
        if (signature is null || !File.Exists(file)) return GuardState.None;
        try
        {
            var text = File.ReadAllText(file);
            var split = text.IndexOf('\n');
            if (split < 0 || !DateTime.TryParse(text[..split], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var written))
            {
                File.Delete(file);
                return GuardState.None;
            }

            var age = (now ?? DateTime.UtcNow) - written.ToUniversalTime();
            var content = text[(split + 1)..];

            if (content == PendingMarker)
            {
                if (age <= PendingMaxAge) return GuardState.Pending;
                File.Delete(file);
                return GuardState.None;
            }

            if (age > MaxAge)
            {
                File.Delete(file);
                return GuardState.None;
            }
            if (content != signature) return GuardState.None;

            File.Delete(file);
            return GuardState.Written;
        }
        catch
        {
            return GuardState.None;
        }
    }

    private static void Save(string userData, string content, DateTime? now)
    {
        FloppyPaths.EnsureDirectory(userData);
        var stamp = (now ?? DateTime.UtcNow).ToString("O", CultureInfo.InvariantCulture);
        File.WriteAllText(Path.Combine(userData, FileName), stamp + "\n" + content);
    }
}
