using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PandA.Core.Concurrency;
using PandA.Core.Settings;

namespace PandA.Core.Verification;

/// <summary>
/// MP286 — verify scan orchestration (the verify-station analogue of <see cref="IInductService"/>).
/// Looks up the active transport order for a scanned blind label, verifies the read labels against its
/// advised set, updates the line's consecutive-fail counter, and advances the carton lifecycle:
/// pass (incl. a bypass carton whose id was read) → verified; fail → re-armed for reprint when the global
/// <c>Reprint Labels</c> setting is on (owner ruling 2026-10-21, R7), otherwise held. Port glue over <c>sdisp_TOOL_PA_VerifyLabel</c>
/// + <c>sdisp_PA_VerifyThreshold_Update</c> (architecture-log 006).
/// <para>
/// Multi-host (decision-010): the whole verify (read → decide → persist) runs under the shared
/// <see cref="LockNames.Carton"/> lease for the scanned TuId, and the save is a version-checked update
/// (reload + re-apply + retry once on conflict). A lease timeout propagates as
/// <see cref="LockTimeoutException"/> — the adapter treats the scan as unprocessed (no routing decision).
/// </para>
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
    private const string BlindLabelType = "BlindLabel";

    private readonly ITransportOrderStore _store;
    private readonly IVerificationService _verification;
    private readonly IVerifyThresholdTracker _threshold;
    private readonly IClock _clock;
    private readonly ILockProvider _locks;
    private readonly ISettingsProvider _settings;
    private readonly ILogger<VerifyStationService> _logger;
    private readonly IRejectHistoryRepository? _rejects;
    private readonly PandA.Core.Ports.IXRefStore? _xref;

    public VerifyStationService(
        ITransportOrderStore store,
        IVerificationService verification,
        IVerifyThresholdTracker threshold,
        IClock clock,
        ILockProvider locks,
        ISettingsProvider settings,
        ILogger<VerifyStationService>? logger = null,
        IRejectHistoryRepository? rejects = null,
        PandA.Core.Ports.IXRefStore? xref = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _verification = verification ?? throw new ArgumentNullException(nameof(verification));
        _threshold = threshold ?? throw new ArgumentNullException(nameof(threshold));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? NullLogger<VerifyStationService>.Instance;
        _rejects = rejects;
        _xref = xref;
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

        var lease = await _locks.AcquireAsync(LockNames.Carton(blindLabel), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await using (lease.ConfigureAwait(false))
        {
            return await VerifyLockedAsync(blindLabel, scanned, options, failThreshold, xref, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async ValueTask<VerifyStationResult> VerifyLockedAsync(
        string blindLabel,
        IReadOnlyList<ScannedLabel> scanned,
        VerifyOptions options,
        int failThreshold,
        IReadOnlyList<LabelXref>? xref,
        CancellationToken cancellationToken)
    {
        var order = await _store.FindActiveByTuIdAsync(blindLabel, cancellationToken).ConfigureAwait(false);
        if (order is null || order.IsExceptionCarton)
        {
            return await FailWithoutDataAsync(blindLabel, order, failThreshold, cancellationToken).ConfigureAwait(false);
        }

        // Per-carton advice toggles (TransportOrder.Bypass / VerifyEnabled) narrow the caller's options: verify
        // runs only when both the station and the carton enable it; either may mark the carton bypassed.
        var effective = options with
        {
            VerifyEnabled = options.VerifyEnabled && order.VerifyEnabled,
            Bypass = options.Bypass || order.Bypass,
        };

        var allXref = await MergeCartonXrefAsync(order.TuId, xref, cancellationToken).ConfigureAwait(false);
        var verify = _verification.Verify(order.Labels, scanned, effective, allXref, order.TuId);
        var code = VerifyReasonCodes.For(verify);
        // F23: wave auto-complete here.

        // Pass (incl. a bypass carton whose id was read — R4) lets the carton proceed and clears the fail streak.
        // A bypass carton whose id was not read comes back as a failing outcome and counts (VF-3).
        // The streak is kept per printer that printed the carton (source sdisp_PA_VerifyThreshold_Update;
        // owner ruling 2026-10-21).
        var proceed = verify.Outcome is VerifyOutcome.Pass or VerifyOutcome.Ignore;
        var threshold = await _threshold
            .RegisterPrintersAsync(PrintersOf(order), pass: proceed, failThreshold, cancellationToken)
            .ConfigureAwait(false);
        var now = _clock.UtcNow;

        // Owner ruling 2026-10-21 (R7): a verify fail re-arms the carton for reprint (its next induct prints it
        // again, no attempt cap) while 'Reprint Labels' is on; off → held for manual intervention (decision-003).
        var rearm = !proceed
            && await _settings.GetAsync(KnownSettings.ReprintLabels, cancellationToken).ConfigureAwait(false);

        if (proceed)
        {
            _logger.LogInformation(
                "Verify passed for transport order {TuId} on line {LineId}; outcome {Outcome}, code {Code}.",
                order.TuId, order.LineId, verify.Outcome, (int)code);
        }
        else
        {
            _logger.LogWarning(
                "Verify failed for transport order {TuId} on line {LineId}; outcome {Outcome}, code {Code}, consecutive failures {ConsecutiveFailures}; {Disposition}.",
                order.TuId, order.LineId, verify.Outcome, (int)code, threshold.ConsecutiveFailures,
                rearm ? "re-armed for reprint" : "held for intervention");
        }

        LogPauses(threshold, order.LineId);
        await _store.SaveWithRetryAsync(
                order,
                o =>
                {
                    if (proceed)
                    {
                        o.MarkVerified(now);
                    }
                    else
                    {
                        o.MarkVerifyFailed(now, rearm);
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);

        // Source sdisp_PA_Scan_Verify L117-132: every verify whose code is not 1 (incl. bypass, 3) writes a
        // reject-history row with its reason.
        if (code != VerifyReasonCode.VerificationPassed)
        {
            await WriteRejectAsync(order.TuId, order.LineId, code, cancellationToken).ConfigureAwait(false);
        }

        // Decision-008: annotate the carton with its verify result as a routing criterion (Pass/Fail + reason
        // code + the advised pass/fail destination) for the adapter to project onto CriteriaBasedSorting.
        var routing = RoutingCriterion.ForVerify(verify, order.VerifyPassDest, order.VerifyFailDest);

        return new VerifyStationResult(
            proceed ? VerifyStationStatus.Verified
                : rearm ? VerifyStationStatus.ReArmed
                : VerifyStationStatus.HeldForIntervention,
            verify,
            threshold.PausePrinter,
            threshold.ConsecutiveFailures,
            routing,
            threshold.PrintersToPause);
    }

    /// <summary>
    /// Source PandaDataID = 0 (no carton data) and the synthetic exception carton (whose only label is the
    /// literal 'ExceptionLabel') both fail verify with code 0 (sdisp_TOOL_PA_VerifyLabel L134-140): the fail
    /// counts toward the printing printer's threshold, writes reject history, and routes to the fail
    /// destination (owner ruling 2026-10-21).
    /// </summary>
    private async ValueTask<VerifyStationResult> FailWithoutDataAsync(
        string blindLabel,
        TransportOrder? order,
        int failThreshold,
        CancellationToken cancellationToken)
    {
        const VerifyReasonCode code = VerifyReasonCode.PandaLogicError;
        var threshold = order is null
            ? new VerifyThresholdResult(0, false)
            : await _threshold
                .RegisterPrintersAsync(PrintersOf(order), pass: false, failThreshold, cancellationToken)
                .ConfigureAwait(false);

        if (order is null)
        {
            _logger.LogWarning("No active transport order for verify scan {TuId}; failing verify (code 0).", blindLabel);
        }
        else
        {
            var now = _clock.UtcNow;
            await _store.SaveWithRetryAsync(order, o => o.MarkVerifyFailed(now), cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "Exception carton {TuId} on line {LineId} reached verify; failing it (code 0).",
                order.TuId, order.LineId);
            LogPauses(threshold, order.LineId);
        }

        await WriteRejectAsync(order?.TuId ?? blindLabel, order?.LineId, code, cancellationToken, hasData: order is not null)
            .ConfigureAwait(false);

        var routing = RoutingCriterion.ForCode(code, failDestination: order?.VerifyFailDest);
        return new VerifyStationResult(
            order is null ? VerifyStationStatus.NoActiveOrder : VerifyStationStatus.HeldForIntervention,
            order is null ? null : new VerifyResult(VerifyOutcome.Fail, []),
            threshold.PausePrinter,
            threshold.ConsecutiveFailures,
            routing,
            threshold.PrintersToPause);
    }

    private async ValueTask WriteRejectAsync(
        string tuId,
        string? lineId,
        VerifyReasonCode code,
        CancellationToken cancellationToken,
        bool hasData = true)
    {
        if (_rejects is not { } rejects)
        {
            return;
        }

        var record = RejectRecord.Create(
            hasData ? InductService.StableOrderId(tuId) : 0,
            cartonListId: 0,
            code,
            _clock.UtcNow,
            tuId,
            lineId);
        await rejects.AddAsync(record, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<LabelXref>?> MergeCartonXrefAsync(
        string tuId,
        IReadOnlyList<LabelXref>? xref,
        CancellationToken cancellationToken)
    {
        if (_xref is not { } store)
        {
            return xref;
        }

        // Every carton xref barcode is an acceptable 'BlindLabel' value (source L191-195).
        var carton = await store.FindByTuIdAsync(tuId, cancellationToken).ConfigureAwait(false);
        if (carton.Count == 0)
        {
            return xref;
        }

        return [.. xref ?? [], .. carton.Select(x => new LabelXref(BlindLabelType, x.Barcode))];
    }

    private static List<string> PrintersOf(TransportOrder order) =>
        [.. order.PrintStates
            .Select(s => s.PrinterId)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    private void LogPauses(VerifyThresholdResult threshold, string lineId)
    {
        foreach (var printerId in threshold.PrintersToPause)
        {
            _logger.LogError(
                "Printer {PrinterId} on line {LineId} reached the verify fail threshold and should be force paused (~PP).",
                printerId, lineId);
        }
    }
}