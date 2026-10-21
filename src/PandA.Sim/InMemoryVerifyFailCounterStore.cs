using PandA.Core.Verification;

namespace PandA.Sim;

/// <summary>
/// In-memory shared verify-fail counters (decision-010): atomic increment-with-optional-reset and reset. Share
/// one instance across simulated hosts to model the shared <c>PrinterState.VerifyFailCount</c> column.
/// </summary>
public sealed class InMemoryVerifyFailCounterStore : IVerifyFailCounterStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);

    public ValueTask<int> IncrementAsync(string key, int resetAtOrAbove = 0, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        lock (_gate)
        {
            var next = _counts.GetValueOrDefault(key) + 1;
            _counts[key] = resetAtOrAbove > 0 && next >= resetAtOrAbove ? 0 : next;
            return ValueTask.FromResult(next);
        }
    }

    public ValueTask ResetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        lock (_gate)
        {
            _counts.Remove(key);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<int> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        lock (_gate)
        {
            return ValueTask.FromResult(_counts.GetValueOrDefault(key));
        }
    }
}
