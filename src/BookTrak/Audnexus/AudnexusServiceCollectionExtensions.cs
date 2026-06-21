using BookTrak.OpenLibrary;
using Microsoft.Extensions.DependencyInjection;

namespace BookTrak.Audnexus;

internal static class AudnexusServiceCollectionExtensions
{
    public static IServiceCollection AddAudnexusServices(this IServiceCollection services, string contactInfo)
    {
        var userAgent = UserAgent.Build(contactInfo);

        services.AddKeyedSingleton(RateLimiterKeys.AudnexusApi,
            (_, _) => new PoliteRateLimiter(maxConcurrent: 2, minInterval: TimeSpan.FromMilliseconds(250)));

        services.AddHttpClient<IAudiobookMetadataProvider, AudnexusClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.audnex.us/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            })
            .AddStandardResilienceHandler(PoliteRetryPolicy.ConfigurePoliteRetries);

        return services;
    }
}
