using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BatTray.Core.Updates;

/// <summary>The installer attached to a release, with the SHA-256 GitHub computed for it.</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size, string Sha256);

/// <summary>A published release.</summary>
/// <param name="Installer">The MSI to install it with one click, or null when the release has no verifiable one.</param>
public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, ReleaseAsset? Installer = null);

/// <summary>
/// Asks GitHub for the latest BatTray release. The only request made is an anonymous GET of the public
/// "latest release" endpoint; nothing about the user or their devices is sent.
/// </summary>
public sealed partial class UpdateChecker
{
    public const string LatestReleaseApi = "https://api.github.com/repos/koratsolutions-code/BatTray/releases/latest";
    public const string ReleasesPage = "https://github.com/koratsolutions-code/BatTray/releases/latest";

    /// <summary>Installers are only ever downloaded from this repository's release files.</summary>
    public const string DownloadPrefix = "https://github.com/koratsolutions-code/BatTray/releases/download/";

    /// <summary>Largest installer accepted (the current one is about 30 MB).</summary>
    public const long MaxInstallerSize = 200L * 1024 * 1024;

    private readonly HttpClient _http;

    public UpdateChecker(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>The latest release when it is newer than <paramref name="current"/>, otherwise null.</summary>
    public async Task<ReleaseInfo?> FindNewerAsync(Version current, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("BatTray", current.ToString(3)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return Parse(json) is { } release && release.Version > Normalize(current) ? release : null;
    }

    /// <summary>
    /// Reads <c>tag_name</c>, <c>html_url</c> and the MSI asset from the GitHub API answer; drafts and
    /// pre-releases are ignored.
    /// </summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
                return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
                return null;
            if (!root.TryGetProperty("tag_name", out var tagElement) || tagElement.GetString() is not { } tag)
                return null;
            if (ParseVersion(tag) is not { } version)
                return null;

            // Only ever open pages on github.com, whatever the answer says.
            var url = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            if (url is null || !url.StartsWith("https://github.com/koratsolutions-code/BatTray/", StringComparison.Ordinal))
                url = ReleasesPage;
            return new ReleaseInfo(version, tag, url, FindInstaller(root));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The MSI asset, only when its name, address, size and SHA-256 digest are all as expected; otherwise
    /// null (the user is then sent to the release page instead).
    /// </summary>
    private static ReleaseAsset? FindInstaller(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object)
                continue;
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
            var digest = asset.TryGetProperty("digest", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : null;
            var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;

            if (name is null || !InstallerName().IsMatch(name))
                continue;
            if (url is null || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal) || !url.EndsWith("/" + name, StringComparison.Ordinal))
                continue;
            if (size <= 0 || size > MaxInstallerSize)
                continue;
            if (digest is null || Sha256Digest().Match(digest) is not { Success: true } m)
                continue;
            return new ReleaseAsset(name, url, size, m.Groups["hex"].Value.ToLowerInvariant());
        }
        return null;
    }

    /// <summary>"v1.2.0", "1.2" or "1.2.0-beta" → 1.2.0; null when it is not a version.</summary>
    public static Version? ParseVersion(string tag)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        var dash = text.IndexOfAny(['-', '+']);
        if (dash >= 0)
            text = text[..dash];
        return Version.TryParse(text, out var v) ? Normalize(v) : null;
    }

    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    [GeneratedRegex(@"^BatTray-Setup-\d+\.\d+(\.\d+)?\.msi$")]
    internal static partial Regex InstallerName();

    [GeneratedRegex("^sha256:(?<hex>[0-9a-fA-F]{64})$")]
    private static partial Regex Sha256Digest();
}
