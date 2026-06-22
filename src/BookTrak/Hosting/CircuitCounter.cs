using System.Collections.Concurrent;

namespace BookTrak.Hosting;

/// <summary>
/// Tracks connected Blazor Server circuits (browser tabs with a live SignalR connection) so
/// shutdown can wait for "zero connected UI circuits" per the "finish, then exit" lifecycle
/// rule. Keyed by circuit id and idempotent — both OnConnectionDownAsync and OnCircuitClosedAsync
/// can fire for the same circuit, and removing twice is a harmless no-op.
/// </summary>
internal sealed class CircuitCounter
{
    private readonly ConcurrentDictionary<string, byte> _connected = new();

    public int Count => _connected.Count;

    public void MarkConnected(string circuitId) => _connected[circuitId] = 0;

    public void MarkDisconnected(string circuitId) => _connected.TryRemove(circuitId, out _);
}
