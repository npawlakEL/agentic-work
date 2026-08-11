using PandA.Core;

namespace PandA.Sim;

/// <summary>In-memory transport-order store for the simulator/tests. Active = any stored order (Phase 1).</summary>
public sealed class InMemoryTransportOrderStore : ITransportOrderStore
{
    private readonly Dictionary<string, TransportOrder> _byTuId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<TransportOrder> All => _byTuId.Values.ToList();

    public ValueTask<TransportOrder?> FindActiveByTuIdAsync(string tuId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tuId);
        _byTuId.TryGetValue(tuId, out var order);
        return ValueTask.FromResult(order);
    }

    public ValueTask UpsertAsync(TransportOrder transportOrder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transportOrder);
        _byTuId[transportOrder.TuId] = transportOrder;
        return ValueTask.CompletedTask;
    }
}
