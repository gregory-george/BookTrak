using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace BookTrak.OpenLibrary;

internal static class OpenLibraryServiceCollectionExtensions
{
    public static IServiceCollection AddOpenLibraryServices(this IServiceCollection services, string contactInfo)
    {
        var userAgent = UserAgent.Build(contactInfo);

        // covers.openlibrary.org is rate-limited separately from the main API — its own gate
        // keeps a big first import from tripping a block on either surface independently.
        services.AddKeyedSingleton(RateLimiterKeys.OpenLibraryApi,
            (_, _) => new PoliteRateLimiter(maxConcurrent: 2, minInterval: TimeSpan.FromMilliseconds(250)));
        services.AddKeyedSingleton(RateLimiterKeys.OpenLibraryCovers,
            (_, _) => new PoliteRateLimiter(maxConcurrent: 1, minInterval: TimeSpan.FromMilliseconds(500)));

        services.AddHttpClient<IOpenLibraryClient, OpenLibraryClient>(client =>
            {
                client.BaseAddress = new Uri("https://openlibrary.org/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            })
            .AddStandardResilienceHandler(ConfigurePoliteRetries);

        services.AddHttpClient<ICoverCacheService, CoverCacheService>(client =>
            {
                client.BaseAddress = new Uri("https://covers.openlibrary.org/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            })
            .AddStandardResilienceHandler(ConfigurePoliteRetries);

        return services;
    }

    /// <summary>Retries 429/503/5xx with exponential backoff + jitter, honoring the
    /// server's Retry-After header when present (audnexus and, occasionally, Open Library
    /// send one) instead of the computed backoff.</summary>
    private static void ConfigurePoliteRetries(HttpStandardResilienceOptions options)
    {
        options.Retry.MaxRetryAttempts = 4;
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        options.Retry.Delay = TimeSpan.FromSeconds(1);

        options.Retry.ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Exception is HttpRequestException ||
            (args.Outcome.Result is { } response && IsRetryableStatus(response.StatusCode)));

        options.Retry.DelayGenerator = args =>
        {
            var retryAfter = args.Outcome.Result?.Headers.RetryAfter;
            if (retryAfter?.Delta is { } delta)
            {
                return ValueTask.FromResult<TimeSpan?>(delta);
            }

            if (retryAfter?.Date is { } date)
            {
                return ValueTask.FromResult<TimeSpan?>(date - DateTimeOffset.UtcNow);
            }

            return ValueTask.FromResult<TimeSpan?>(null);
        };
    }

    private static bool IsRetryableStatus(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests ||
        status == HttpStatusCode.ServiceUnavailable ||
        (int)status >= 500;
}
