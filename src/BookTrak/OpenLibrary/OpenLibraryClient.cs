using System.Net;
using System.Text.Json;
using BookTrak.Hosting;
using BookTrak.OpenLibrary.Models;
using BookTrak.OpenLibrary.Raw;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookTrak.OpenLibrary;

internal sealed class OpenLibraryClient(
    HttpClient httpClient,
    [FromKeyedServices(RateLimiterKeys.OpenLibraryApi)] PoliteRateLimiter rateLimiter,
    InFlightOperationCounter inFlightOps,
    ILogger<OpenLibraryClient> logger) : IOpenLibraryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<NormalizedSearchWork>> SearchWorksAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"search.json?q={Uri.EscapeDataString(query)}&fields=key,title,author_name,first_publish_year,cover_i,ratings_average,ratings_count";
        var raw = await GetAsync<RawSearchWorksResponse>(url, cancellationToken).ConfigureAwait(false);
        return raw?.Docs?.Select(OpenLibraryNormalizer.NormalizeSearchWork).ToList() ?? [];
    }

    public async Task<IReadOnlyList<NormalizedSearchAuthor>> SearchAuthorsAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"search/authors.json?q={Uri.EscapeDataString(query)}";
        var raw = await GetAsync<RawSearchAuthorsResponse>(url, cancellationToken).ConfigureAwait(false);
        return raw?.Docs?.Select(OpenLibraryNormalizer.NormalizeSearchAuthor).ToList() ?? [];
    }

    public async Task<NormalizedWork?> GetWorkAsync(string workId, CancellationToken cancellationToken = default)
    {
        var raw = await GetAsync<RawWork>($"works/{workId}.json", cancellationToken).ConfigureAwait(false);
        return raw is null ? null : OpenLibraryNormalizer.NormalizeWork(raw);
    }

    public async Task<IReadOnlyList<NormalizedEdition>> GetWorkEditionsAsync(string workId, int limit = 50, int offset = 0, CancellationToken cancellationToken = default)
    {
        var url = $"works/{workId}/editions.json?limit={limit}&offset={offset}";
        var raw = await GetAsync<RawEditionsResponse>(url, cancellationToken).ConfigureAwait(false);
        return raw?.Entries?.Select(OpenLibraryNormalizer.NormalizeEdition).ToList() ?? [];
    }

    public async Task<NormalizedRatings?> GetWorkRatingsAsync(string workId, CancellationToken cancellationToken = default)
    {
        var raw = await GetAsync<RawRatings>($"works/{workId}/ratings.json", cancellationToken).ConfigureAwait(false);
        return raw is null ? null : OpenLibraryNormalizer.NormalizeRatings(raw);
    }

    public async Task<NormalizedAuthor?> GetAuthorAsync(string authorId, CancellationToken cancellationToken = default)
    {
        var raw = await GetAsync<RawAuthor>($"authors/{authorId}.json", cancellationToken).ConfigureAwait(false);
        return raw is null ? null : OpenLibraryNormalizer.NormalizeAuthor(raw);
    }

    public async Task<IReadOnlyList<NormalizedWork>> GetAuthorWorksAsync(string authorId, int limit = 50, int offset = 0, CancellationToken cancellationToken = default)
    {
        var url = $"authors/{authorId}/works.json?limit={limit}&offset={offset}";
        var raw = await GetAsync<RawAuthorWorksResponse>(url, cancellationToken).ConfigureAwait(false);
        return raw?.Entries?.Select(OpenLibraryNormalizer.NormalizeWork).ToList() ?? [];
    }

    public async Task<NormalizedEdition?> GetEditionByIsbnAsync(string isbn, CancellationToken cancellationToken = default)
    {
        var raw = await GetAsync<RawIsbnEdition>($"isbn/{Uri.EscapeDataString(isbn)}.json", cancellationToken).ConfigureAwait(false);
        return raw is null ? null : OpenLibraryNormalizer.NormalizeEdition(raw);
    }

    private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken) where T : class
    {
        using var lease = await rateLimiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
        using var op = inFlightOps.Track();

        try
        {
            using var response = await httpClient.GetAsync(relativeUrl, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Open Library request failed: {Url}", relativeUrl);
            throw new OpenLibraryUnavailableException($"Open Library request failed: {relativeUrl}", ex);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Open Library returned unexpected data: {Url}", relativeUrl);
            throw new OpenLibraryUnavailableException($"Open Library returned unexpected data: {relativeUrl}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Open Library request timed out: {Url}", relativeUrl);
            throw new OpenLibraryUnavailableException($"Open Library request timed out: {relativeUrl}", ex);
        }
    }
}

internal static class RateLimiterKeys
{
    public const string OpenLibraryApi = "ol-api";
    public const string OpenLibraryCovers = "ol-covers";
    public const string AudnexusApi = "audnexus-api";
    public const string AudibleApi = "audible-api";
}
