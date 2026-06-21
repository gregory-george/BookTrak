namespace BookTrak.OpenLibrary;

/// <summary>
/// A small client-side "be polite" gate: caps concurrent outbound requests and enforces a
/// minimum interval between dispatches. covers.openlibrary.org is rate-limited separately from
/// the main API, so it gets its own instance with stricter limits — see DI registration.
/// </summary>
internal sealed class PoliteRateLimiter(int maxConcurrent, TimeSpan minInterval)
{
    private readonly SemaphoreSlim _concurrency = new(maxConcurrent, maxConcurrent);
    private readonly Lock _gate = new();
    private DateTime _nextAllowedUtc = DateTime.MinValue;

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);

        TimeSpan delay;
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            delay = _nextAllowedUtc > now ? _nextAllowedUtc - now : TimeSpan.Zero;
            _nextAllowedUtc = (delay > TimeSpan.Zero ? _nextAllowedUtc : now) + minInterval;
        }

        if (delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _concurrency.Release();
                throw;
            }
        }

        return new Releaser(_concurrency);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            semaphore.Release();
        }
    }
}
