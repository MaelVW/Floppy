using System.Net.Http.Headers;
using System.Text.Json;

namespace Floppy.Core.Updates;

/// <summary>
/// Fragt die GitHub-Releases von Floppy Hub ab. Nimmt bewusst die Liste (nicht "/releases/latest"),
/// weil GitHub dort Vorabversionen (Beta) ausblendet - waehrend der Beta-Phase soll genau das aber erkannt werden.
/// Braucht keinen Zugang: funktioniert nur, wenn das Repo oeffentlich ist (oder die URL auf einen anderen,
/// oeffentlich erreichbaren Feed zeigt).
/// </summary>
public sealed class GitHubReleaseFeed : IUpdateFeed, IDisposable
{
    public const string DefaultUrl = "https://api.github.com/repos/MaelVW/Floppy/releases";

    private readonly HttpClient _http;
    private readonly Uri _url;

    public GitHubReleaseFeed(string? url = null, HttpMessageHandler? handler = null)
    {
        _url = new Uri(string.IsNullOrWhiteSpace(url) ? DefaultUrl : url);
        _http = new HttpClient(handler ?? new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(10) }, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FloppyHub-UpdateCheck/2.0");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<UpdateInfo?> GetLatestAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync(_url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        return Parse(doc.RootElement);
    }

    /// <summary>
    /// Waehlt die HOECHSTE Version der Liste (Entwuerfe zaehlen nicht) - nicht einfach den ersten Eintrag:
    /// GitHub sortiert nach dem Datum des Commits, nicht nach Versionsnummer, ein spaeter neu getaggtes
    /// aelteres Release kaeme sonst nach vorn.
    /// </summary>
    internal static UpdateInfo? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) return null;

        UpdateInfo? best = null;
        AppVersion bestVersion = default;
        foreach (var release in root.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object) continue;
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
            if (!release.TryGetProperty("tag_name", out var tagProp) || tagProp.ValueKind != JsonValueKind.String ||
                tagProp.GetString() is not { Length: > 0 } tag) continue;

            var version = tag[0] is 'v' or 'V' ? tag[1..] : tag;
            if (!AppVersion.TryParse(version, out var parsed)) continue;
            if (best is not null && !parsed.IsNewerThan(bestVersion)) continue;

            var html = release.TryGetProperty("html_url", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() ?? "" : "";
            var name = release.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } s ? s : tag;
            var installerName = $"FloppyHubSetup-{version}.exe";
            best = new UpdateInfo(version, name, html, AssetUrl(release, installerName), AssetUrl(release, installerName + ".sha256.txt"));
            bestVersion = parsed;
        }
        return best;
    }

    private static string? AssetUrl(JsonElement release, string assetName)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object) continue;
            if (!asset.TryGetProperty("name", out var n) || n.GetString() != assetName) continue;
            if (asset.TryGetProperty("browser_download_url", out var u) && u.ValueKind == JsonValueKind.String) return u.GetString();
        }
        return null;
    }

    public void Dispose() => _http.Dispose();
}
