using System.Net;
using System.Text.Json;
using BookTrak.Audnexus.Models;
using BookTrak.Audnexus.Raw;
using BookTrak.Hosting;
using BookTrak.OpenLibrary;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookTrak.Audnexus;

internal sealed class AudnexusClient(
    HttpClient httpClient,
    [FromKeyedServices(RateLimiterKeys.AudnexusApi)] PoliteRateLimiter rateLimiter,
    InFlightOperationCounter inFlightOps,
    ILogger<AudnexusClient> logger) : IAudiobookMetadataProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<NormalizedAudiobook?> GetByAsinAsync(string asin, CancellationToken cancellationToken = default)
    {
        using var lease = await rateLimiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
        using var op = inFlightOps.Track();

        var url = $"books/{Uri.EscapeDataString(asin)}";

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var raw = await JsonSerializer.DeserializeAsync<RawAudnexusBook>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return raw is null ? null : AudnexusNormalizer.Normalize(raw);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "audnexus request failed: {Url}", url);
            throw new AudnexusUnavailableException($"audnexus request failed: {url}", ex);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "audnexus returned unexpected data: {Url}", url);
            throw new AudnexusUnavailableException($"audnexus returned unexpected data: {url}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "audnexus request timed out: {Url}", url);
            throw new AudnexusUnavailableException($"audnexus request timed out: {url}", ex);
        }
    }
}
