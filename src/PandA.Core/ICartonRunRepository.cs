namespace PandA.Core;

public interface ICartonRunRepository
{
    ValueTask<long> CreateRunAsync(CartonRunRecord record, CancellationToken cancellationToken = default);

    ValueTask UpdatePrinterAsync(long runId, string printerId, CancellationToken cancellationToken = default);

    /// <summary>Find a single run by its <c>CartonListID</c> (RunId), or null.</summary>
    ValueTask<CartonRunRecord?> FindByRunIdAsync(long runId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<CartonRunRecord>> GetRunsForOrderAsync(
        long pandaDataId,
        CancellationToken cancellationToken = default);
}
