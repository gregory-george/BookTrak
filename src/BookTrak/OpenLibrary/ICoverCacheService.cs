namespace BookTrak.OpenLibrary;

/// <summary>Cache-first: checks covers/ on disk before ever touching the network. Tolerates
/// fetch failures by returning null — callers show a placeholder and can retry later.
/// covers.openlibrary.org is rate-limited separately from the main API (see PoliteRateLimiter
/// registration), which matters most during a big first CSV import.</summary>
public interface ICoverCacheService
{
    /// <summary>Returns the local file path for a book cover, fetching it on first miss.</summary>
    Task<string?> GetBookCoverPathAsync(string coverId, CoverSize size, CancellationToken cancellationToken = default);

    /// <summary>Returns the local file path for an author photo, fetching it on first miss.</summary>
    Task<string?> GetAuthorPhotoPathAsync(string authorOpenLibraryId, CoverSize size, CancellationToken cancellationToken = default);
}
