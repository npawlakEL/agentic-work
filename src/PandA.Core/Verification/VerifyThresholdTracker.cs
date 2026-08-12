using System.Collections.Concurrent;

namespace PandA.Core.Verification;

/// <summary>
/// In-memory <see cref="IVerifyThresholdTracker"/>. A pass resets a line's streak to zero; a fail
/// increments it and signals a pause once the count reaches the threshold. Thread-safe per line.
/// <para>
/// <paramref name="resetOnTrip"/> selects the post-trip policy (VF-4, configurable): when <c>true</c>
/// the counter is refreshed to 0 the moment it trips the pause (source behavior — after an operator
/// un-pauses it takes a fresh full streak to pause again); when <c>false</c> (default) the counter stays
/// at/above the threshold so every subsequent fail re-signals the pause until an explicit <see cref="Reset"/>.
/// </para>
/// </summary>
public sealed class VerifyThresholdTracker(bool resetOnTrip = false) : IVerifyThresholdTracker
{
    private readonly ConcurrentDictionary<string, int> _counts =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly bool _resetOnTrip = resetOnTrip;

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
        if (pause && _resetOnTrip)
        {
            // Refresh the streak to 0 on trip so the next pause needs another full window (source _Refresh).
            _counts[lineId] = 0;
        }

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
