using PandA.Core;
using PandA.Sim;

namespace PandA.Tests;

/// <summary>
/// Spy transport-order store over the contract-faithful <see cref="InMemoryTransportOrderStore"/> (detached
/// copies + version CAS) that counts persisting writes (inserts + updates) as <see cref="UpsertCount"/>, so
/// driver tests can assert persistence behaviour (senior review: PartiallyPrinted persists, NoPrinter does not).
/// </summary>
public sealed class CountingTransportOrderStore : ITransportOrderStore
{
    private readonly InMemoryTransportOrderStore _inner = new();

    /// <summary>Number of persisting writes (insert + update) performed by the code under test.</summary>
    public int UpsertCount { get; private set; }

    public ValueTask<TransportOrder?> FindActiveByTuIdAsync(string tuId, CancellationToken cancellationToken = default) =>
        _inner.FindActiveByTuIdAsync(tuId, cancellationToken);

    public async ValueTask InsertAsync(TransportOrder transportOrder, CancellationToken cancellationToken = default)
    {
        await _inner.InsertAsync(transportOrder, cancellationToken).ConfigureAwait(false);
        UpsertCount++;
    }

    public async ValueTask UpdateAsync(TransportOrder transportOrder, CancellationToken cancellationToken = default)
    {
        await _inner.UpdateAsync(transportOrder, cancellationToken).ConfigureAwait(false);
        UpsertCount++;
    }

    /// <summary>Seed an order without counting it as a write (arrange step); stamps the instance's version.</summary>
    public void Seed(TransportOrder order) => _inner.InsertAsync(order).AsTask().GetAwaiter().GetResult();
}