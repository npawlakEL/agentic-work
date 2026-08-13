using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly ILogger<VerifyStationService> _logger;

    public VerifyStationService(
        ITransportOrderStore store,
        IVerificationService verification,
        IVerifyThresholdTracker threshold,
        IClock clock,
        ILogger<VerifyStationService>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _verification = verification ?? throw new ArgumentNullException(nameof(verification));
        _threshold = threshold ?? throw new ArgumentNullException(nameof(threshold));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? NullLogger<VerifyStationService>.Instance;
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
            _logger.LogWarning("No active transport order for verify scan {TuId}.", blindLabel);
            return new VerifyStationResult(VerifyStationStatus.NoActiveOrder, Verify: null, PrinterPaused: false, 0);
        }

        var verify = _verification.Verify(order.Labels, scanned, options, xref);
        // F23: wave auto-complete here.

        // Pass and Ignore (clean bypass) let the carton proceed and clear the fail streak. A bypass that
        // read no-read/no-data comes back as a Fail outcome, so it holds the carton and counts (VF-3).
        var proceed = verify.Outcome is VerifyOutcome.Pass or VerifyOutcome.Ignore;
        var threshold = _threshold.Register(order.LineId, pass: proceed, failThreshold);

        if (proceed)
        {
            order.MarkVerified(_clock.UtcNow);
            _logger.LogInformation(
                "Verify passed for transport order {TuId} on line {LineId}; outcome {Outcome}.",
                order.TuId, order.LineId, verify.Outcome);
        }
        else
        {
            // Decision-003: no auto re-arm. Hold the carton for manual intervention.
            order.MarkVerifyFailed(_clock.UtcNow);
            // F22: reject audit emission here.
            _logger.LogWarning(
                "Verify failed for transport order {TuId} on line {LineId}; outcome {Outcome}, consecutive failures {ConsecutiveFailures}.",
                order.TuId, order.LineId, verify.Outcome, threshold.ConsecutiveFailures);
        }

        await _store.UpsertAsync(order, cancellationToken).ConfigureAwait(false);

        return new VerifyStationResult(
            proceed ? VerifyStationStatus.Verified : VerifyStationStatus.HeldForIntervention,
            verify,
            threshold.PausePrinter,
            threshold.ConsecutiveFailures);
    }
}
