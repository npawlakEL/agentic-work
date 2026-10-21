namespace PandA.Core.Verification;

/// <summary>
/// Shared consecutive verify-fail counters (source <c>PrinterState.VerifyFailCount</c>), keyed by printer id
/// (decision-010). Every host's verify station increments the same counter, so each operation is a single
/// atomic store operation — never a read in Core followed by a write.
/// </summary>
public interface IVerifyFailCounterStore
{
    /// <summary>
    /// Atomically increment the counter for <paramref name="key"/> and return the incremented value. When
    /// <paramref name="resetAtOrAbove"/> is &gt; 0 and the incremented value reaches it, the stored counter is
    /// set to 0 in the same atomic step (source <c>_Refresh</c> right after <c>~PP</c>); the returned value is
    /// still the incremented (tripping) count.
    /// </summary>
    ValueTask<int> IncrementAsync(string key, int resetAtOrAbove = 0, CancellationToken cancellationToken = default);

    /// <summary>Atomically set the counter for <paramref name="key"/> to 0.</summary>
    ValueTask ResetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Read the current counter (0 when absent).</summary>
    ValueTask<int> GetAsync(string key, CancellationToken cancellationToken = default);
}
