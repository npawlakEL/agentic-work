namespace PandA.Core;

/// <summary>
/// Carton print runs (source <c>CartonList</c>). Multi-host (decision-010): run ids (<c>CartonListID</c>) are
/// <b>store-generated and unique across every host</b> — they are sent to the PLC and come back in PLC events,
/// so they must never be minted from a per-process counter.
/// </summary>
public interface ICartonRunRepository
{
    /// <summary>
    /// Reserve a store-generated run id (unique across all hosts) without creating the run yet. Used when the
    /// id must be known before the run row exists (F10 exception carton id = run id).
    /// </summary>
    ValueTask<long> ReserveRunIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a run and return its id. When <see cref="CartonRunRecord.RunId"/> is &gt; 0 it must be an id
    /// previously obtained from <see cref="ReserveRunIdAsync"/> and is used as-is; otherwise the store
    /// generates a new unique id.
    /// </summary>
    ValueTask<long> CreateRunAsync(CartonRunRecord record, CancellationToken cancellationToken = default);

    ValueTask UpdatePrinterAsync(long runId, string printerId, CancellationToken cancellationToken = default);

    /// <summary>Find a single run by its <c>CartonListID</c> (RunId), or null.</summary>
    ValueTask<CartonRunRecord?> FindByRunIdAsync(long runId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<CartonRunRecord>> GetRunsForOrderAsync(
        long pandaDataId,
        CancellationToken cancellationToken = default);
}
