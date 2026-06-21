namespace BookTrak.OpenLibrary.Models;

public sealed record NormalizedEdition(
    string? OpenLibraryEditionId,
    string? Isbn10,
    string? Isbn13,
    int? NumberOfPages,
    string? Language,
    string? Publisher,
    string? PublishDate,
    string? PrimaryCoverId,
    IReadOnlyList<string> WorkOpenLibraryIds);
