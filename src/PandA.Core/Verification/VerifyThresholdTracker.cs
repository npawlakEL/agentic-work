using System.Collections.Concurrent;

namespace PandA.Core.Verification;

/// <summary>
/// In-memory <see cref="IVerifyThresholdTracker"/>. A pass resets a line's streak to zero; a fail
/// increments it and signals a pause once the count reaches the threshold. Thread-safe per line.
/// </summary>
public sealed class VerifyThresholdTracker : IVerifyThresholdTracker
{
    private readonly ConcurrentDictionary<string, int> _counts =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public VerifyThresholdResult Register(string lineId, bool pass, int failThreshold)
    {
        ArgumentException.ThrowIfNullOrEmpty(lineId);

        if (pass)
        {
            _counts[lineId] = 0;
            return new VerifyThresholdResult(0, PausePrinter: false);
        }

        var count = _counts.AddOrUpdate(lineId, 1, static (_, current) => current + 1);
        var pause = failThreshold > 0 && count >= failThreshold;
        return new VerifyThresholdResult(count, pause);
    }

    /// <inheritdoc />
    public int CurrentCount(string lineId)
    {
        ArgumentException.ThrowIfNullOrEmpty(lineId);
        return _counts.TryGetValue(lineId, out var count) ? count : 0;
    }

    /// <inheritdoc />
    public void Reset(string lineId)
    {
        ArgumentException.ThrowIfNullOrEmpty(lineId);
        _counts[lineId] = 0;
    }
}
