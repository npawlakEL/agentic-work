namespace PandA.Core;

using PandA.Core.Induct;

public enum TransportOrderStatus
{
    /// <summary>Advised by the host; label set stored, awaiting induct scan (source PandaData/ADVISED).</summary>
    Advised = 0,

    /// <summary>Every label was dispatched to printers in a full run.</summary>
    Printed = 1,

    /// <summary>Labels were scanned and verified OK; the carton may proceed (source ActiveRecord=0).</summary>
    Verified = 2,

    /// <summary>
    /// Verify failed with <c>Reprint Labels</c> off. The carton is held for manual intervention (decision-003).
    /// With <c>Reprint Labels</c> on, a verify fail instead re-arms the carton back to <see cref="Advised"/>
    /// (owner ruling 2026-10-21, R7; see <see cref="TransportOrder.VerifyFailRearmed"/>).
    /// </summary>
    HeldForIntervention = 3,

    /// <summary>
    /// An operator has authorized a reprint for this carton (per-carton web-screen override). Permits
    /// exactly one further full print run, after which the authorization is consumed.
    /// </summary>
    ReprintAuthorized = 4,
}

/// <summary>
/// The carton, modelled as a Transport Order (decision-002): keyed by the blind label (<see cref="TuId"/>),
/// carrying the typed <see cref="PandaLabelSet"/> as an extension. Reprint policy per decision-003:
/// <see cref="PrintCount"/> is a monotonic run counter, verify failures hold for manual intervention, and
/// a reprint requires an explicit operator authorization. Backend-agnostic; the econtroller adapter maps
/// this onto MfcTransportOrder + DynamicField at integration.
/// </summary>
public sealed class TransportOrder
{
    private Dictionary<string, LabelPrintState> _printStates;

    public TransportOrder(string tuId, string lineId, PandaLabelSet labels, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tuId);
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        ArgumentNullException.ThrowIfNull(labels);

