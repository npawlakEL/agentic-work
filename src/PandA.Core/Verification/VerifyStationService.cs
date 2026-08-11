namespace PandA.Core.Verification;

/// <summary>
/// MP286 — verify scan orchestration (the verify-station analogue of <see cref="IInductService"/>).
/// Looks up the active transport order for a scanned blind label, verifies the read labels against its
/// advised set, updates the line's consecutive-fail counter, and advances the carton lifecycle:
/// pass/bypass → verified; fail → re-armed for reprint. Port glue over <c>sdisp_TOOL_PA_VerifyLabel</c>
/// + <c>sdisp_PA_VerifyThreshold_Update</c> (architecture-log 006).
/// </summary>
public interface IVerifyStationService
{
    ValueTask<VerifyStationResult> VerifyAsync(
        string blindLabel,
        IReadOnlyList<ScannedLabel> scanned,
        VerifyOptions options,
        int failThreshold,
        IReadOnlyList<LabelXref>? xref = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IVerifyStationService" />
public sealed class VerifyStationService : IVerifyStationService
{
    private readonly ITransportOrderStore _store;
    private readonly IVerificationService _verification;
    private readonly IVerifyThresholdTracker _threshold;
    private readonly IClock _clock;

    public VerifyStationService(
        ITransportOrderStore store,
        IVerificationService verification,
        IVerifyThresholdTracker threshold,
        IClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _verification = verification ?? throw new ArgumentNullException(nameof(verification));
        _threshold = threshold ?? throw new ArgumentNullException(nameof(threshold));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async ValueTask<VerifyStationResult> VerifyAsync(
        string blindLabel,
        IReadOnlyList<ScannedLabel> scanned,
        VerifyOptions options,
        int failThreshold,
        IReadOnlyList<LabelXref>? xref = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blindLabel);
        ArgumentNullException.ThrowIfNull(scanned);
        ArgumentNullException.ThrowIfNull(options);

        var order = await _store.FindActiveByTuIdAsync(blindLabel, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return new VerifyStationResult(VerifyStationStatus.NoActiveOrder, Verify: null, PrinterPaused: false, 0);
        }

        var verify = _verification.Verify(order.Labels, scanned, options, xref);

        // Pass and Ignore (bypass) both let the carton proceed and clear the fail streak.
        var proceed = verify.Outcome is VerifyOutcome.Pass or VerifyOutcome.Ignore;
        var threshold = _threshold.Register(order.LineId, pass: proceed, failThreshold);

        if (proceed)
        {
            order.MarkVerified(_clock.UtcNow);
        }
        else
        {
            // Decision-003: no auto re-arm. Hold the carton for manual intervention.
            order.MarkVerifyFailed(_clock.UtcNow);
        }

        await _store.UpsertAsync(order, cancellationToken).ConfigureAwait(false);

        return new VerifyStationResult(
            proceed ? VerifyStationStatus.Verified : VerifyStationStatus.HeldForIntervention,
            verify,
            threshold.PausePrinter,
            threshold.ConsecutiveFailures);
    }
}
