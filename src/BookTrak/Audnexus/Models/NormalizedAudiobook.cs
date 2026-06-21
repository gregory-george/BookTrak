namespace BookTrak.Audnexus.Models;

public sealed record NormalizedAudiobook(
    string Asin,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Narrators,
    string? PublisherName,
    string? ReleaseDate,
    int? RuntimeLengthMinutes,
    string? ImageUrl,
    string? Summary,
    string? SeriesName,
    string? SeriesPosition,
    string? Language);
