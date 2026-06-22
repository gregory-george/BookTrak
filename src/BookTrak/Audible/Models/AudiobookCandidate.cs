namespace BookTrak.Audible.Models;

/// <summary>A single audiobook hit from Audible's catalog search — just enough to rank/display
/// candidates and hand the ASIN to audnexus for the real metadata fetch.</summary>
public sealed record AudiobookCandidate(
    string Asin,
    string Title,
    string? Subtitle,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Narrators,
    string? PublisherName);
