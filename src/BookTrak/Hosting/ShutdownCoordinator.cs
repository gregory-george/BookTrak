namespace BookTrak.Hosting;

/// <summary>
/// Pure "finish, then exit" wait logic, extracted from Program.cs so it's unit-testable without
/// a live WinForms tray or Kestrel host: poll in-flight-op and connected-circuit counts until
/// both reach zero, reporting progress after each poll, with a fallback deadline so a
/// stuck/never-disconnecting circuit can't hang shutdown forever.
/// </summary>
internal static class ShutdownCoordinator
{
    public static async Task WaitForIdleAsync(
        Func<int> getInFlightOps,
        Func<int> getConnectedCircuits,
        Action<int, int> onWaiting,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ops = getInFlightOps();
            var circuits = getConnectedCircuits();

            if (ops == 0 && circuits == 0)
            {
                return;
            }

            onWaiting(ops, circuits);
            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
