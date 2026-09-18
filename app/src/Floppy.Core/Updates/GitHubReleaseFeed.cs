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

    internal static UpdateInfo? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;
        var first = root[0];
        if (first.ValueKind != JsonValueKind.Object) return null;
        if (!first.TryGetProperty("tag_name", out var tagProp) || tagProp.GetString() is not { Length: > 0 } tag) return null;

        var version = tag.Length > 0 && (tag[0] == 'v' || tag[0] == 'V') ? tag[1..] : tag;
        var html = first.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        var name = first.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } s ? s : tag;
        return new UpdateInfo(version, name, html);
    }

    public void Dispose() => _http.Dispose();
}
