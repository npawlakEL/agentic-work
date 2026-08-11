using PandA.Core;

namespace PandA.Tests;

/// <summary>
/// Spy transport-order store that wraps an in-memory dictionary and counts <see cref="UpsertAsync"/> calls,
/// so driver tests can assert persistence behaviour (senior review: PartiallyPrinted upserts, NoPrinter does
/// not). The in-memory store mutates by reference and hides missing upserts, so we need the count.
/// </summary>
public sealed class CountingTransportOrderStore : ITransportOrderStore
{
    private readonly Dictionary<string, TransportOrder> _byTuId = new(StringComparer.OrdinalIgnoreCase);

    public int UpsertCount { get; private set; }

    public ValueTask<TransportOrder?> FindActiveByTuIdAsync(string tuId, CancellationToken cancellationToken = default)
    {
        _byTuId.TryGetValue(tuId, out var order);
        return ValueTask.FromResult(order);
    }

    public ValueTask UpsertAsync(TransportOrder transportOrder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transportOrder);
        UpsertCount++;
        _byTuId[transportOrder.TuId] = transportOrder;
        return ValueTask.CompletedTask;
    }

    /// <summary>Seed an order without counting it as an upsert (arrange step).</summary>
    public void Seed(TransportOrder order) => _byTuId[order.TuId] = order;
}
