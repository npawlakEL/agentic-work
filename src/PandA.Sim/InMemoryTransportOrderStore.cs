using PandA.Core;
using PandA.Core.Concurrency;

namespace PandA.Sim;

/// <summary>
/// In-memory transport-order table for the simulator/tests. Active = any stored order (Phase 1).
/// <para>
/// Faithfully enforces the multi-host <see cref="ITransportOrderStore"/> contract (decision-010): rows are
/// stored and handed out as <b>detached copies</b> (a caller can never mutate the stored row through a
/// reference), inserts enforce a unique active TuId, and updates are a compare-and-swap on
/// <see cref="TransportOrder.Version"/>. Share one instance across simulated hosts to model the shared database.
/// </para>
/// </summary>
public sealed class InMemoryTransportOrderStore : ITransportOrderStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, TransportOrder> _byTuId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Detached copies of every stored order.</summary>
    public IReadOnlyCollection<TransportOrder> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _byTuId.Values.Select(o => o.Clone())];
            }
        }
    }

    public ValueTask<TransportOrder?> FindActiveByTuIdAsync(string tuId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tuId);
        lock (_gate)
        {
            return ValueTask.FromResult(_byTuId.TryGetValue(tuId, out var order) ? order.Clone() : null);
        }
    }

    public ValueTask InsertAsync(TransportOrder transportOrder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transportOrder);
        if (transportOrder.Version != 0)
        {
            throw new ArgumentException("Only a never-persisted order (Version 0) can be inserted.", nameof(transportOrder));
        }

        lock (_gate)
        {
            if (_byTuId.ContainsKey(transportOrder.TuId))
            {
                throw new DuplicateTransportOrderException(transportOrder.TuId);
            }

            var stored = transportOrder.Clone();
            stored.StampVersion(1);
            _byTuId.Add(transportOrder.TuId, stored);
            transportOrder.StampVersion(1);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(TransportOrder transportOrder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transportOrder);
        lock (_gate)
        {
            if (!_byTuId.TryGetValue(transportOrder.TuId, out var current))
            {
                throw new ConcurrencyConflictException(transportOrder.TuId, transportOrder.Version, actualVersion: null);
            }

            if (current.Version != transportOrder.Version)
            {
                throw new ConcurrencyConflictException(transportOrder.TuId, transportOrder.Version, current.Version);
            }

            var next = transportOrder.Version + 1;
            var stored = transportOrder.Clone();
            stored.StampVersion(next);
            _byTuId[transportOrder.TuId] = stored;
            transportOrder.StampVersion(next);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> DeleteAsync(string tuId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Remove(tuId));
    }

    public ValueTask<IReadOnlyList<string>> FindTuIdsByWaveAsync(string waveId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(waveId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<string> tuIds = [.. _byTuId.Values
                .Where(o => string.Equals(o.WaveId, waveId, StringComparison.OrdinalIgnoreCase))
                .Select(o => o.TuId)];
            return ValueTask.FromResult(tuIds);
        }
    }

    /// <summary>Drop the order for <paramref name="tuId"/> (e.g. once its carton has left the simulated line).</summary>
    public bool Remove(string tuId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tuId);
        lock (_gate)
        {
            return _byTuId.Remove(tuId);
        }
    }
}
