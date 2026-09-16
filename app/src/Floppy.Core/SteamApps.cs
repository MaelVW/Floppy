using System.Text.RegularExpressions;

namespace Floppy.Core;

/// <summary>Steam-Helfer: AppID aus Zahl oder Link, bekannte Namen.</summary>
public static partial class SteamApps
{
    [GeneratedRegex(@"store\.steampowered\.com/app/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex StoreUrl();

    [GeneratedRegex(@"rungameid/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RunGameId();

    [GeneratedRegex(@"/(\d+)(/|$)")]
    private static partial Regex AnyNumberSegment();

    /// <summary>Nimmt "220", einen Store-Link oder steam://rungameid/220 und liefert die AppID.</summary>
    public static bool TryResolveAppId(string? input, out string appId)
    {
        appId = string.Empty;
        var t = PathRules.StripQuotes(input);
        if (t.Length == 0) return false;

        if (t.All(char.IsAsciiDigit)) { appId = t; return true; }
        foreach (var rx in new[] { StoreUrl(), RunGameId(), AnyNumberSegment() })
        {
            var m = rx.Match(t);
            if (m.Success) { appId = m.Groups[1].Value; return true; }
        }
        return false;
    }

    public static string RunUri(string appId) => $"steam://rungameid/{appId}";

    /// <summary>Titelbild aus dem oeffentlichen Steam-CDN (fuer Cover in der Bibliothek).</summary>
    public static string HeaderImageUrl(string appId) =>
        $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";

    /// <summary>Ein paar Klassiker - gleiche Liste wie FloppyLib.ps1.</summary>
    public static IReadOnlyDictionary<string, string> KnownNames { get; } = new Dictionary<string, string>
    {
        ["70"] = "Half-Life",
        ["220"] = "Half-Life 2",
        ["400"] = "Portal",
        ["620"] = "Portal 2",
        ["240"] = "Counter-Strike: Source",
        ["730"] = "Counter-Strike 2",
        ["440"] = "Team Fortress 2",
        ["570"] = "Dota 2",
        ["4000"] = "Garry's Mod",
        ["105600"] = "Terraria",
        ["220200"] = "Kerbal Space Program",
        ["292030"] = "The Witcher 3: Wild Hunt",
        ["271590"] = "Grand Theft Auto V",
        ["346110"] = "ARK: Survival Evolved",
        ["236850"] = "Stellaris",
        ["294100"] = "RimWorld",
        ["413150"] = "Stardew Valley",
        ["739630"] = "Phasmophobia",
        ["1145360"] = "Hades",
        ["1091500"] = "Cyberpunk 2077",
    };

    public static string? TryGetKnownName(string appId) =>
        KnownNames.TryGetValue(appId.Trim(), out var n) ? n : null;
}
