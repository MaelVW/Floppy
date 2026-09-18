namespace Floppy.Core.Updates;

/// <summary>
/// Versionsnummer wie "2.0.0" oder "2.0.0-beta.1". Vergleich nach SemVer-Regeln:
/// eine fertige Version schlaegt eine Vorabversion gleicher Zahl, sonst zaehlt der
/// Vorabversions-Teil (numerische Abschnitte wie "beta.10" &gt; "beta.9").
/// </summary>
public readonly record struct AppVersion(int Major, int Minor, int Patch, string? PreRelease)
{
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s[1..];

        var dash = s.IndexOf('-');
        var core = dash < 0 ? s : s[..dash];
        var pre = dash < 0 ? null : s[(dash + 1)..];
        if (dash >= 0 && string.IsNullOrEmpty(pre)) return false;

        var parts = core.Split('.');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var major) || major < 0) return false;
        if (!int.TryParse(parts[1], out var minor) || minor < 0) return false;
        if (!int.TryParse(parts[2], out var patch) || patch < 0) return false;

        version = new AppVersion(major, minor, patch, pre);
        return true;
    }

    /// <summary>true, wenn diese Version neuer ist als <paramref name="other"/>.</summary>
    public bool IsNewerThan(AppVersion other)
    {
        if (Major != other.Major) return Major > other.Major;
        if (Minor != other.Minor) return Minor > other.Minor;
        if (Patch != other.Patch) return Patch > other.Patch;
        if (PreRelease is null) return other.PreRelease is not null;   // fertige Version schlaegt Vorabversion
        if (other.PreRelease is null) return false;
        return ComparePreRelease(PreRelease, other.PreRelease) > 0;
    }

    private static int ComparePreRelease(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            if (i >= pa.Length) return -1;
            if (i >= pb.Length) return 1;
            var na = int.TryParse(pa[i], out var ia);
            var nb = int.TryParse(pb[i], out var ib);
            var cmp = (na, nb) switch
            {
                (true, true) => ia.CompareTo(ib),
                (false, false) => string.CompareOrdinal(pa[i], pb[i]),
                (true, false) => -1,   // Zahl zaehlt als "kleiner" als Text (SemVer-Regel)
                (false, true) => 1,
            };
            if (cmp != 0) return cmp;
        }
        return 0;
    }

    public override string ToString() => PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";
}
