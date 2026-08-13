using PandA.Core;

namespace PandA.Sim;

public sealed class InMemoryCartonRunRepository : ICartonRunRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<long, CartonRunRecord> _records = [];
    private long _nextRunId = 1;

    public IReadOnlyList<CartonRunRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return [.. _records.Values.OrderBy(record => record.RunId)];
            }
        }
    }

    public ValueTask<long> CreateRunAsync(CartonRunRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var runId = _nextRunId++;
            _records.Add(runId, record with { RunId = runId });
            return ValueTask.FromResult(runId);
        }
    }

    public ValueTask UpdatePrinterAsync(
        long runId,
        string printerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(printerId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _records[runId] = _records[runId] with { AssignedPrinter = printerId };
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<CartonRunRecord>> GetRunsForOrderAsync(
        long pandaDataId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            IReadOnlyList<CartonRunRecord> records =
                [.. _records.Values
                    .Where(record => record.PandaDataId == pandaDataId)
                    .OrderBy(record => record.RunId)];

            return ValueTask.FromResult(records);
        }
    }
}
