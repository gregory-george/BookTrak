using BookTrak.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookTrak.OpenLibrary;

internal sealed class CoverCacheService(
    HttpClient httpClient,
    [FromKeyedServices(RateLimiterKeys.OpenLibraryCovers)] PoliteRateLimiter rateLimiter,
    InFlightOperationCounter inFlightOps,
    ILogger<CoverCacheService> logger) : ICoverCacheService
{
    public Task<string?> GetBookCoverPathAsync(string coverId, CoverSize size, CancellationToken cancellationToken = default)
        => GetOrFetchAsync($"b/id/{coverId}", Path.Combine(AppPaths.CoversDirectory, "books"), coverId, size, cancellationToken);

    public Task<string?> GetAuthorPhotoPathAsync(string authorOpenLibraryId, CoverSize size, CancellationToken cancellationToken = default)
        => GetOrFetchAsync($"a/olid/{authorOpenLibraryId}", Path.Combine(AppPaths.CoversDirectory, "authors"), authorOpenLibraryId, size, cancellationToken);

    public async Task<string?> GetExternalCoverPathAsync(string url, string cacheKey, CancellationToken cancellationToken = default)
    {
        var localDir = Path.Combine(AppPaths.CoversDirectory, "books");
        var localPath = Path.Combine(localDir, $"{cacheKey}.jpg");

        if (File.Exists(localPath))
        {
            return localPath;
        }

        using var op = inFlightOps.Track();

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            Directory.CreateDirectory(localDir);
            var tempPath = localPath + ".tmp";

            await using (var fileStream = File.Create(tempPath))
            {
                await response.Content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, localPath, overwrite: true);
            return localPath;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            logger.LogWarning(ex, "External cover fetch failed for {CacheKey} ({Url}) — will retry on next request", cacheKey, url);
            return null;
        }
    }

    private async Task<string?> GetOrFetchAsync(string remotePathPrefix, string localDir, string id, CoverSize size, CancellationToken cancellationToken)
    {
        var sizeChar = ToSizeChar(size);
        var localPath = Path.Combine(localDir, $"{id}-{sizeChar}.jpg");

        if (File.Exists(localPath))
        {
            return localPath;
        }

        using var lease = await rateLimiter.AcquireAsync(cancellationToken).ConfigureAwait(false);

        // Another in-flight request may have written it while we waited on the gate.
        if (File.Exists(localPath))
        {
            return localPath;
        }

        using var op = inFlightOps.Track();

        try
        {
            using var response = await httpClient.GetAsync($"{remotePathPrefix}-{sizeChar}.jpg", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            Directory.CreateDirectory(localDir);
            var tempPath = localPath + ".tmp";

            await using (var fileStream = File.Create(tempPath))
            {
                await response.Content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, localPath, overwrite: true);
            return localPath;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Cover fetch failed for {Id} — will retry on next request", id);
            return null;
        }
    }

    private static char ToSizeChar(CoverSize size) => size switch
    {
        CoverSize.Small => 'S',
        CoverSize.Medium => 'M',
        CoverSize.Large => 'L',
        _ => 'M',
    };
}
