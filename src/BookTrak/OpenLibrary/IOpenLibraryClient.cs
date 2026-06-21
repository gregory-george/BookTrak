using BookTrak.OpenLibrary.Models;

namespace BookTrak.OpenLibrary;

/// <summary>No API key needed; in return we send a descriptive User-Agent, cache aggressively,
/// and rate-limit ourselves (see PoliteRateLimiter). Every call degrades gracefully offline —
/// callers should expect OpenLibraryUnavailableException and fall back to cached/local data.</summary>
public interface IOpenLibraryClient
{
    Task<IReadOnlyList<NormalizedSearchWork>> SearchWorksAsync(string query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NormalizedSearchAuthor>> SearchAuthorsAsync(string query, CancellationToken cancellationToken = default);

    Task<NormalizedWork?> GetWorkAsync(string workId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NormalizedEdition>> GetWorkEditionsAsync(string workId, int limit = 50, int offset = 0, CancellationToken cancellationToken = default);

    Task<NormalizedRatings?> GetWorkRatingsAsync(string workId, CancellationToken cancellationToken = default);

    Task<NormalizedAuthor?> GetAuthorAsync(string authorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NormalizedWork>> GetAuthorWorksAsync(string authorId, int limit = 50, int offset = 0, CancellationToken cancellationToken = default);

    Task<NormalizedEdition?> GetEditionByIsbnAsync(string isbn, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when Open Library can't be reached or returns an error — callers should
/// catch this and degrade to cached/local data rather than letting it propagate to the UI.</summary>
public sealed class OpenLibraryUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
