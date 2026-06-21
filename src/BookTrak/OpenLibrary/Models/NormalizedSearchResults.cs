namespace BookTrak.OpenLibrary.Models;

public sealed record NormalizedSearchWork(
    string? OpenLibraryWorkId,
    string Title,
    IReadOnlyList<string> AuthorNames,
    int? FirstPublishYear,
    string? PrimaryCoverId,
    double? RatingsAverage,
    int? RatingsCount);

public sealed record NormalizedSearchAuthor(
    string? OpenLibraryId,
    string Name,
    string? BirthDate,
    int? WorkCount);
