using BookTrak.Hosting;

namespace BookTrak.Tests;

public class IdleShutdownMonitorTests
{
    [Fact]
    public async Task WatchAsync_NeverConnected_DoesNotFireEvenAtZero()
    {
        // No tab has ever connected (e.g. still loading right after launch) — must not treat
        // that as "idle" just because the circuit count happens to read zero.
        using var cts = new CancellationTokenSource();
        var fired = false;

        var task = IdleShutdownMonitor.WatchAsync(
            getConnectedCircuits: () => 0,
            onIdleTimeout: () => fired = true,
            gracePeriod: TimeSpan.FromMilliseconds(30),
            pollInterval: TimeSpan.FromMilliseconds(5),
            cancellationToken: cts.Token);

        await Task.Delay(150);
        cts.Cancel();
        await task;

        Assert.False(fired);
    }

    [Fact]
    public async Task WatchAsync_ConnectedThenDisconnectedPastGracePeriod_Fires()
    {
        var pollCount = 0;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var fired = false;

        await IdleShutdownMonitor.WatchAsync(
            // First couple of polls see a connected tab, then it drops to zero for good.
            getConnectedCircuits: () => ++pollCount <= 2 ? 1 : 0,
            onIdleTimeout: () => fired = true,
            gracePeriod: TimeSpan.FromMilliseconds(30),
            pollInterval: TimeSpan.FromMilliseconds(5),
            cancellationToken: timeoutCts.Token);

        Assert.True(fired);
    }

    [Fact]
    public async Task WatchAsync_BriefDropToZeroDuringRefresh_DoesNotFire()
    {
        // Simulates a page refresh: circuit count blips to zero for less than the grace period
        // (OnConnectionDownAsync fires before the replacement connection comes up) then recovers.
        var pollCount = 0;
        var fired = false;
        using var cts = new CancellationTokenSource();

        var task = IdleShutdownMonitor.WatchAsync(
            getConnectedCircuits: () =>
            {
                pollCount++;
                return pollCount switch
                {
                    1 => 1,
                    2 => 0,
                    3 => 0,
                    _ => 1,
                };
            },
            onIdleTimeout: () => fired = true,
            gracePeriod: TimeSpan.FromMilliseconds(500),
            pollInterval: TimeSpan.FromMilliseconds(10),
            cancellationToken: cts.Token);

        await Task.Delay(200);
        cts.Cancel();
        await task;

        Assert.False(fired);
    }

    [Fact]
    public async Task WatchAsync_CancelledBeforeGraceElapses_NeverFires()
    {
        using var cts = new CancellationTokenSource();
        var fired = false;

        var task = IdleShutdownMonitor.WatchAsync(
            getConnectedCircuits: () => 0,
            onIdleTimeout: () => fired = true,
            gracePeriod: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(5),
            cancellationToken: cts.Token);

        await Task.Delay(50);
        cts.Cancel();
        await task;

        Assert.False(fired);
    }
}
