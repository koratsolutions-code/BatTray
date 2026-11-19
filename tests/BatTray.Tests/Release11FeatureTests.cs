using System.Security.Cryptography;
using BatTray.Core;
using BatTray.Core.History;
using BatTray.Core.Models;
using BatTray.Core.Settings;
using BatTray.Core.Updates;

namespace BatTray.Tests;

/// <summary>Per-device alerts, full-charge notice, device order, history and one-click updates.</summary>
public class Release11FeatureTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static Device D(string id, int? percent, bool? charging = null, bool estimated = false) =>
        new(id, id, DeviceKind.Mouse, percent, T0, Charging: charging, Estimated: estimated);

    // ---------- Per-device alerts ----------

    [Fact]
    public void AlertThreshold_GeneralCustomAndMuted()
    {
        var s = new AppSettings { CriticalThreshold = 15 }
            .WithCustomization("a", new DeviceCustomization(AlertAt: 40))
            .WithCustomization("b", new DeviceCustomization(NoAlerts: true));
        Assert.Equal(40, s.AlertThresholdFor("a"));
        Assert.Null(s.AlertThresholdFor("b"));
        Assert.Equal(15, s.AlertThresholdFor("c"));
        Assert.Null((s with { LowBatteryAlerts = false }).AlertThresholdFor("a"));
    }

    [Fact]
    public void Customization_WithOnlyAlertSettings_IsKept()
    {
        Assert.False(new DeviceCustomization(AlertAt: 30).Normalize().IsEmpty);
        Assert.False(new DeviceCustomization(NoAlerts: true).Normalize().IsEmpty);
        Assert.Equal(95, new DeviceCustomization(AlertAt: 400).Normalize().AlertAt);
        Assert.Null(new DeviceCustomization(AlertAt: 30, NoAlerts: true).Normalize().AlertAt);
    }

    [Fact]
    public void AlertTracker_UsesEachDeviceLevel_AndRearmsAboveIt()
    {
        var tracker = new BatteryAlertTracker();
        int? Level(Device d) => d.Id switch { "a" => 40, "b" => null, _ => 15 };

        var due = tracker.Evaluate([D("a", 38), D("b", 5), D("c", 30)], Level, rearmThreshold: 30);
        Assert.Equal(["a"], due.Select(d => d.Id));

        // Still below its level: no repeat. Back above 40 re-arms it.
        Assert.Empty(tracker.Evaluate([D("a", 35)], Level, 30));
        Assert.Empty(tracker.Evaluate([D("a", 45)], Level, 30));
        Assert.Single(tracker.Evaluate([D("a", 39)], Level, 30));
    }

    // ---------- Full charge ----------

    [Fact]
    public void FullCharge_AnnouncedOnceWhileCharging_RearmsBelow95()
    {
        var tracker = new FullChargeTracker();
        Assert.Empty(tracker.Evaluate([D("a", 80, charging: true)]));
        Assert.Single(tracker.Evaluate([D("a", 100, charging: true)]));
        Assert.Empty(tracker.Evaluate([D("a", 100, charging: true)]));
        Assert.Empty(tracker.Evaluate([D("a", 100, charging: false)]));
        Assert.Empty(tracker.Evaluate([D("a", 90, charging: false)]));
        Assert.Single(tracker.Evaluate([D("a", 100, charging: true)]));
    }

    [Fact]
    public void FullCharge_DeviceWithoutChargingFlag_AnnouncedWhenLevelRisesTo100()
    {
        var tracker = new FullChargeTracker();
        Assert.Empty(tracker.Evaluate([D("a", 100)])); // first reading: unknown history, stay quiet
        Assert.Empty(tracker.Evaluate([D("a", 96)]));
        Assert.Single(tracker.Evaluate([D("a", 100)]));
    }

    [Fact]
    public void FullCharge_IgnoresEstimates()
    {
        var tracker = new FullChargeTracker();
        tracker.Evaluate([D("a", 50, estimated: true)]);
        Assert.Empty(tracker.Evaluate([D("a", 100, charging: true, estimated: true)]));
    }

    // ---------- Order ----------

    [Fact]
    public void Order_SavedIdsFirst_OthersKeepTheirOrder()
    {
        var s = new AppSettings { DeviceOrder = ["c", "a"] };
        var ordered = s.Order([D("a", 1), D("b", 2), D("c", 3), D("d", 4)]);
        Assert.Equal(["c", "a", "b", "d"], ordered.Select(d => d.Id));
    }

    [Fact]
    public void WithOrder_KeepsDisconnectedDevicesAfterTheVisibleOnes()
    {
        var s = new AppSettings { DeviceOrder = ["x", "a", "b"] }.WithOrder(["b", "a"]);
        Assert.Equal(["b", "a", "x"], s.DeviceOrder);
    }

    [Fact]
    public void Normalize_WidgetOptions()
    {
        var s = (new AppSettings { WidgetPercent = "LOW", PercentBelow = 500, WidgetSize = "huge", Hotkey = " " }).Normalize();
        Assert.Equal("low", s.WidgetPercent);
        Assert.Equal(100, s.PercentBelow);
        Assert.Equal("normal", s.WidgetSize);
        Assert.Equal(AppSettings.DefaultHotkey, s.Hotkey);
    }

    // ---------- History ----------

    [Fact]
    public void History_StoresChangesAndPeriodicSamples_NotEstimates()
    {
        var h = new BatteryHistory();
        h.Record([D("a", 80), D("e", 50, estimated: true)], T0);
        h.Record([D("a", 80)], T0.AddMinutes(5));    // same level, too soon
        h.Record([D("a", 79)], T0.AddMinutes(6));    // changed
        h.Record([D("a", 79)], T0.AddMinutes(30));   // periodic sample
        Assert.Equal([80, 79, 79], h.For("a", T0.AddMinutes(30)).Select(p => p.Percent));
        Assert.Empty(h.For("e", T0));
    }

    [Fact]
    public void History_DropsReadingsOlderThanSevenDays()
    {
        var h = new BatteryHistory();
        h.Record([D("a", 90)], T0);
        h.Record([D("a", 50)], T0.AddDays(8));
        Assert.Equal([50], h.For("a", T0.AddDays(8)).Select(p => p.Percent));
    }

    [Fact]
    public void History_Segments_SplitAtGaps()
    {
        var points = new List<HistoryPoint>
        {
            new(T0.AddHours(-10), 100),
            new(T0.AddHours(-9.8), 90),
            new(T0.AddMinutes(-30), 60),   // gap of several hours → new segment
            new(T0, 50),
        };
        var segments = BatteryHistory.Segments(points, T0);
        Assert.Equal(2, segments.Count);
        Assert.Equal(1.0, segments[^1][^1].X, 3);
        Assert.Equal(0.5, segments[^1][^1].Y, 3);
    }

    [Fact]
    public void HistoryStore_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"battray-history-{Guid.NewGuid():N}.json");
        try
        {
            var store = new HistoryStore(path);
            var h = new BatteryHistory();
            h.Record([D("a", 70)], T0);
            store.Save(h.Snapshot());
            var loaded = new BatteryHistory(store.Load());
            Assert.Equal([70], loaded.For("a", T0).Select(p => p.Percent));
            store.Delete();
            Assert.False(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---------- One-click update ----------

    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string Release(string name, string url, string? digest, long size = 1000) => $$"""
        {
          "tag_name": "v1.2.0",
          "html_url": "https://github.com/koratsolutions-code/BatTray/releases/tag/v1.2.0",
          "assets": [
            { "name": "{{name}}", "browser_download_url": "{{url}}", "size": {{size}}, "digest": {{(digest is null ? "null" : $"\"{digest}\"")}} }
          ]
        }
        """;

    private const string Url = "https://github.com/koratsolutions-code/BatTray/releases/download/v1.2.0/BatTray-Setup-1.2.0.msi";

    [Fact]
    public void Parse_FindsVerifiedInstaller()
    {
        var release = UpdateChecker.Parse(Release("BatTray-Setup-1.2.0.msi", Url, "sha256:" + Hash.ToUpperInvariant()));
        Assert.NotNull(release?.Installer);
        Assert.Equal(Hash, release!.Installer!.Sha256);
        Assert.Equal(1000, release.Installer.Size);
    }

    [Theory]
    [InlineData("BatTray-Setup-1.2.0.msi", "https://evil.example/BatTray-Setup-1.2.0.msi", "sha256:" + Hash)]
    [InlineData("BatTray-Setup-1.2.0.msi", "https://github.com/someone/BatTray/releases/download/v1.2.0/BatTray-Setup-1.2.0.msi", "sha256:" + Hash)]
    [InlineData("BatTray-Setup-1.2.0.msi", Url, null)]
    [InlineData("BatTray-Setup-1.2.0.msi", Url, "md5:abc")]
    [InlineData("setup.exe", "https://github.com/koratsolutions-code/BatTray/releases/download/v1.2.0/setup.exe", "sha256:" + Hash)]
    public void Parse_RejectsInstallersThatCannotBeVerified(string name, string url, string? digest)
    {
        var release = UpdateChecker.Parse(Release(name, url, digest));
        Assert.NotNull(release);
        Assert.Null(release!.Installer);
    }

    [Fact]
    public void Parse_RejectsOversizedInstaller() =>
        Assert.Null(UpdateChecker.Parse(Release("BatTray-Setup-1.2.0.msi", Url, "sha256:" + Hash, size: 500L * 1024 * 1024))!.Installer);

    [Fact]
    public async Task Downloader_RejectsFileWithWrongHash_AndDeletesIt()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"battray-update-{Guid.NewGuid():N}");
        var content = new byte[1000];
        var handler = new FixedHandler(content);
        try
        {
            var asset = new ReleaseAsset("BatTray-Setup-1.2.0.msi", Url, content.Length, Hash);
            await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateDownloader(new HttpClient(handler)).DownloadAsync(asset, folder));
            Assert.Empty(Directory.EnumerateFiles(folder));

            var good = asset with { Sha256 = Convert.ToHexString(SHA256.HashData(content)) };
            var path = await new UpdateDownloader(new HttpClient(handler)).DownloadAsync(good, folder);
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Downloader_RefusesAddressesOutsideTheRepository()
    {
        var asset = new ReleaseAsset("BatTray-Setup-1.2.0.msi", "https://evil.example/BatTray-Setup-1.2.0.msi", 10, Hash);
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateDownloader(new HttpClient(new FixedHandler([]))).DownloadAsync(asset, Path.GetTempPath()));
    }

    private sealed class FixedHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
    }
}
