using BookTrak.Audible.Models;
using BookTrak.Audible.Raw;

namespace BookTrak.Audible;

/// <summary>Pure raw-DTO -&gt; candidate mapping. No I/O. Assumes the caller has already filtered
/// out products missing an Asin/Title.</summary>
internal static class AudibleNormalizer
{
    public static AudiobookCandidate Normalize(RawAudibleProduct p) => new(
        Asin: p.Asin!,
        Title: p.Title!,
        Subtitle: p.Subtitle,
        Authors: p.Authors?.Select(a => a.Name).Where(n => n is not null).Select(n => n!).ToList() ?? [],
        Narrators: p.Narrators?.Select(n => n.Name).Where(n => n is not null).Select(n => n!).ToList() ?? [],
        PublisherName: p.PublisherName);
}
