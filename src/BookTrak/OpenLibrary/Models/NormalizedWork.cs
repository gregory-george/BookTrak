namespace BookTrak.OpenLibrary.Models;

public sealed record NormalizedWork(
    string? OpenLibraryWorkId,
    string Title,
    string? Subtitle,
    string? Description,
    string? FirstPublishDate,
    IReadOnlyList<string> Subjects,
    IReadOnlyList<string> AuthorOpenLibraryIds,
    string? PrimaryCoverId);
