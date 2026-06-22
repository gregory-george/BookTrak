using BookTrak.Hosting;

namespace BookTrak.Tests;

public class ShutdownCoordinatorTests
{
    [Fact]
    public async Task WaitForIdleAsync_AlreadyIdle_ReturnsImmediatelyWithoutWaiting()
    {
        var waitingCalls = 0;

        await ShutdownCoordinator.WaitForIdleAsync(
            getInFlightOps: () => 0,
            getConnectedCircuits: () => 0,
            onWaiting: (_, _) => waitingCalls++,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(10));

        Assert.Equal(0, waitingCalls);
    }

    [Fact]
    public async Task WaitForIdleAsync_InFlightOpsThenZero_WaitsUntilBothCountersClear()
    {
        // Simulates one download finishing after a couple of polls and one browser tab
        // disconnecting after a couple more — the wait must not return until BOTH hit zero.
        var ops = 2;
        var circuits = 3;
        var opsPolls = 0;
        var circuitPolls = 0;
        var reportedDuringWait = new List<(int Ops, int Circuits)>();

        await ShutdownCoordinator.WaitForIdleAsync(
            getInFlightOps: () =>
            {
                opsPolls++;
                if (opsPolls >= 2)
                {
                    ops = 0;
                }
                return ops;
            },
            getConnectedCircuits: () =>
            {
                circuitPolls++;
                if (circuitPolls >= 4)
                {
                    circuits = 0;
                }
                return circuits;
            },
            onWaiting: (o, c) => reportedDuringWait.Add((o, c)),
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(5));

        // Every reported snapshot while waiting must have at least one non-zero counter —
        // otherwise the loop should have already exited.
        Assert.All(reportedDuringWait, snapshot => Assert.True(snapshot.Ops > 0 || snapshot.Circuits > 0));
        Assert.True(reportedDuringWait.Count >= 3);
    }

    [Fact]
    public async Task WaitForIdleAsync_NeverIdle_StopsAtTimeoutInsteadOfHangingForever()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await ShutdownCoordinator.WaitForIdleAsync(
            getInFlightOps: () => 0,
            getConnectedCircuits: () => 1, // a circuit that never disconnects
            onWaiting: (_, _) => { },
            timeout: TimeSpan.FromMilliseconds(100),
            pollInterval: TimeSpan.FromMilliseconds(20));

        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 1000, "Should give up at the timeout, not hang indefinitely.");
    }

    [Fact]
    public void CircuitCounter_ConnectThenDisconnect_CountReflectsConnectedCircuits()
    {
        var counter = new CircuitCounter();
        Assert.Equal(0, counter.Count);

        counter.MarkConnected("circuit-1");
        counter.MarkConnected("circuit-2");
        Assert.Equal(2, counter.Count);

        counter.MarkDisconnected("circuit-1");
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public void CircuitCounter_DisconnectFiresTwice_IsIdempotent()
    {
        // Regression guard: both OnConnectionDownAsync and OnCircuitClosedAsync call
        // MarkDisconnected for the same circuit — the second call must not go negative.
        var counter = new CircuitCounter();
        counter.MarkConnected("circuit-1");

        counter.MarkDisconnected("circuit-1");
        counter.MarkDisconnected("circuit-1");

        Assert.Equal(0, counter.Count);
    }
}
