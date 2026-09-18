using System.Net;
using System.Text;
using System.Text.Json;
using Floppy.Core.Updates;

namespace Floppy.Core.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("2.0.0", 2, 0, 0, null)]
    [InlineData("v2.0.0", 2, 0, 0, null)]
    [InlineData("2.0.0-beta.1", 2, 0, 0, "beta.1")]
    [InlineData("V2.0.0-beta.1", 2, 0, 0, "beta.1")]
    public void Parses_valid_versions(string text, int major, int minor, int patch, string? pre)
    {
        Assert.True(AppVersion.TryParse(text, out var v));
        Assert.Equal(new AppVersion(major, minor, patch, pre), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2.0")]
    [InlineData("2.0.0.0")]
    [InlineData("2.0.x")]
    [InlineData("2.0.0-")]
    [InlineData(null)]
    public void Rejects_invalid_versions(string? text) => Assert.False(AppVersion.TryParse(text, out _));

    [Theory]
    [InlineData("2.0.0-beta.2", "2.0.0-beta.1")]     // hoehere Beta
    [InlineData("2.0.0", "2.0.0-beta.1")]             // fertig schlaegt Beta
    [InlineData("2.0.1", "2.0.0")]                    // Patch
    [InlineData("2.1.0", "2.0.9")]                    // Minor vor Patch
    [InlineData("3.0.0", "2.9.9")]                    // Major vor Minor
    [InlineData("2.0.0-beta.10", "2.0.0-beta.9")]     // numerisch, nicht als Text vergleichen
    [InlineData("2.0.0-rc.1", "2.0.0-beta.9")]        // Text-Vorabversionen: alphabetisch
    public void Detects_newer_version(string newer, string older)
    {
        Assert.True(AppVersion.TryParse(newer, out var n));
        Assert.True(AppVersion.TryParse(older, out var o));
        Assert.True(n.IsNewerThan(o));
        Assert.False(o.IsNewerThan(n));
    }

    [Fact]
    public void Same_version_is_not_newer()
    {
        Assert.True(AppVersion.TryParse("2.0.0-beta.1", out var a));
        Assert.True(AppVersion.TryParse("2.0.0-beta.1", out var b));
        Assert.False(a.IsNewerThan(b));
    }
}

public class GitHubReleaseFeedTests
{
    private const string SampleReleases = """
        [
          {
            "tag_name": "v2.0.0-beta.1",
            "name": "Floppy Hub 2.0.0-beta.1",
            "html_url": "https://github.com/MaelVW/Floppy/releases/tag/v2.0.0-beta.1",
            "prerelease": true
          },
          {
            "tag_name": "v1.0.0",
            "name": "Floppy Hub 1.0.0",
            "html_url": "https://github.com/MaelVW/Floppy/releases/tag/v1.0.0",
            "prerelease": false
          }
        ]
        """;

    [Fact]
    public void Parses_newest_entry_including_prerelease()
    {
        using var doc = JsonDocument.Parse(SampleReleases);
        var info = GitHubReleaseFeed.Parse(doc.RootElement);
        Assert.NotNull(info);
        Assert.Equal("2.0.0-beta.1", info!.Version);
        Assert.Equal("Floppy Hub 2.0.0-beta.1", info.Name);
        Assert.EndsWith("/v2.0.0-beta.1", info.HtmlUrl);
    }

    [Fact]
    public void Empty_list_yields_null()
    {
        using var doc = JsonDocument.Parse("[]");
        Assert.Null(GitHubReleaseFeed.Parse(doc.RootElement));
    }

    [Fact]
    public async Task GetLatestAsync_uses_the_fake_handler()
    {
        using var handler = new FakeGitHub(SampleReleases);
        using var feed = new GitHubReleaseFeed("https://example.invalid/releases", handler);
        var info = await feed.GetLatestAsync(CancellationToken.None);
        Assert.Equal("2.0.0-beta.1", info?.Version);
    }

    [Fact]
    public async Task Non_success_status_yields_null()
    {
        using var handler = new FakeGitHub(SampleReleases, HttpStatusCode.NotFound);
        using var feed = new GitHubReleaseFeed("https://example.invalid/releases", handler);
        Assert.Null(await feed.GetLatestAsync(CancellationToken.None));
    }

    private sealed class FakeGitHub(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
