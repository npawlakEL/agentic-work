using PandA.Core.Concurrency;

namespace PandA.Core;

/// <summary>
/// Persistence port for transport orders (cartons). The eController adapter implements this over the shared
/// platform database (decision-010 adapter guide); the Sim uses an in-memory table.
/// <para>
/// Multi-host semantics (decision-010) every adapter must honor:
/// <list type="bullet">
/// <item><b>Detached reads.</b> <see cref="FindActiveByTuIdAsync"/> returns a fresh, detached copy carrying
/// the row's <see cref="TransportOrder.Version"/>; mutating it never changes the stored row.</item>
/// <item><b>Unique active TuId.</b> <see cref="InsertAsync"/> fails with
/// <see cref="DuplicateTransportOrderException"/> when an active order for the TuId already exists.</item>
/// <item><b>Optimistic concurrency.</b> <see cref="UpdateAsync"/> writes only if the stored version still equals
/// <see cref="TransportOrder.Version"/> (conditional update; affected rows == 1), then bumps the version on
/// both the row and the passed instance. A mismatch (or a missing row) throws
/// <see cref="ConcurrencyConflictException"/> and writes nothing — there is no last-writer-wins path.</item>
/// </list>
/// Writers additionally hold the <see cref="LockNames.Carton"/> lease; the version check is the backstop that
/// turns any lease violation (expiry under a stalled holder, a writer that bypassed the lease) into a detected
/// conflict instead of a silent lost update.
/// </para>
/// </summary>
public interface ITransportOrderStore
{
    /// <summary>Find the active (in-flight) transport order for a blind label as a detached copy, or null.</summary>
    ValueTask<TransportOrder?> FindActiveByTuIdAsync(string tuId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert a new transport order (its <see cref="TransportOrder.Version"/> must be 0). On success the
    /// instance's version is stamped with the stored version (1).
    /// </summary>
    /// <exception cref="DuplicateTransportOrderException">An active order for the TuId already exists.</exception>
    ValueTask InsertAsync(TransportOrder transportOrder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Conditionally update an existing order: succeeds only when the stored version equals
    /// <see cref="TransportOrder.Version"/>; on success the version is incremented on the row and the instance.
    /// </summary>
    /// <exception cref="ConcurrencyConflictException">The stored version differs or the row no longer exists.</exception>
    ValueTask UpdateAsync(TransportOrder transportOrder, CancellationToken cancellationToken = default);
}

/// <summary>Convenience helpers over <see cref="ITransportOrderStore"/>.</summary>
public static class TransportOrderStoreExtensions
{
    /// <summary>Insert a never-persisted order (version 0) or conditionally update a loaded one.</summary>
    public static ValueTask SaveAsync(
        this ITransportOrderStore store,
        TransportOrder transportOrder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(transportOrder);
        return transportOrder.Version == 0
            ? store.InsertAsync(transportOrder, cancellationToken)
            : store.UpdateAsync(transportOrder, cancellationToken);
    }

    /// <summary>
    /// Apply <paramref name="mutation"/> to <paramref name="transportOrder"/> and persist it (insert or
    /// conditional update). On a <see cref="ConcurrencyConflictException"/> the order is reloaded, the same
    /// mutation is re-applied to the fresh copy and the update is retried <b>once</b>; a second conflict
    /// propagates. Callers hold the carton lease, so a conflict means a writer bypassed it or the lease
    /// expired — re-applying the intent to the latest row is safe, last-wins on the whole row is not.
    /// </summary>
    /// <returns>The persisted instance (the reloaded copy after a retry), or null when the row vanished.</returns>
    public static async ValueTask<TransportOrder?> SaveWithRetryAsync(
        this ITransportOrderStore store,
        TransportOrder transportOrder,
        Action<TransportOrder> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(transportOrder);
        ArgumentNullException.ThrowIfNull(mutation);

        mutation(transportOrder);
        try
        {
            await store.SaveAsync(transportOrder, cancellationToken).ConfigureAwait(false);
            return transportOrder;
        }
        catch (ConcurrencyConflictException) when (transportOrder.Version > 0)
        {
            var fresh = await store.FindActiveByTuIdAsync(transportOrder.TuId, cancellationToken).ConfigureAwait(false);
            if (fresh is null)
            {
                return null;
            }

            mutation(fresh);
            await store.UpdateAsync(fresh, cancellationToken).ConfigureAwait(false);
            return fresh;
        }
    }
}
