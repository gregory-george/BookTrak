using System.Net;
using System.Text.Json;
using BookTrak.Audible.Models;
using BookTrak.Audible.Raw;
using BookTrak.Hosting;
using BookTrak.OpenLibrary;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookTrak.Audible;

internal sealed class AudibleClient(
    HttpClient httpClient,
    [FromKeyedServices(RateLimiterKeys.AudibleApi)] PoliteRateLimiter rateLimiter,
    InFlightOperationCounter inFlightOps,
    ILogger<AudibleClient> logger) : IAudiobookSearchProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AudiobookCandidate>> SearchAsync(
        string title,
        string author,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        var keywords = string.Join(' ', new[] { title, author }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (keywords.Length == 0)
        {
            return [];
        }

        using var lease = await rateLimiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
        using var op = inFlightOps.Track();

        // response_groups=product_desc,contributors gives us title/subtitle/authors/narrators —
        // enough to rank candidates without a second round-trip. us region only (see CLAUDE.md).
        var url = $"1.0/catalog/products?keywords={Uri.EscapeDataString(keywords)}" +
                  $"&num_results={maxResults}&products_sort_by=Relevance" +
                  "&response_groups=product_desc,contributors";

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return [];
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var raw = await JsonSerializer.DeserializeAsync<RawAudibleSearchResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);

            if (raw?.Products is not { Count: > 0 } products)
            {
                return [];
            }

            return products
                .Where(p => !string.IsNullOrWhiteSpace(p.Asin) && !string.IsNullOrWhiteSpace(p.Title))
                .Select(AudibleNormalizer.Normalize)
                .ToList();
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Audible search request failed: {Url}", url);
            throw new AudibleUnavailableException($"Audible search request failed: {url}", ex);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Audible search returned unexpected data: {Url}", url);
            throw new AudibleUnavailableException($"Audible search returned unexpected data: {url}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Audible search request timed out: {Url}", url);
            throw new AudibleUnavailableException($"Audible search request timed out: {url}", ex);
        }
    }
}
