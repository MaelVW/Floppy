using Floppy.Core;
using Godot;

namespace FloppyHub.App.Services;

/// <summary>
/// Steam-Titelbilder: einmal aus dem oeffentlichen Steam-CDN laden, danach lokal
/// gespeichert (auch offline). Nur Zahlen-IDs, nur JPEG, hoechstens 2 MB.
/// </summary>
public sealed class CoverCache(string directory)
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan RetryMissingAfter = TimeSpan.FromDays(7);

    private static readonly System.Net.Http.HttpClient Http = CreateClient();
    private readonly Dictionary<string, Texture2D?> _memory = new();

    public string Directory { get; } = directory;

    public async Task<Texture2D?> GetAsync(string appId, bool allowDownload)
    {
        if (appId.Length == 0 || appId.Length > 10 || !appId.All(char.IsAsciiDigit)) return null;
        if (_memory.TryGetValue(appId, out var known)) return known;

        var file = System.IO.Path.Combine(Directory, appId + ".jpg");
        var missing = System.IO.Path.Combine(Directory, appId + ".fehlt");
        byte[]? bytes = null;

        try
        {
            if (File.Exists(file))
            {
                bytes = await File.ReadAllBytesAsync(file);
            }
            else if (allowDownload && !(File.Exists(missing) && DateTime.Now - File.GetLastWriteTime(missing) < RetryMissingAfter))
            {
                bytes = await DownloadAsync(appId);
                FloppyPaths.EnsureDirectory(Directory);
                if (bytes is not null) await File.WriteAllBytesAsync(file, bytes);
                else await File.WriteAllTextAsync(missing, DateTime.Now.ToString("O"));
            }
        }
        catch (Exception ex)
        {
            GD.Print($"Cover {appId}: {ex.Message}");
        }

        Texture2D? texture = null;
        if (bytes is not null)
        {
            var image = new Image();
            if (image.LoadJpgFromBuffer(bytes) == Error.Ok) texture = ImageTexture.CreateFromImage(image);
        }
        _memory[appId] = texture;
        return texture;
    }

    public long SizeOnDisk()
    {
        try
        {
            return System.IO.Directory.Exists(Directory)
                ? new DirectoryInfo(Directory).EnumerateFiles().Sum(f => f.Length)
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    public void Clear()
    {
        _memory.Clear();
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return;
            foreach (var f in new DirectoryInfo(Directory).EnumerateFiles())
                if (f.Extension is ".jpg" or ".fehlt") f.Delete();
        }
        catch { }
    }

    private static async Task<byte[]?> DownloadAsync(string appId)
    {
        using var response = await Http.GetAsync(SteamApps.HeaderImageUrl(appId), System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) return null;
        if (response.Content.Headers.ContentLength is > MaxBytes) return null;

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBytes) return null;
        }
        var data = buffer.ToArray();
        return data is [0xFF, 0xD8, ..] ? data : null;   // nur echte JPEGs
    }

    private static System.Net.Http.HttpClient CreateClient()
    {
        var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FloppyHub/2.0");
        return client;
    }
}