        TuId = tuId;
        LineId = lineId;
        Labels = labels;
        CreatedAt = createdAt;
        Status = TransportOrderStatus.Advised;
        VerifyEnabled = true;
        _printStates = BuildPrintStates(labels);
    }

    /// <summary>
    /// F10 / owner ruling 2026-10-21 — create the synthetic exception carton the SQL inserts in
    /// <c>sdisp_PA_LookupCarton</c> L509-536 (LabelType1='Exception', LabelBarcode1='ExceptionLabel',
    /// VerifyEnabled=1). It is recorded as printed (by <paramref name="printerId"/>) so the verify station
    /// finds it and fails it (reason 0, PandaDataID=0 path of <c>sdisp_PA_VerifyCarton</c>).
    /// </summary>
    public static TransportOrder CreateExceptionCarton(
        string cartonId,
        string lineId,
        Label exceptionLabel,
        string printerId,
        DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(exceptionLabel);
        var order = new TransportOrder(cartonId, lineId, new PandaLabelSet([exceptionLabel]), at)
        {
            IsExceptionCarton = true,
        };
        order.MarkLabelPrinted(exceptionLabel.LabelType, printerId, at);
        order.CompletePrintRun(at);
        return order;
    }

    /// <summary>True for a synthetic F10 exception carton (always fails verify with reason 0).</summary>
    public bool IsExceptionCarton { get; private init; }

    /// <summary>
    /// Optimistic-concurrency version of the persisted row (decision-010): 0 = never persisted. Stamped by the
    /// <see cref="ITransportOrderStore"/> on insert/update; an update only succeeds when the stored version
    /// still equals this value.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>Store adapters stamp the persisted row version after a read, insert, or successful update.</summary>
    public void StampVersion(long version)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        Version = version;
    }

    /// <summary>
    /// Deep, detached copy (including per-label print state and <see cref="Version"/>). Store adapters hand out
    /// and keep copies so callers never share a mutable instance with the store or another host.
    /// </summary>
    public TransportOrder Clone()
    {
        var copy = (TransportOrder)MemberwiseClone();
        copy._printStates = _printStates.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
        return copy;
    }

    /// <summary>The blind label / transport-unit id used to match advice with the induct scan.</summary>
    public string TuId { get; }

    public string LineId { get; private set; }

    public PandaLabelSet Labels { get; private set; }

    public TransportOrderStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string? WaveId { get; private set; }

    public string? ProfileName { get; private set; }

    public bool Bypass { get; private set; }

    public bool VerifyEnabled { get; private set; }

    public string? VerifyPassDest { get; private set; }

    public string? VerifyFailDest { get; private set; }

    /// <summary>
    /// Physical carton measurements captured at the induct scan (Wave-0 inbound foundation). Null until the
    /// carton is inducted; a bare (identity-only) induct leaves them null. Read by F-LOG1 (run history),
    /// F20 (gap classification), and DYNAP (apply-point calculation).
    /// </summary>
    public InductScanMeasurements? InductMeasurements { get; private set; }

    /// <summary>
    /// F20 (decision — read-quality) — the carton status classified at the induct scan from the blind label
    /// markers and front gap (<see cref="InductQualityClassifier"/>). Null until the carton is inducted.
    /// <see cref="CartonStatus.PrintReady"/> is the only status that prints the real label; every other value
    /// is an exception candidate (F10) and is logged by run-history (F-LOG1).
    /// </summary>
    public CartonStatus? StatusAtInduct { get; private set; }

    /// <summary>Number of completed (full) print runs this carton has been through (source Printed, monotonic).</summary>
    public int PrintCount { get; private set; }

    public DateTimeOffset? FirstPrintedAt { get; private set; }

    public DateTimeOffset? LastPrintedAt { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public DateTimeOffset? VerifyFailedAt { get; private set; }

    /// <summary>Reason recorded when an operator authorized a reprint (audit).</summary>
    public string? ReprintAuthorizationReason { get; private set; }

    /// <summary>
    /// True when the carton was system-re-armed by a pre-verify PLC tracking recovery (F15/decision-009).
    /// Permits exactly one further print run while the carton stays in <see cref="TransportOrderStatus.Advised"/>
    /// (distinct from operator <see cref="AuthorizeReprint"/>). Cleared when the next run completes.
    /// </summary>
    public bool TrackingRearmed { get; private set; }

    /// <summary>
    /// True when a verify fail re-armed the carton because <c>Reprint Labels</c> was on (owner ruling 2026-10-21,
    /// R7 — the source <c>sdisp_TOOL_PA_VerifyLabel</c> L460-467 <c>Printed=0, ActiveRecord=1</c>). Permits the
    /// next print run while the carton stays in <see cref="TransportOrderStatus.Advised"/>; cleared when that run
    /// completes. No attempt cap: every further verify fail re-arms again while the setting is on.
    /// </summary>
    public bool VerifyFailRearmed { get; private set; }

    /// <summary>Per-label-type print outcome for the current run.</summary>
    public IReadOnlyCollection<LabelPrintState> PrintStates => _printStates.Values;

    public LabelPrintState PrintStateFor(string labelType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(labelType);
        return _printStates[labelType];
    }

    /// <summary>
    /// True when the carton may be printed now: never printed and freshly advised, an operator has
    /// authorized a reprint (decision-003), or the system re-armed it (tracking recovery / verify-fail re-arm).
    /// A held/printed/verified carton without authorization may not.
    /// </summary>
    public bool CanPrint =>
        (PrintCount == 0 && Status == TransportOrderStatus.Advised)
        || Status == TransportOrderStatus.ReprintAuthorized
        || ((TrackingRearmed || VerifyFailRearmed) && Status == TransportOrderStatus.Advised);

    /// <summary>
    /// Induct print gate honoring the global <c>Reprint Labels</c> setting (source
    /// <c>sdisp_PA_LookupCarton</c> L308-347): when the setting is on, an already-printed / inactive carton
    /// prints again exactly as the SQL does; when off, the decision-003 rules of <see cref="CanPrint"/>
    /// apply (operator authorization required). Owner ruling 2026-10-21.
    /// </summary>
    public bool CanPrintWith(bool reprintLabels) => reprintLabels || CanPrint;

    /// <summary>
    /// True when the carton has produced output in its current advice generation: a completed run, a
    /// post-advice lifecycle state, or any label already dispatched. Used by re-advice to decide whether to
    /// re-arm (decision-013 D4).
    /// </summary>
    public bool HasPrinted =>
        PrintCount > 0 || Status != TransportOrderStatus.Advised || _printStates.Values.Any(s => s.Printed);

    /// <summary>Replace the advised label set (Phase-1 duplicate-advice = overwrite / last-wins; spec §6a).</summary>
    public void OverwriteAdvice(
        string lineId,
        PandaLabelSet labels,
        DateTimeOffset advisedAt,
        string? waveId = null,
        string? profileName = null,
        bool bypass = false,
        bool verifyEnabled = true,
        string? verifyPassDest = null,
        string? verifyFailDest = null,
        bool rearm = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        ArgumentNullException.ThrowIfNull(labels);

        LineId = lineId;
        Labels = labels;
        if (!rearm)
        {
            // decision-013 D4 / owner ruling 2026-10-21: with "Reprint Labels" off, re-advice of a carton that
            // already printed updates its label data + metadata but does NOT re-arm it. The lifecycle
            // (status, monotonic count, timestamps) is untouched; print states are kept per label type that
            // still exists (with their printer ids) and new label types start unprinted.
            SetAdviceMetadata(waveId, profileName, bypass, verifyEnabled, verifyPassDest, verifyFailDest);
            var rebuilt = BuildPrintStates(labels);
            foreach (var type in rebuilt.Keys.ToList())
            {
                if (_printStates.TryGetValue(type, out var kept))
                {
                    rebuilt[type] = kept;
                }
            }

            _printStates = rebuilt;
            return;
        }

        CreatedAt = advisedAt;
        Status = TransportOrderStatus.Advised;
        PrintCount = 0;
        FirstPrintedAt = null;
        LastPrintedAt = null;
        VerifiedAt = null;
        VerifyFailedAt = null;
        ReprintAuthorizationReason = null;
        TrackingRearmed = false;
        VerifyFailRearmed = false;
        SetAdviceMetadata(waveId, profileName, bypass, verifyEnabled, verifyPassDest, verifyFailDest);
        _printStates = BuildPrintStates(labels);
    }

    public void SetAdviceMetadata(
        string? waveId = null,
        string? profileName = null,
        bool bypass = false,
        bool verifyEnabled = true,
        string? verifyPassDest = null,
        string? verifyFailDest = null)
    {
        WaveId = string.IsNullOrWhiteSpace(waveId) ? null : waveId;
        ProfileName = string.IsNullOrWhiteSpace(profileName) ? null : profileName;
        Bypass = bypass;
        VerifyEnabled = verifyEnabled;
        VerifyPassDest = string.IsNullOrWhiteSpace(verifyPassDest) ? null : verifyPassDest;
        VerifyFailDest = string.IsNullOrWhiteSpace(verifyFailDest) ? null : verifyFailDest;
    }

    /// <summary>
    /// Wave-0 inbound foundation — stamp the carton with the physical measurements from its induct scan
    /// (dimensions, front gap, PLC index bundle). Pure state capture; no gating behavior. Later features
    /// (F-LOG1/F20/DYNAP) read <see cref="InductScan"/> once they are wired onto the induct path.
    /// </summary>
    public void StampInductScan(InductScanMeasurements measurements)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        InductMeasurements = measurements;
    }

    /// <summary>
    /// F20 — record the read-quality status classified at the induct scan (see
    /// <see cref="InductQualityClassifier"/>). Pure capture; used by run-history (F-LOG1) and the exception
    /// trigger (F10). Does not gate the print decision on its own.
    /// </summary>
    public void StampInductStatus(CartonStatus status)
    {
        StatusAtInduct = status;
    }

    /// <summary>
    /// F15 / decision-009 — pre-verify PLC tracking recovery. Only a carton still in
    /// <see cref="TransportOrderStatus.Printed"/> can be recovered (a <see cref="TransportOrderStatus.Verified"/>
    /// or <see cref="TransportOrderStatus.HeldForIntervention"/> carton, or an already re-armed
    /// <see cref="TransportOrderStatus.Advised"/> one, is a no-op — making repeat events idempotent).
    /// When reprint is allowed the carton is system-re-armed back to <see cref="TransportOrderStatus.Advised"/>
    /// (<see cref="PrintCount"/> stays monotonic); when reprint is disabled it is held for an operator.
    /// </summary>
    public TrackingRecovery ResetForTrackingEvent(int eventCode, bool reprintAllowed)
    {
        _ = eventCode;
        if (Status != TransportOrderStatus.Printed)
        {
            return TrackingRecovery.NotApplicable;
        }

        if (!reprintAllowed)
        {
            Status = TransportOrderStatus.HeldForIntervention;
            return TrackingRecovery.Held;
        }

        Status = TransportOrderStatus.Advised;
        TrackingRearmed = true;
        VerifiedAt = null;
        VerifyFailedAt = null;
        ReprintAuthorizationReason = null;
        foreach (var state in _printStates.Values)
        {
            state.Reset();
        }

        return TrackingRecovery.ReArmed;
    }

    public void ForceMarkPrinted(DateTimeOffset at)
    {
        foreach (var state in _printStates.Values)
        {
            if (!state.Printed)
            {
                state.MarkPrinted("forced", at);
            }
        }

        CompletePrintRun(at);
    }

    /// <summary>Record that a single label type was dispatched to a printer (partial-safe, no counter change).</summary>
    public void MarkLabelPrinted(string labelType, string printerId, DateTimeOffset at)
    {
        if (_printStates.TryGetValue(labelType, out var state))
        {
            state.MarkPrinted(printerId, at);
        }
    }

    /// <summary>
    /// Complete a full print run: every label printed. Increments the monotonic <see cref="PrintCount"/>,
    /// stamps timestamps, and consumes any reprint authorization. Only call when the run printed all labels.
    /// </summary>
    public void CompletePrintRun(DateTimeOffset at)
    {
        PrintCount++;
        LastPrintedAt = at;
        FirstPrintedAt ??= at;
        Status = TransportOrderStatus.Printed;
        ReprintAuthorizationReason = null;
        TrackingRearmed = false;
        VerifyFailRearmed = false;
    }

    /// <summary>Verify passed: carton is complete and may proceed (source ActiveRecord=0, VerifyTime set).</summary>
    public void MarkVerified(DateTimeOffset verifiedAt)
    {
        Status = TransportOrderStatus.Verified;
        VerifiedAt = verifiedAt;
    }

    /// <summary>
    /// Verify failed. With <paramref name="rearm"/> false (<c>Reprint Labels</c> off) the carton is held for
    /// manual intervention (decision-003). With <paramref name="rearm"/> true (<c>Reprint Labels</c> on — owner
    /// ruling 2026-10-21, R7) it is re-armed: back to <see cref="TransportOrderStatus.Advised"/> with its per-label
    /// print states reset so its next induct prints it again — indefinitely, no attempt cap. The advised set,
    /// the monotonic <see cref="PrintCount"/> and the first/last printed timestamps are preserved either way.
    /// </summary>
    public void MarkVerifyFailed(DateTimeOffset failedAt, bool rearm = false)
    {
        VerifyFailedAt = failedAt;
        if (!rearm)
        {
            Status = TransportOrderStatus.HeldForIntervention;
            return;
        }

        Status = TransportOrderStatus.Advised;
        VerifyFailRearmed = true;
        VerifiedAt = null;
        ReprintAuthorizationReason = null;
        foreach (var state in _printStates.Values)
        {
            state.Reset();
        }
    }

    /// <summary>
    /// Operator authorization to reprint this carton (per-carton web-screen override). Enables exactly one
    /// further full print run. Never called automatically; does not reset <see cref="PrintCount"/>.
    /// </summary>
    public void AuthorizeReprint(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = TransportOrderStatus.ReprintAuthorized;
        ReprintAuthorizationReason = reason;
        foreach (var state in _printStates.Values)
        {
            state.Reset();
        }
    }

    /// <summary>
    /// Clear per-label print outcomes before a setting-driven reprint (<c>Reprint Labels</c> on) of an
    /// already-printed carton, so the new run's per-label state is not polluted by the previous run.
    /// </summary>
    public void ResetPrintStatesForReprint()
    {
        foreach (var state in _printStates.Values)
        {
            state.Reset();
        }
    }

    private static Dictionary<string, LabelPrintState> BuildPrintStates(PandaLabelSet labels) =>
        labels.DistinctLabelTypes()
            .ToDictionary(t => t, t => new LabelPrintState(t), StringComparer.OrdinalIgnoreCase);
}
