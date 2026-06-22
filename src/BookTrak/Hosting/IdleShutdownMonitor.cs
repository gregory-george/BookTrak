namespace BookTrak.Hosting;

/// <summary>
/// Watches connected-circuit count; once at least one tab has connected and the count then stays
/// at zero for a grace period, fires a callback to trigger the same "finish, then exit" sequence
/// as tray Quit. The grace period absorbs transient zero-counts during page refresh/navigation
/// (OnConnectionDownAsync fires before the replacement connection's OnConnectionUpAsync) and the
/// "has connected" gate stops it from firing during the brief window before the first browser tab
/// finishes loading after launch.
/// </summary>
internal static class IdleShutdownMonitor
{
    public static async Task WatchAsync(
        Func<int> getConnectedCircuits,
        Action onIdleTimeout,
        TimeSpan gracePeriod,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        var hasConnected = false;
        DateTime? zeroSince = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var circuits = getConnectedCircuits();

            if (circuits > 0)
            {
                hasConnected = true;
                zeroSince = null;
            }
            else if (hasConnected)
            {
                zeroSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - zeroSince >= gracePeriod)
                {
                    onIdleTimeout();
                    return;
                }
            }

            try
            {
                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
