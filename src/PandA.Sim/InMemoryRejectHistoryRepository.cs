using PandA.Core.Verification;

namespace PandA.Sim;

public sealed class InMemoryRejectHistoryRepository : IRejectHistoryRepository
{
    private readonly Lock _gate = new();
    private readonly List<RejectRecord> _records = [];

    public IReadOnlyList<RejectRecord> Records
    {
        get
        {
            lock (_gate)
            {
                return [.. _records];
            }
        }
    }

    public ValueTask AddAsync(RejectRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _records.Add(record);
        }

        return ValueTask.CompletedTask;
    }
}
