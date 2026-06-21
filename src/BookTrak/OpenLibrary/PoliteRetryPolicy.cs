using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace BookTrak.OpenLibrary;

/// <summary>Shared resilience config for both Open Library and audnexus HttpClients: retries
/// 429/503/5xx with exponential backoff + jitter, honoring the server's Retry-After header when
/// present instead of the computed backoff.</summary>
internal static class PoliteRetryPolicy
{
    public static void ConfigurePoliteRetries(HttpStandardResilienceOptions options)
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
