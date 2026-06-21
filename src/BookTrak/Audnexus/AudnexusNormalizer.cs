using BookTrak.Audnexus.Models;
using BookTrak.Audnexus.Raw;

namespace BookTrak.Audnexus;

internal static class AudnexusNormalizer
{
    public static NormalizedAudiobook Normalize(RawAudnexusBook raw) => new(
        Asin: raw.Asin ?? string.Empty,
        Title: raw.Title ?? string.Empty,
        Subtitle: raw.Subtitle,
        Authors: raw.Authors?.Select(a => a.Name).Where(n => n is not null).Select(n => n!).ToList() ?? [],
        Narrators: raw.Narrators?.Select(n => n.Name).Where(n => n is not null).Select(n => n!).ToList() ?? [],
        PublisherName: raw.PublisherName,
        ReleaseDate: raw.ReleaseDate,
        RuntimeLengthMinutes: raw.RuntimeLengthMin,
        ImageUrl: raw.Image,
        Summary: raw.Summary,
        SeriesName: raw.SeriesPrimary?.Name,
        SeriesPosition: raw.SeriesPrimary?.Position,
        Language: raw.Language);
}
