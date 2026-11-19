using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace BatTray.Core.Updates;

/// <summary>
/// Downloads a release installer and checks it before it can be run: it must come from this repository's
/// release files, have the size GitHub reported and the SHA-256 GitHub computed for it. Anything else is
/// deleted and reported as an error.
/// </summary>
public sealed class UpdateDownloader
{
    private readonly HttpClient _http;

    public UpdateDownloader(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    /// <summary>%LocalAppData%\KoratSolutions\BatTray\updates</summary>
    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KoratSolutions", "BatTray", "updates");

    /// <summary>Downloads and verifies the installer; returns the path of the verified file.</summary>
    /// <param name="progress">Fraction downloaded, 0–1.</param>
    /// <exception cref="InvalidDataException">The file is not the one GitHub described (wrong size or SHA-256).</exception>
    public async Task<string> DownloadAsync(ReleaseAsset asset, string? folder = null, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!asset.Url.StartsWith(UpdateChecker.DownloadPrefix, StringComparison.Ordinal) || !UpdateChecker.InstallerName().IsMatch(asset.Name))
            throw new InvalidDataException("The installer address is not a BatTray release file.");

        folder ??= DefaultFolder;
        Directory.CreateDirectory(folder);
        // Old downloads are not needed any more.
        foreach (var old in Directory.EnumerateFiles(folder, "BatTray-Setup-*.msi*"))
            TryDelete(old);

        var target = Path.Combine(folder, asset.Name);
        var partial = target + ".part";
        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, asset.Url))
            {
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("BatTray", "update"));
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } length && length != asset.Size)
                    throw new InvalidDataException("The installer does not have the expected size.");

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > asset.Size)
                        throw new InvalidDataException("The installer is larger than expected.");
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report((double)total / asset.Size);
                }
                if (total != asset.Size)
                    throw new InvalidDataException("The installer download is incomplete.");
            }

            if (!HashMatches(partial, asset.Sha256))
                throw new InvalidDataException("The installer's SHA-256 does not match the one published on GitHub.");

            File.Move(partial, target, overwrite: true);
            return target;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    /// <summary>True when the file's SHA-256 is <paramref name="expectedHex"/> (case-insensitive).</summary>
    public static bool HashMatches(string path, string expectedHex)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(actual, expectedHex, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
