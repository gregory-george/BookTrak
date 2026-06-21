using Microsoft.Extensions.DependencyInjection;

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
            .AddStandardResilienceHandler(PoliteRetryPolicy.ConfigurePoliteRetries);

        services.AddHttpClient<ICoverCacheService, CoverCacheService>(client =>
            {
                client.BaseAddress = new Uri("https://covers.openlibrary.org/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            })
            .AddStandardResilienceHandler(PoliteRetryPolicy.ConfigurePoliteRetries);

        return services;
    }
}
