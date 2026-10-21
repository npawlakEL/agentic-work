namespace PandA.Core.Verification;

/// <summary>
/// <see cref="IVerifyThresholdTracker"/> policy over the shared <see cref="IVerifyFailCounterStore"/>. A pass
/// resets a key's (printer's) streak to zero; a fail atomically increments it and signals a pause once the
/// count reaches the threshold.
/// <para>
/// <c>resetOnTrip</c> selects the post-trip policy (VF-4, configurable): when <c>true</c> (default — source
/// behavior, <c>sdisp_PA_VerifyThreshold_Update</c> calls <c>_Refresh</c> right after the <c>~PP</c>; owner
/// ruling 2026-10-21) the counter is refreshed to 0 in the same atomic store step that trips the pause, so after
/// an operator un-pauses it takes a fresh full streak to pause again; when <c>false</c> the counter stays
/// at/above the threshold so every subsequent fail re-signals the pause until an explicit reset.
/// </para>
/// </summary>
public sealed class VerifyThresholdTracker : IVerifyThresholdTracker
{
    private readonly IVerifyFailCounterStore _counters;
    private readonly bool _resetOnTrip;

    public VerifyThresholdTracker(IVerifyFailCounterStore counters, bool resetOnTrip = true)
    {
        _counters = counters ?? throw new ArgumentNullException(nameof(counters));
        _resetOnTrip = resetOnTrip;
    }

    /// <inheritdoc />
    public async ValueTask<VerifyThresholdResult> RegisterAsync(
        string key,
        bool pass,
        int failThreshold,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (pass)
        {
            await _counters.ResetAsync(key, cancellationToken).ConfigureAwait(false);
            return new VerifyThresholdResult(0, PausePrinter: false);
        }

        var resetAt = _resetOnTrip && failThreshold > 0 ? failThreshold : 0;
        var count = await _counters.IncrementAsync(key, resetAt, cancellationToken).ConfigureAwait(false);
        var pause = failThreshold > 0 && count >= failThreshold;
        return new VerifyThresholdResult(count, pause);
    }

    /// <inheritdoc />
    public ValueTask<int> CurrentCountAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _counters.GetAsync(key, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask ResetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _counters.ResetAsync(key, cancellationToken);
    }
}
