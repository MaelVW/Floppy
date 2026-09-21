using System.Security.Cryptography;

namespace Floppy.Core.Updates;

public enum UpdateDownloadError
{
    None,

    /// <summary>Der Link zeigt nicht auf das offizielle Release dieser Version - wird nie automatisch geladen.</summary>
    NotTrusted,

    /// <summary>Keine (oder keine passende) Pruefsumme - ohne sie wird nichts installiert.</summary>
    NoChecksum,

    Network,
    ChecksumMismatch,
    TooLarge,
    Cancelled,
}

public readonly record struct UpdateProgress(long BytesDone, long BytesTotal);

public sealed record UpdateDownloadResult(string? Path, UpdateDownloadError Error, string? Detail = null)
{
    public bool Ok => Error == UpdateDownloadError.None;
}

/// <summary>
/// Laedt das Setup eines Updates herunter und prueft es, bevor irgendetwas davon ausgefuehrt wird:
///  * nur von genau dem offiziellen Release-Pfad (https, github.com, dieses Repo, dieselbe Version),
///  * die SHA-256-Pruefsumme aus dem Release MUSS vorhanden sein und stimmen,
///  * hoechstens <see cref="MaxBytes"/>.
/// Schreibt erst in eine <c>.part</c>-Datei und benennt sie erst nach bestandener Pruefung um.
/// </summary>
public sealed class UpdateDownloader : IDisposable
{
    /// <summary>Nur dieses Repository liefert Updates (Ordner der Release-Dateien).</summary>
    public const string TrustedPrefix = "https://github.com/MaelVW/Floppy/releases/download/";

    public const long DefaultMaxBytes = 400L * 1024 * 1024;

    private readonly HttpClient _http;

    public UpdateDownloader(HttpMessageHandler? handler = null, long maxBytes = DefaultMaxBytes)
    {
        MaxBytes = maxBytes;
        _http = new HttpClient(handler ?? new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15), MaxAutomaticRedirections = 5 }, disposeHandler: true)
        {
            Timeout = TimeSpan.FromMinutes(30),   // ganzer Download; abbrechen geht jederzeit ueber das CancellationToken
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FloppyHub-Update/2.0");
    }

    public long MaxBytes { get; }

    /// <summary>Ist dieser Link genau der offizielle Download der Setup-Datei bzw. Pruefsumme dieser Version?</summary>
    public static bool IsTrusted(string? url, UpdateInfo info, bool checksum)
    {
        if (url is null || !AppVersion.TryParse(info.Version, out _) || info.Version.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))) return false;
        var expected = $"{TrustedPrefix}v{info.Version}/{info.InstallerFileName}{(checksum ? ".sha256.txt" : "")}";
        return string.Equals(url, expected, StringComparison.Ordinal);
    }

    public async Task<UpdateDownloadResult> DownloadAsync(UpdateInfo info, string directory, IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        if (!IsTrusted(info.InstallerUrl, info, checksum: false)) return new(null, UpdateDownloadError.NotTrusted);
        if (!IsTrusted(info.ChecksumUrl, info, checksum: true)) return new(null, UpdateDownloadError.NoChecksum);

        var finalPath = Path.Combine(directory, info.InstallerFileName);
        var partPath = finalPath + ".part";
        try
        {
            var expectedHash = await ReadChecksumAsync(info, ct).ConfigureAwait(false);
            if (expectedHash is null) return new(null, UpdateDownloadError.NoChecksum);

            Directory.CreateDirectory(directory);
            if (File.Exists(finalPath)) File.Delete(finalPath);

            using var response = await _http.GetAsync(info.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new(null, UpdateDownloadError.Network, $"HTTP {(int)response.StatusCode}");
            var total = response.Content.Headers.ContentLength ?? 0;
            if (total > MaxBytes) return new(null, UpdateDownloadError.TooLarge);

            string actualHash;
            var tooLarge = false;
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var target = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long done = 0;
                var lastReport = DateTime.UtcNow;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    done += read;
                    if (done > MaxBytes)
                    {
                        tooLarge = true;
                        break;
                    }
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    if (progress is not null && DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(100))
                    {
                        lastReport = DateTime.UtcNow;
                        progress.Report(new UpdateProgress(done, total));
                    }
                }
                progress?.Report(new UpdateProgress(done, total > 0 ? total : done));
                actualHash = Convert.ToHexString(hash.GetHashAndReset());
            }

            if (tooLarge) return Fail(partPath, UpdateDownloadError.TooLarge);   // erst nach dem Schliessen loeschen
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                return Fail(partPath, UpdateDownloadError.ChecksumMismatch);

            File.Move(partPath, finalPath, overwrite: true);
            return new(finalPath, UpdateDownloadError.None);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Discard(partPath);
            return new(null, UpdateDownloadError.Cancelled);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Discard(partPath);
            return new(null, UpdateDownloadError.Network, ex.Message);
        }
    }

    /// <summary>Die Pruefsummen-Datei hat die Form <c>HASH  FloppyHubSetup-x.exe</c>; der Dateiname muss stimmen.</summary>
    private async Task<string?> ReadChecksumAsync(UpdateInfo info, CancellationToken ct)
    {
        using var response = await _http.GetAsync(info.ChecksumUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        if (response.Content.Headers.ContentLength > 4096) return null;

        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (text.Length > 4096) return null;
        var parts = text.Split([' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0].Length != 64 || !parts[0].All(char.IsAsciiHexDigit)) return null;
        return parts[1] == info.InstallerFileName ? parts[0].ToUpperInvariant() : null;
    }

    private static UpdateDownloadResult Fail(string partPath, UpdateDownloadError error)
    {
        Discard(partPath);
        return new(null, error);
    }

    private static void Discard(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* bleibt liegen, wird beim naechsten Start aufgeraeumt */ }
    }

    public void Dispose() => _http.Dispose();
}
