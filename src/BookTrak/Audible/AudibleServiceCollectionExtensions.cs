using BookTrak.OpenLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace BookTrak.Audible;

internal static class AudibleServiceCollectionExtensions
{
    public static IServiceCollection AddAudibleServices(this IServiceCollection services)
    {
        // Unofficial endpoint — keep concurrency at 1 and the interval conservative so discovery
        // never looks like scraping. Its own rate-limiter, separate from audnexus/OL.
        services.AddKeyedSingleton(RateLimiterKeys.AudibleApi,
            (_, _) => new PoliteRateLimiter(maxConcurrent: 1, minInterval: TimeSpan.FromMilliseconds(500)));

        // Deliberately no descriptive "BookTrak/..." User-Agent here: this is Audible's own app
        // API, not a partner-friendly service like OL/audnexus, and a bot-shaped UA raises the
        // odds of being blocked. Let HttpClient send its default.
        services.AddHttpClient<IAudiobookSearchProvider, AudibleClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.audible.com/");
            })
            .AddStandardResilienceHandler(PoliteRetryPolicy.ConfigurePoliteRetries);

        return services;
    }
}
