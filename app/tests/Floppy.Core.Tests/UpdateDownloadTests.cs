using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Floppy.Core.Updates;

namespace Floppy.Core.Tests;

public class UpdateFeedAssetsTests
{
    private static string Release(string tag, bool draft = false, bool withAssets = true) => $$"""
        {
          "tag_name": "{{tag}}", "name": "Floppy Hub {{tag[1..]}}", "draft": {{(draft ? "true" : "false")}},
          "html_url": "https://github.com/MaelVW/Floppy/releases/tag/{{tag}}",
          "assets": [{{(withAssets ? $$"""
            { "name": "FloppyHubSetup-{{tag[1..]}}.exe", "browser_download_url": "https://github.com/MaelVW/Floppy/releases/download/{{tag}}/FloppyHubSetup-{{tag[1..]}}.exe" },
            { "name": "FloppyHubSetup-{{tag[1..]}}.exe.sha256.txt", "browser_download_url": "https://github.com/MaelVW/Floppy/releases/download/{{tag}}/FloppyHubSetup-{{tag[1..]}}.exe.sha256.txt" }
            """ : "")}}]
        }
        """;

    private static UpdateInfo? Parse(params string[] releases)
    {
        using var doc = JsonDocument.Parse("[" + string.Join(",", releases) + "]");
        return GitHubReleaseFeed.Parse(doc.RootElement);
    }

    [Fact]
    public void Reads_installer_and_checksum_links()
    {
        var info = Parse(Release("v2.0.0-beta.6"))!;
        Assert.Equal("2.0.0-beta.6", info.Version);
        Assert.Equal("https://github.com/MaelVW/Floppy/releases/download/v2.0.0-beta.6/FloppyHubSetup-2.0.0-beta.6.exe", info.InstallerUrl);
        Assert.EndsWith(".exe.sha256.txt", info.ChecksumUrl);
        Assert.True(UpdateDownloader.IsTrusted(info.InstallerUrl, info, checksum: false));
        Assert.True(UpdateDownloader.IsTrusted(info.ChecksumUrl, info, checksum: true));
    }

    [Fact]
    public void Takes_the_highest_version_not_the_first_entry_and_skips_drafts()
    {
        // GitHub sortiert nach Commit-Datum: das aelteste Release kann vorn stehen
        var info = Parse(Release("v2.0.0-beta.3"), Release("v2.0.0-beta.5"), Release("v2.0.0-beta.9", draft: true), Release("v2.0.0-beta.4"))!;
        Assert.Equal("2.0.0-beta.5", info.Version);
    }

    [Fact]
    public void A_release_without_files_can_still_be_announced_but_not_installed()
    {
        var info = Parse(Release("v2.0.0-beta.6", withAssets: false))!;
        Assert.Null(info.InstallerUrl);
        Assert.Null(info.ChecksumUrl);
    }
}

