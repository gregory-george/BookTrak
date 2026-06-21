namespace BookTrak.Hosting;

/// <summary>
/// Tracks active background operations (OL/audnexus requests, cover fetches) so shutdown can
/// wait for "zero in-flight ops" per the "finish, then exit" lifecycle rule. Registered as a
/// singleton; callers wrap each outbound operation with <see cref="Track"/>.
/// </summary>
internal sealed class InFlightOperationCounter
{
    private int _count;

    public int Count => _count;

    public IDisposable Track()
    {
        Interlocked.Increment(ref _count);
        return new Releaser(this);
    }

    public async Task WaitForIdleAsync(CancellationToken cancellationToken)
    {
        while (Volatile.Read(ref _count) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class Releaser(InFlightOperationCounter owner) : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref owner._count);
    }
}
