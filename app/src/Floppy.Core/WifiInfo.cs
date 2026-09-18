using System.Diagnostics;

namespace Floppy.Core;

/// <summary>
/// Bestmoegliche Ermittlung des gerade verbundenen WLAN-Namens (SSID) - nur ein Hinweis fuer
/// die Chat-Einladung ("nutzt am besten dasselbe WLAN"), kein sicherer Wert. Liest ihn per
/// <c>netsh wlan show interfaces</c> aus, statt die deutlich aufwendigere WLAN-API einzubinden -
/// schlaegt etwas fehl (kein WLAN, kein Windows, o.ae.), gibt es einfach null zurueck.
/// </summary>
public static class WifiInfo
{
    public const int MaxNameLength = 32;

    public static string? CurrentSsid()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(2000))
            {
                try { proc.Kill(); } catch { /* egal, war nur ein Versuch */ }
                return null;
            }
            return ParseSsid(output);
        }
        catch
        {
            return null;   // kein netsh, kein WLAN-Adapter, keine Berechtigung, ... - dann eben kein Hinweis
        }
    }

    /// <summary>Sucht die Zeile "SSID : Name" (nicht "BSSID"); Label bleibt auch auf deutschem Windows "SSID".</summary>
    internal static string? ParseSsid(string netshOutput)
    {
        foreach (var rawLine in netshOutput.Split('\n'))
        {
            var line = rawLine.Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            if (!line[..colon].TrimEnd().Equals("SSID", StringComparison.OrdinalIgnoreCase)) continue;
            var value = line[(colon + 1)..].Trim();
            if (value.Length is 0 or > MaxNameLength) return null;
            return value;
        }
        return null;
    }
}
