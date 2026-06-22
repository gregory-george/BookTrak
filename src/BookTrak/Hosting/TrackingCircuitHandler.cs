using Microsoft.AspNetCore.Components.Server.Circuits;

namespace BookTrak.Hosting;

/// <summary>Registered as a scoped CircuitHandler (one instance per circuit) so the singleton
/// CircuitCounter reflects connected/disconnected transitions for every open browser tab.</summary>
internal sealed class TrackingCircuitHandler(CircuitCounter counter) : CircuitHandler
{
    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        counter.MarkConnected(circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        counter.MarkDisconnected(circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        counter.MarkDisconnected(circuit.Id);
        return Task.CompletedTask;
    }
}
