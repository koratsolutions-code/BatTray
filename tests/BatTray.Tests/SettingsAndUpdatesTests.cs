using BatTray.Core.Models;
using BatTray.Core.Settings;
using BatTray.Core.Updates;

namespace BatTray.Tests;

public class SettingsAndUpdatesTests
{
    // ---------- Settings ----------

    [Fact]
    public void Defaults_FollowWindowsAndCheckForUpdates()
    {
        var s = new AppSettings();
        Assert.Equal("auto", s.Language);
        Assert.Equal("auto", s.Theme);
        Assert.True(s.CheckForUpdates);
        Assert.Empty(s.IgnoredReceiverIds);
    }

    [Theory]
    [InlineData("ES", "es")]
    [InlineData("en", "en")]
    [InlineData("fr", "auto")]
    [InlineData(null, "auto")]
    public void Normalize_Language(string? value, string expected) =>
        Assert.Equal(expected, (new AppSettings() with { Language = value! }).Normalize().Language);

    [Theory]
    [InlineData("Dark", "dark")]
    [InlineData("light", "light")]
    [InlineData("blue", "auto")]
    public void Normalize_Theme(string value, string expected) =>
        Assert.Equal(expected, (new AppSettings() with { Theme = value }).Normalize().Theme);

    [Fact]
    public void IgnoredReceivers_AddRemoveCaseInsensitive()
    {
        const string id = "usb:25A7FA70:abc";
        var s = new AppSettings().WithIgnored(id, true);
        Assert.True(s.IsIgnored(id.ToLowerInvariant()));
        Assert.False(s.WithIgnored(id, false).IsIgnored(id));
    }

    [Fact]
    public void Settings_RoundTripNewFields()
    {
        var path = Path.Combine(Path.GetTempPath(), $"battray-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            store.Save(new AppSettings { Language = "es", Theme = "dark", CheckForUpdates = false }.WithIgnored("usb:1:2", true));
            var loaded = store.Load();
            Assert.Equal("es", loaded.Language);
            Assert.Equal("dark", loaded.Theme);
            Assert.False(loaded.CheckForUpdates);
            Assert.True(loaded.IsIgnored("usb:1:2"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---------- Connection groups ----------

    [Theory]
    [InlineData("bt:D104C6AECB01", DeviceConnection.Bluetooth)]
    [InlineData("pnp:0123", DeviceConnection.Bluetooth)]
    [InlineData("usb:32060861:cc2e", DeviceConnection.Receiver)]
    [InlineData("gip:xyz", DeviceConnection.Controller)]
    [InlineData("sony:abc", DeviceConnection.Controller)]
    [InlineData("fake-mouse", DeviceConnection.Other)]
    public void Connection_FromIdScheme(string id, DeviceConnection expected) =>
        Assert.Equal(expected, DeviceConnections.Of(id));

    // ---------- Update check ----------

    [Theory]
    [InlineData("v1.1.0", "1.1.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("v2.0.0-beta.1", "2.0.0")]
    public void ParseVersion(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag));

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    public void ParseVersion_RejectsNonVersions(string tag) => Assert.Null(UpdateChecker.ParseVersion(tag));

    [Fact]
    public void Parse_ReadsTagAndPage()
    {
        var release = UpdateChecker.Parse("""
            { "tag_name": "v1.1.0", "html_url": "https://github.com/koratsolutions-code/BatTray/releases/tag/v1.1.0",
              "draft": false, "prerelease": false }
            """);
        Assert.Equal(new Version(1, 1, 0), release?.Version);
        Assert.Equal("https://github.com/koratsolutions-code/BatTray/releases/tag/v1.1.0", release?.PageUrl);
    }

    [Fact]
    public void Parse_IgnoresPrereleasesAndDrafts()
    {
        Assert.Null(UpdateChecker.Parse("""{ "tag_name": "v2.0.0", "prerelease": true }"""));
        Assert.Null(UpdateChecker.Parse("""{ "tag_name": "v2.0.0", "draft": true }"""));
        Assert.Null(UpdateChecker.Parse("not json"));
    }

    [Fact]
    public void Parse_NeverOpensPagesOutsideTheRepository()
    {
        var release = UpdateChecker.Parse("""{ "tag_name": "v1.1.0", "html_url": "https://evil.example/download" }""");
        Assert.Equal(UpdateChecker.ReleasesPage, release?.PageUrl);
    }

    [Fact]
    public async Task FindNewer_ComparesWithCurrentVersion()
    {
        using var http = new HttpClient(new FixedResponse("""{ "tag_name": "v1.1.0", "html_url": "https://github.com/koratsolutions-code/BatTray/releases/tag/v1.1.0" }"""));
        var checker = new UpdateChecker(http);

        Assert.NotNull(await checker.FindNewerAsync(new Version(1, 0, 0)));
        Assert.Null(await checker.FindNewerAsync(new Version(1, 1, 0)));
        Assert.Null(await checker.FindNewerAsync(new Version(1, 2, 0)));
    }

    private sealed class FixedResponse(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(UpdateChecker.LatestReleaseApi, request.RequestUri?.ToString());
            Assert.Contains("BatTray", request.Headers.UserAgent.ToString());
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
