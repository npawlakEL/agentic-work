namespace PandA.Core;

public interface ICartonRunRepository
{
    ValueTask<long> CreateRunAsync(CartonRunRecord record, CancellationToken cancellationToken = default);

    ValueTask UpdatePrinterAsync(long runId, string printerId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<CartonRunRecord>> GetRunsForOrderAsync(
        long pandaDataId,
        CancellationToken cancellationToken = default);
}
