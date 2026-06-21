using BookTrak.Audnexus.Models;

namespace BookTrak.Audnexus;

/// <summary>audnexus derives its catalog from Audible and can rate-limit, lag, or go down — keep
/// it behind this interface, cache responses where the caller persists them, and always allow
/// manual entry so audiobooks work even when it's unavailable.</summary>
public interface IAudiobookMetadataProvider
{
    Task<NormalizedAudiobook?> GetByAsinAsync(string asin, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when audnexus can't be reached or returns an error — callers should catch this
/// and fall back to manual entry rather than letting it propagate to the UI.</summary>
public sealed class AudnexusUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
