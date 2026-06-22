using BookTrak.Audible.Models;

namespace BookTrak.Audible;

/// <summary>Discovery-only: turns a title + author into candidate Audible ASINs via Audible's
/// unofficial catalog-search endpoint. This is the one place BookTrak talks to Audible directly
/// (a documented carve-out from the "never scrape Audible" rule — see CLAUDE.md); the actual
/// audiobook metadata is still fetched through audnexus by ASIN. Always allow manual ASIN entry
/// as a fallback, since this endpoint is undocumented and may break or rate-limit.</summary>
public interface IAudiobookSearchProvider
{
    Task<IReadOnlyList<AudiobookCandidate>> SearchAsync(
        string title,
        string author,
        int maxResults = 10,
        CancellationToken cancellationToken = default);
}

/// <summary>Thrown when Audible's catalog search can't be reached or returns garbage — callers
/// should catch this and fall back to manual ASIN entry rather than surfacing a raw error.</summary>
public sealed class AudibleUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
