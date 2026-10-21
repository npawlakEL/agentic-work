using PandA.Core;

namespace PandA.Sim;

/// <summary>
/// In-memory carton-run table. Run ids are store-generated from one counter, so sharing a single instance
/// across simulated hosts yields ids unique across hosts (decision-010).
/// </summary>
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

    public ValueTask<long> ReserveRunIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return ValueTask.FromResult(_nextRunId++);
        }
    }

    public ValueTask<long> CreateRunAsync(CartonRunRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            long runId;
            if (record.RunId > 0)
            {
                if (record.RunId >= _nextRunId || _records.ContainsKey(record.RunId))
                {
                    throw new InvalidOperationException(string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Run id {record.RunId} was not reserved or is already used."));
                }

                runId = record.RunId;
            }
            else
            {
                runId = _nextRunId++;
            }

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

    public ValueTask<CartonRunRecord?> FindByRunIdAsync(long runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(_records.GetValueOrDefault(runId));
        }
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