public class UpdateDownloaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "floppy-upd-" + Guid.NewGuid());
    private readonly byte[] _setup = RandomNumberGenerator.GetBytes(300_000);
    private static readonly UpdateInfo Info = new("2.0.0-beta.6", "Floppy Hub 2.0.0-beta.6", "https://github.com/MaelVW/Floppy/releases/tag/v2.0.0-beta.6",
        "https://github.com/MaelVW/Floppy/releases/download/v2.0.0-beta.6/FloppyHubSetup-2.0.0-beta.6.exe",
        "https://github.com/MaelVW/Floppy/releases/download/v2.0.0-beta.6/FloppyHubSetup-2.0.0-beta.6.exe.sha256.txt");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string SumFile(byte[]? content = null, string name = "FloppyHubSetup-2.0.0-beta.6.exe") =>
        $"{Convert.ToHexString(SHA256.HashData(content ?? _setup))}  {name}\r\n";

    private static FakeSite Site(byte[] setup, string? sum, HttpStatusCode setupStatus = HttpStatusCode.OK) => new()
    {
        Setup = setup, Sum = sum, SetupStatus = setupStatus,
    };

    [Fact]
    public async Task Downloads_verifies_and_reports_progress()
    {
        var site = Site(_setup, SumFile());
        using var dl = new UpdateDownloader(site);
        var seen = new List<UpdateProgress>();
        var result = await dl.DownloadAsync(Info, _dir, new SyncProgress(seen.Add), CancellationToken.None);

        Assert.True(result.Ok, result.Detail);
        Assert.Equal(_setup, await File.ReadAllBytesAsync(result.Path!));
        Assert.Equal(Path.Combine(_dir, "FloppyHubSetup-2.0.0-beta.6.exe"), result.Path);
        Assert.False(File.Exists(result.Path + ".part"));
        Assert.Equal(_setup.Length, seen[^1].BytesDone);
        Assert.Equal(_setup.Length, seen[^1].BytesTotal);
    }

    [Fact]
    public async Task A_file_that_does_not_match_the_checksum_is_thrown_away()
    {
        var site = Site(_setup, SumFile(RandomNumberGenerator.GetBytes(10)));
        using var dl = new UpdateDownloader(site);
        var result = await dl.DownloadAsync(Info, _dir, null, CancellationToken.None);

        Assert.Equal(UpdateDownloadError.ChecksumMismatch, result.Error);
        Assert.Empty(Directory.GetFiles(_dir));   // weder fertige Datei noch .part
    }

    [Theory]
    [InlineData(null)]                                            // keine Pruefsummen-Datei
    [InlineData("Unsinn")]                                        // kein Hash
    [InlineData("ABCD  FloppyHubSetup-2.0.0-beta.6.exe")]         // Hash zu kurz
    public async Task Without_a_usable_checksum_nothing_is_downloaded(string? sum)
    {
        var site = Site(_setup, sum);
        using var dl = new UpdateDownloader(site);
        var result = await dl.DownloadAsync(Info, _dir, null, CancellationToken.None);
        Assert.Equal(UpdateDownloadError.NoChecksum, result.Error);
        Assert.False(site.SetupRequested);
    }

    [Fact]
    public async Task A_checksum_for_another_file_does_not_count()
    {
        var site = Site(_setup, SumFile(name: "FloppyHubSetup-2.0.0-beta.5.exe"));
        using var dl = new UpdateDownloader(site);
        Assert.Equal(UpdateDownloadError.NoChecksum, (await dl.DownloadAsync(Info, _dir, null, CancellationToken.None)).Error);
        Assert.False(site.SetupRequested);
    }

    [Theory]
    [InlineData("https://evil.example/MaelVW/Floppy/releases/download/v2.0.0-beta.6/FloppyHubSetup-2.0.0-beta.6.exe")]
    [InlineData("http://github.com/MaelVW/Floppy/releases/download/v2.0.0-beta.6/FloppyHubSetup-2.0.0-beta.6.exe")]
    [InlineData("https://github.com/Someone/Floppy/releases/download/v2.0.0-beta.6/FloppyHubSetup-2.0.0-beta.6.exe")]
    [InlineData("https://github.com/MaelVW/Floppy/releases/download/v2.0.0-beta.5/FloppyHubSetup-2.0.0-beta.6.exe")]   // andere Version im Pfad
    [InlineData("https://github.com/MaelVW/Floppy/releases/download/v2.0.0-beta.6/Other.exe")]
    [InlineData("https://github.com/MaelVW/Floppy/releases/download/v2.0.0-beta.6/FloppyHubSetup-2.0.0-beta.6.exe?x=1")]
    [InlineData("https://github.com.evil.example/MaelVW/Floppy/releases/download/v2.0.0-beta.6/FloppyHubSetup-2.0.0-beta.6.exe")]
    public async Task Only_the_official_release_path_is_ever_downloaded(string url)
    {
        var site = Site(_setup, SumFile());
        using var dl = new UpdateDownloader(site);
        var result = await dl.DownloadAsync(Info with { InstallerUrl = url }, _dir, null, CancellationToken.None);
        Assert.Equal(UpdateDownloadError.NotTrusted, result.Error);
        Assert.Equal(0, site.Requests);   // nicht einmal angefragt
    }

    [Fact]
    public async Task A_version_with_odd_characters_is_never_trusted()
    {
        var odd = Info with { Version = "2.0.0-beta.6/../x" };
        Assert.False(UpdateDownloader.IsTrusted(odd.InstallerUrl, odd, checksum: false));
        Assert.Equal(UpdateDownloadError.NotTrusted, (await new UpdateDownloader(Site(_setup, SumFile())).DownloadAsync(odd, _dir, null, CancellationToken.None)).Error);
    }

    [Fact]
    public async Task Too_big_files_are_refused_by_header_and_while_streaming()
    {
        using (var dl = new UpdateDownloader(Site(_setup, SumFile()), maxBytes: 1000))
            Assert.Equal(UpdateDownloadError.TooLarge, (await dl.DownloadAsync(Info, _dir, null, CancellationToken.None)).Error);

        var noLength = Site(_setup, SumFile());
        noLength.HideContentLength = true;
        using (var dl = new UpdateDownloader(noLength, maxBytes: 1000))
            Assert.Equal(UpdateDownloadError.TooLarge, (await dl.DownloadAsync(Info, _dir, null, CancellationToken.None)).Error);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Server_errors_and_cancelling_are_reported_and_leave_nothing_behind()
    {
        using (var dl = new UpdateDownloader(Site(_setup, SumFile(), HttpStatusCode.NotFound)))
            Assert.Equal(UpdateDownloadError.Network, (await dl.DownloadAsync(Info, _dir, null, CancellationToken.None)).Error);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using (var dl = new UpdateDownloader(Site(_setup, SumFile())))
            Assert.Equal(UpdateDownloadError.Cancelled, (await dl.DownloadAsync(Info, _dir, null, cts.Token)).Error);
        Assert.Empty(Directory.Exists(_dir) ? Directory.GetFiles(_dir) : []);
    }

    [Fact]
    public void Setup_is_started_silently_and_asks_for_the_right_restart()
    {
        Assert.Contains("/VERYSILENT", UpdateInstaller.BuildArguments(false));
        Assert.Contains("/SUPPRESSMSGBOXES", UpdateInstaller.BuildArguments(false));
        Assert.EndsWith("/RELAUNCH=hub", UpdateInstaller.BuildArguments(false));
        Assert.EndsWith("/RELAUNCH=hub,motor", UpdateInstaller.BuildArguments(true));
        Assert.Throws<FileNotFoundException>(() => UpdateInstaller.Start(Path.Combine(_dir, "gibt-es-nicht.exe"), false));
    }

    private sealed class SyncProgress(Action<UpdateProgress> onReport) : IProgress<UpdateProgress>
    {
        public void Report(UpdateProgress value) => onReport(value);
    }

    private sealed class FakeSite : HttpMessageHandler
    {
        public byte[] Setup { get; init; } = [];
        public string? Sum { get; init; }
        public HttpStatusCode SetupStatus { get; init; } = HttpStatusCode.OK;
        public bool HideContentLength { get; set; }
        public int Requests { get; private set; }
        public bool SetupRequested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests++;
            var url = request.RequestUri!.ToString();
            if (url.EndsWith(".sha256.txt", StringComparison.Ordinal))
                return Task.FromResult(Sum is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Sum, Encoding.ASCII) });

            SetupRequested = true;
            HttpContent content = HideContentLength ? new HiddenLengthContent(Setup) : new ByteArrayContent(Setup);
            return Task.FromResult(new HttpResponseMessage(SetupStatus) { Content = content });
        }
    }

    /// <summary>Wie ein Server, der die Groesse nicht vorab nennt (chunked).</summary>
    private sealed class HiddenLengthContent(byte[] data) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) => stream.WriteAsync(data).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
