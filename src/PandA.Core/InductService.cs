using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PandA.Core.Concurrency;
using PandA.Core.Induct;
using PandA.Core.Labels;
using PandA.Core.Ports;
using PandA.Core.Settings;
using PandA.Core.Slots;
using PandA.Core.Verification;
using PandA.Core.Zpl;

namespace PandA.Core;

/// <summary>
/// MP2 — induct scan. Looks up the active transport order for a scanned blind label, selects a printer for
/// each label (architecture-log 005), dispatches the ZPL, stamps LastPrinted, and marks the order printed.
/// </summary>
public interface IInductService
{
    /// <summary>
    /// Induct from a decoded <see cref="InductScan"/> event (the transport-agnostic projection of the PLC
    /// 281 → ADS plugin → MfcTransportOrder+extension flow; decision-020). Carries the physical carton
    /// measurements so run-history/quality/apply-point features can read them from the carton.
    /// </summary>
    ValueTask<InductResult> InductAsync(
        InductScan scan,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Identity-only induct convenience (no physical measurements). Equivalent to inducting an
    /// <see cref="InductScan.ForBlindLabel"/> scan; used by callers/tests that only exercise the print path.
    /// </summary>
    ValueTask<InductResult> InductAsync(
        string lineId,
        string blindLabel,
        CancellationToken cancellationToken = default) =>
        InductAsync(InductScan.ForBlindLabel(lineId, blindLabel), cancellationToken);
}

/// <remarks>
/// Multi-host (decision-010): a carton can be inducted on any lane served by any host, so the whole
/// check-CanPrint → print → persist sequence runs under the shared <see cref="LockNames.Carton"/> lease
/// (<see cref="ILockProvider"/> is a required, cross-host dependency — there is no process-local fallback).
/// The lease is renewed (fencing check) immediately before dispatching; if it was lost nothing is printed.
/// On lease timeout the induct fails safe (<see cref="InductStatus.LockTimeout"/>, nothing printed). Printer
/// state is a fresh <see cref="IPrinterStateStore"/> snapshot per induct; the order is persisted with a
/// version-checked update and, on a conflict, the recorded mutations are re-applied to the reloaded row once
/// (the labels are never printed twice).
/// </remarks>
public sealed class InductService : IInductService
{
    private readonly ITransportOrderStore _store;
    private readonly ILineProvider _lines;
    private readonly IPrinterSelectionService _selection;
    private readonly IPrinterGateway _gateway;
    private readonly IClock _clock;
    private readonly ISettingsProvider _settings;
    private readonly IPrinterStateStore _printerStates;
    private readonly ILockProvider _locks;
    private readonly ISlotIndexProvider _slots;
    private readonly ICartonRunRepository _runs;
    private readonly IMinGapProvider? _minGap;
    private readonly IXRefStore? _xref;
    private readonly IExceptionLabelSource? _exceptionLabels;
    private readonly ILogger<InductService> _logger;
    private readonly FirePointResolver _firePoints = new();
    private readonly ApplyPointResolver _applyPoints = new();

    public InductService(
        ITransportOrderStore store,
        ILineProvider lines,
        IPrinterSelectionService selection,
        IPrinterGateway gateway,
        IClock clock,
        ISettingsProvider settings,
        IPrinterStateStore printerStates,
        ILockProvider locks,
        ISlotIndexProvider slots,
        ICartonRunRepository runs,
        IMinGapProvider? minGap = null,
        IXRefStore? xref = null,
        IExceptionLabelSource? exceptionLabels = null,
        ILogger<InductService>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _lines = lines ?? throw new ArgumentNullException(nameof(lines));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _printerStates = printerStates ?? throw new ArgumentNullException(nameof(printerStates));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _slots = slots ?? throw new ArgumentNullException(nameof(slots));
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _minGap = minGap;
        _xref = xref;
        _exceptionLabels = exceptionLabels;
        _logger = logger ?? NullLogger<InductService>.Instance;
    }

    public ValueTask<InductResult> InductAsync(
        string lineId,
        string blindLabel,
        CancellationToken cancellationToken = default) =>
        InductAsync(InductScan.ForBlindLabel(lineId, blindLabel), cancellationToken);

    public async ValueTask<InductResult> InductAsync(
        InductScan scan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentException.ThrowIfNullOrWhiteSpace(scan.LineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scan.BlindLabel);

        var leases = new List<ILease>(2);
        try
        {
            // Serialize every writer of this carton across all hosts (platform-settings F13 / source PA_Lock):
            // a duplicate induct on another lane cannot pass the CanPrint check while this run is dispatching.
            leases.Add(await _locks
                .AcquireAsync(LockNames.Carton(scan.BlindLabel), cancellationToken: cancellationToken)
                .ConfigureAwait(false));

            return await InductLockedAsync(scan, leases, cancellationToken).ConfigureAwait(false);
        }
        catch (LockTimeoutException ex)
        {
            // Fail safe: another host holds the carton. Nothing printed; the PLC still gets a slot assignment.
            _logger.LogWarning(
                ex,
                "Induct scan {TuId} on line {LineId} not processed: carton lease {Resource} is held by another host.",
                scan.BlindLabel, scan.LineId, ex.Resource);
            var slot = await _slots.NextSlotAsync(cancellationToken).ConfigureAwait(false);
            return InductResult.LockTimeout().WithSlot(slot);
        }
        finally
        {
            for (var i = leases.Count - 1; i >= 0; i--)
            {
                await leases[i].DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async ValueTask<InductResult> InductLockedAsync(
        InductScan scan,
        List<ILease> leases,
        CancellationToken cancellationToken)
    {
        var blindLabel = scan.BlindLabel;

        // Source sdisp_PA_PickPrinter L97-101: every induct (including no-print outcomes) draws the next
        // PLC carton-assign slot from CartonAssignSeq (1..300 CYCLE) — owner ruling 2026-10-21.
        var slot = await _slots.NextSlotAsync(cancellationToken).ConfigureAwait(false);

        var order = await _store.FindActiveByTuIdAsync(blindLabel, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            // F18: a non-BL scan (oLPN/UPC/…) doesn't match a blind label directly; resolve it through the
            // barcode cross-reference populated at advice time.
            order = await ResolveByBarcodeAsync(scan, cancellationToken).ConfigureAwait(false);
            if (order is not null && !string.Equals(order.TuId, blindLabel, StringComparison.OrdinalIgnoreCase))
            {
                // Also hold the resolved carton's own lease, then re-read it so CanPrint reflects any run that
                // completed (on any host) while we waited.
                leases.Add(await _locks
                    .AcquireAsync(LockNames.Carton(order.TuId), cancellationToken: cancellationToken)
                    .ConfigureAwait(false));
                order = await _store.FindActiveByTuIdAsync(order.TuId, cancellationToken).ConfigureAwait(false);
            }
        }

        var result = await InductOrderAsync(scan, order, slot, leases, cancellationToken).ConfigureAwait(false);
        return result.WithSlot(slot);
    }

    /// <summary>
    /// Fencing check before an irreversible dispatch: renew every held lease. False means a lease expired and
    /// another host may own the carton now — the caller must not print.
    /// </summary>
    private async ValueTask<bool> StillOwnsAsync(IReadOnlyList<ILease> leases, CancellationToken cancellationToken)
    {
        foreach (var lease in leases)
        {
            if (!await lease.RenewAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogError(
                    "Lease {Resource} was lost before dispatch (expired; another host may own it); nothing printed.",
                    lease.Resource);
                return false;
            }
        }

        return true;
    }
    private async ValueTask<InductResult> InductOrderAsync(
        InductScan scan,
        TransportOrder? order,
        int slot,
        IReadOnlyList<ILease> leases,
        CancellationToken cancellationToken)
    {
        var lineId = scan.LineId;
        var blindLabel = scan.BlindLabel;

        // F20: classify read-quality from the blind-label markers + front gap BEFORE any data lookup, as
        // sdisp_PA_LookupCarton L208-235 does. MinGap defaults to the global "MinGap" setting (seed 20).
        var minGap = _minGap is { } provider
            ? provider.GetMinGap()
            : await _settings.GetAsync(KnownSettings.MinGap, cancellationToken).ConfigureAwait(false);
        var inductStatus = InductQualityClassifier.Classify(blindLabel, scan.FrontGap, minGap);

        if (order is not null)
        {
            // Wave-0 inbound foundation: stamp the carton with its physical induct measurements so downstream
            // features (F-LOG1/F20/DYNAP) can read them (decision-020).
            order.StampInductScan(scan.ToMeasurements());
            order.StampInductStatus(inductStatus);
        }

        if (inductStatus != CartonStatus.PrintReady)
        {
            // Owner ruling 2026-10-21 (source sdisp_PA_Scan_Induct L107 NoPrintFlag): a read that is not
            // PrintReady (no read / no data / label conflict / gap error) never prints the carton's real
            // labels, even when an order resolves. Only the F10 exception label may print.
            _logger.LogInformation(
                "Induct scan {TuId} on line {LineId} classified {Status} (front gap {FrontGap}); real labels not printed.",
                blindLabel, lineId, inductStatus, scan.FrontGap);
            var exception = await TryEmitExceptionLabelAsync(scan, inductStatus, slot, leases, cancellationToken).ConfigureAwait(false);
            if (exception is not null)
            {
                return exception; // the exception path records its own run
            }

            await CreateRunAsync(scan, order?.TuId ?? blindLabel, inductStatus, cancellationToken).ConfigureAwait(false);
            return InductResult.NotPrintReady(inductStatus);
        }

        if (order is null)
        {
            // F10 (decision-021): an unmatched carton with exceptions enabled gets a locally-generated
            // exception label against a synthesized identity, then routes to reject at verify.
            var exception = await TryEmitExceptionLabelAsync(scan, inductStatus, slot, leases, cancellationToken).ConfigureAwait(false);
            if (exception is not null)
            {
                return exception;
            }

            _logger.LogWarning("No active transport order for induct scan {TuId} on line {LineId}.", blindLabel, lineId);
            return InductResult.NoActiveOrder();
        }

        // F-LOG1: record this carton pass through the induct scanner for run-history auditing. The assigned
        // printer is filled in later (UpdatePrinterAsync) once a printer is matched in the print loop.
        var runId = await CreateRunAsync(scan, order.TuId, inductStatus, cancellationToken).ConfigureAwait(false);

        if (order.Labels.Labels.Count == 0)
        {
            _logger.LogWarning("Transport order {TuId} on line {LineId} has no label data.", order.TuId, lineId);
            return InductResult.NoData();
        }

        // Reprint policy: "Reprint Labels" on (source fallback 1, decision-013 D2) lets an already-printed
        // carton print again exactly as sdisp_PA_LookupCarton L308-347; off requires an operator
        // authorization (decision-003). Owner ruling 2026-10-21.
        var reprintLabels = await _settings.GetAsync(KnownSettings.ReprintLabels, cancellationToken).ConfigureAwait(false);
        if (!order.CanPrintWith(reprintLabels))
        {
            _logger.LogInformation(
                "Transport order {TuId} on line {LineId} is not eligible for reprint. Status {Status}, print count {PrintCount}.",
                order.TuId, lineId, order.Status, order.PrintCount);
            return InductResult.NoReprint();
        }

        var resetForReprint = !order.CanPrint;
        if (resetForReprint)
        {
            order.ResetPrintStatesForReprint();
        }

        return await PrintOrderAsync(scan, order, slot, runId, resetForReprint, leases, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<long> CreateRunAsync(
        InductScan scan,
        string tuId,
        CartonStatus inductStatus,
        CancellationToken cancellationToken)
    {
        var record = CartonRunRecord.Create(
            tuId: tuId,
            pandaDataId: StableOrderId(tuId),
            lineId: scan.LineId,
            sorterNumber: scan.SorterNumber,
            sorterMode: scan.SorterMode,
            deviceId: scan.DeviceId,
            seqNum: scan.SeqNum,
            labelStatus: 0,
            scannedLabels: [.. scan.ScannedLabels],
            length: scan.Length,
            width: scan.Width,
            height: scan.Height,
            weight: scan.Weight,
            frontGap: scan.FrontGap,
            statusAtInduct: inductStatus,
            assignedPrinter: null,
            destinationLane: null,
            createdAt: _clock.UtcNow);
        return await _runs.CreateRunAsync(record, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<InductResult> PrintOrderAsync(
        InductScan scan,
        TransportOrder order,
        int slot,
        long runId,
        bool resetForReprint,
        IReadOnlyList<ILease> leases,
        CancellationToken cancellationToken)
    {
        var lineId = scan.LineId;
        var context = await _lines.GetLineAsync(lineId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No line configuration for line '{lineId}'.");

        // PROFSW / GAP F19: resolve the carton's fire-point profile by its host-supplied ProfileName.
        // A ProfileName that is set but not among the line's active profiles => NoProfile, print nothing.
        var (profileOk, activeProfile) = ResolveProfile(context.Config, order);
        if (!profileOk)
        {
            _logger.LogWarning(
                "Transport order {TuId} on line {LineId} requests unknown profile {ProfileName}; nothing printed.",
                order.TuId, lineId, order.ProfileName);
            return InductResult.NoProfile();
        }

        // Owner ruling 2026-10-21 (source sdisp_PA_LookupCarton L392-395 → status Bypass → NoPrintFlag):
        // a Bypass carton prints nothing and gets no exception label.
        if (order.Bypass)
        {
            _logger.LogInformation(
                "Transport order {TuId} on line {LineId} is flagged Bypass; nothing printed.",
                order.TuId, lineId);
            return InductResult.Bypass();
        }

        // Selection is orientation-agnostic: each label routes to whatever printer maps to its type,
        // so a carton's side (Shipping) and top (Content) labels each reach their own printer.
        // Fresh shared printer-state snapshot for this induct only (never cached across inducts/hosts).
        var states = await _printerStates.GetLineSnapshotAsync(context.Config, cancellationToken).ConfigureAwait(false);
        var selection = _selection.Select(context.Config, states, order.Labels);

        if (selection.Assignments.Any(a => a.Status == LabelSelectionStatus.Assigned)
            && !await StillOwnsAsync(leases, cancellationToken).ConfigureAwait(false))
        {
            return InductResult.LockTimeout();
        }

        var now = _clock.UtcNow;
        var printersById = context.Config.Printers.ToDictionary(p => p.PrinterId, StringComparer.OrdinalIgnoreCase);
        var runPrinterRecorded = false;
        var outcomes = selection.Assignments.ToList();

        // Every mutation applied to the order in this run, so it can be re-applied to a reloaded row if the
        // version-checked save conflicts (the labels are already out; they are never printed again).
        var measurements = order.InductMeasurements;
        var statusAtInduct = order.StatusAtInduct;
        var replay = new List<Action<TransportOrder>>
        {
            o =>
            {
                if (measurements is not null)
                {
                    o.StampInductScan(measurements);
                }

                if (statusAtInduct is { } s)
                {
                    o.StampInductStatus(s);
                }
            },
        };
        if (resetForReprint)
        {
            replay.Add(o => o.ResetPrintStatesForReprint());
        }

        for (var i = 0; i < outcomes.Count; i++)
        {
            var assignment = outcomes[i];
            if (assignment.Status != LabelSelectionStatus.Assigned || assignment.PrinterId is null)
            {
                continue;
            }

            var printer = printersById[assignment.PrinterId];
            var label = assignment.Label;

            // F-LOG1: attach the first matched printer to the run-history record for this carton.
            if (!runPrinterRecorded)
            {
                await _runs.UpdatePrinterAsync(runId, printer.PrinterId, cancellationToken).ConfigureAwait(false);
                runPrinterRecorded = true;
            }

            // Resolve the print/apply firing points from the carton's resolved fire-point profile (if any).
            FirePoint? firePoint = null;
            int? applyPulse = null;
            if (activeProfile is { } profile)
            {
                var resolution = _firePoints.Resolve(profile, printer.PrinterId, label.LabelType);
                firePoint = resolution.FirePoint;

                // Owner ruling 2026-10-21 (source sdisp_PA2BP_SendPrinterFirePoints L152-155/L178-181): no
                // fire-point row, a tracking device of 0, or a NeglectPrint "0…" print point is ErrorCode 2 —
                // the label is skipped and NOT printed. LastPrinted is still stamped (PickPrinter L469-473).
                if (firePoint is not { NeglectPrint: false })
                {
                    outcomes[i] = LabelAssignment.NoFirePoint(label, printer.PrinterId);
                    await StampLastPrintedAsync(printer.PrinterId, now, cancellationToken).ConfigureAwait(false);
                    _logger.LogWarning(
                        "No usable fire point for label {LabelType} of transport order {TuId} on printer {PrinterId} (line {LineId}); label skipped.",
                        label.LabelType, order.TuId, printer.PrinterId, lineId);
                    continue;
                }

                // DYNAP (decision-016): convert the human APPLY point (inch/edge) into a carton-aware PLC
                // pulse when the carton's physical dimensions are known. PRINT point stays static. Top-apply
                // additionally needs tamp kinematics (not yet commissioned) so it is left null for now.
                // The line's DynamicPrintPoint switch disables the dynamic pulse entirely.
                if (context.Config.DynamicPrintPoint
                    && firePoint is { } fp
                    && order.InductMeasurements is { } m
                    && m.Length > 0
                    && printer.PrinterType == ApplyOrientation.Side)
                {
                    var applyConfig = new ApplyPointConfig(
                        EncoderResolution: context.Config.EncoderResolution,
                        LabelWidthInches: printer.LabelWidthInches,
                        DefaultApplyDistance: printer.DefaultApplyDistance);
                    // Side-apply math ignores height; Length is already in step pulses. When Top-apply is
                    // commissioned, Height (PLC units) must be converted to inches before it feeds the
                    // tamp-kinematic lead correction (which is inch-based). Gated to Side keeps that latent.
                    var dims = new CartonDimensions(m.Length, m.Height);
                    applyPulse = _applyPoints.Resolve(fp.ApplyFirePoint, ApplyOrientation.Side, dims, applyConfig);
                }
            }

            var zpl = PrepareZpl(context.Config, label.Zpl);

            try
            {
                await _gateway.SendAsync(
                    new PrintJob(printer.PrinterId, printer.Ip, printer.Port, label.LabelType, label.Lpn, zpl, firePoint, applyPulse, slot),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One label's dispatch failure must not strand the carton: record it as not printed and keep
                // dispatching the rest so the per-label state and overall outcome stay consistent. LastPrinted
                // is stamped on every attempt (source PickPrinter L469-473) so load balancing rotates away
                // from a broken printer (owner ruling 2026-10-21).
                outcomes[i] = LabelAssignment.DispatchFailed(label, printer.PrinterId);
                await StampLastPrintedAsync(printer.PrinterId, now, cancellationToken).ConfigureAwait(false);
                _logger.LogError(
                    ex,
                    "Failed to dispatch label {LabelType} for transport order {TuId} to printer {PrinterId} on line {LineId}.",
                    label.LabelType, order.TuId, printer.PrinterId, lineId);
                continue;
            }

            _logger.LogInformation(
                "Dispatched label {LabelType} for transport order {TuId} to printer {PrinterId} on line {LineId}.",
                label.LabelType, order.TuId, printer.PrinterId, lineId);

            // Stamp LastPrinted so the next carton rotates (round-robin state).
            await StampLastPrintedAsync(printer.PrinterId, now, cancellationToken).ConfigureAwait(false);

            // Record which label type printed, to which printer (per-label outcome; decision-003).
            var printedType = label.LabelType;
            var printedOn = printer.PrinterId;
            order.MarkLabelPrinted(printedType, printedOn, now);
            replay.Add(o => o.MarkLabelPrinted(printedType, printedOn, now));
        }

        var result = InductResult.FromAssignments(outcomes);
        // F08: routing here.

        // Only a FULL run counts as a print run and increments the monotonic counter (decision-003).
        if (result.Status == InductStatus.Printed)
        {
            order.CompletePrintRun(now);
            replay.Add(o => o.CompletePrintRun(now));
            await PersistPrintRunAsync(order, replay, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Completed print run for transport order {TuId} on line {LineId}; print count is {PrintCount}.",
                order.TuId, lineId, order.PrintCount);
        }
        else if (result.Status == InductStatus.PartiallyPrinted
            || outcomes.Any(a => a.Status is LabelSelectionStatus.DispatchFailed or LabelSelectionStatus.NoFirePoint))
        {
            // A dispatch was attempted: persist per-label print state, but do not count the run or advance
            // status. (A pure no-eligible-printer outcome dispatched nothing and is not persisted.)
            await PersistPrintRunAsync(order, replay, cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "Transport order {TuId} on line {LineId} not fully printed ({Status}).",
                order.TuId, lineId, result.Status);
        }

        return result;
    }

    /// <summary>LastPrinted is a single-column, last-writer-wins rotation hint in the shared printer store.</summary>
    private ValueTask StampLastPrintedAsync(string printerId, DateTimeOffset now, CancellationToken cancellationToken) =>
        _printerStates.StampLastPrintedAsync(printerId, now, cancellationToken);

    /// <summary>
    /// Persist the print-run mutations with a version-checked update. On a conflict (a writer bypassed the
    /// carton lease, or the lease expired under a stalled host) reload the row and re-apply <paramref name="replay"/>
    /// once; if that conflicts too, log an Error and give up — the labels were dispatched and are never
    /// printed again by this run.
    /// </summary>
    private async ValueTask PersistPrintRunAsync(
        TransportOrder order,
        IReadOnlyList<Action<TransportOrder>> replay,
        CancellationToken cancellationToken)
    {
        try
        {
            await _store.SaveAsync(order, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict persisting print run for {TuId}; re-applying to the latest row.", order.TuId);
        }

        try
        {
            var fresh = await _store.FindActiveByTuIdAsync(order.TuId, cancellationToken).ConfigureAwait(false);
            if (fresh is null)
            {
                _logger.LogError("Transport order {TuId} vanished while persisting its print run; print state not recorded.", order.TuId);
                return;
            }

            foreach (var apply in replay)
            {
                apply(fresh);
            }

            await _store.UpdateAsync(fresh, cancellationToken).ConfigureAwait(false);
        }
        catch (ConcurrencyConflictException ex)
        {
            _logger.LogError(ex, "Repeated concurrency conflict persisting print run for {TuId}; print state not recorded (labels were dispatched once).", order.TuId);
        }
    }

    /// <summary>
    /// Final ZPL for dispatch: F11 VetLabel sanitization when the line's <c>FilterLabels</c> is on, then the
    /// F12 <c>~HS</c> host-status suffix when enabled (source <c>sdisp_PA_Print</c> order).
    /// </summary>
    private static string PrepareZpl(LineConfig config, string zpl)
    {
        var prepared = config.FilterLabels ? ZplSanitizer.Sanitize(zpl) : zpl;
        return config.PrinterStatusSuffix ? ZplStatusSuffix.Append(prepared) : prepared;
    }

    /// <summary>
    /// F18 — resolve a non-blind-label induct scan to its transport order via the barcode cross-reference.
    /// Considers the scanned blind label plus any additional scanned barcodes, collects the cross-referenced
    /// TuIds, and elects the best active order: lowest <see cref="TransportOrder.PrintCount"/> first, then the
    /// earliest advised (source election order PrintCount ASC → ActiveRecord DESC → wave StatusTime ASC,
    /// approximated by PrintCount then CreatedAt). Returns null when the xref store is absent or no match.
    /// </summary>
    private async ValueTask<TransportOrder?> ResolveByBarcodeAsync(InductScan scan, CancellationToken ct)
    {
        if (_xref is not { } xref)
        {
            return null;
        }

        var barcodes = new List<string> { scan.BlindLabel };
        barcodes.AddRange(scan.ScannedLabels);

        var tuIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var barcode in barcodes.Distinct(StringComparer.Ordinal))
        {
            foreach (var tuId in await xref.FindTuIdsByBarcodeAsync(barcode, ct).ConfigureAwait(false))
            {
                tuIds.Add(tuId);
            }
        }

        TransportOrder? best = null;
        foreach (var tuId in tuIds)
        {
            var candidate = await _store.FindActiveByTuIdAsync(tuId, ct).ConfigureAwait(false);
            if (candidate is null)
            {
                continue;
            }

            if (best is null
                || candidate.PrintCount < best.PrintCount
                || (candidate.PrintCount == best.PrintCount && candidate.CreatedAt < best.CreatedAt))
            {
                best = candidate;
            }
        }

        if (best is not null)
        {
            _logger.LogInformation(
                "Induct scan {TuId} resolved to transport order {ResolvedTuId} via barcode cross-reference.",
                scan.BlindLabel, best.TuId);
        }

        return best;
    }

    /// <summary>
    /// F10 (decision-021) — emit a locally-generated exception label for an unmatched carton. Gated by the
    /// effective <c>PrintExceptionLabels</c> flag (a defined global overrides per-line) and a non-Bypass read
    /// that maps to an exception reason. Synthesizes a
    /// human-readable unique id (<c>{Reason}-{seq}-{runId}</c>, the run id store-generated across hosts), builds the ZPL from the injected
    /// <see cref="IExceptionLabelSource"/> (LocalTemplate by default; DCMS/eHub via the adapter), prints it to
    /// an available printer of the line's apply orientation (Side if any side printer, else Top; <c>NoPrinter</c>
    /// when none is available), records the synthetic
    /// carton as printed (F-LOG1), and returns a verify-then-reject routing criterion (F08). Returns
    /// <c>null</c> when exceptions are disabled, no source is wired, or the read yields no exception reason.
    /// </summary>
    private async ValueTask<InductResult?> TryEmitExceptionLabelAsync(
        InductScan scan,
        CartonStatus status,
        int slot,
        IReadOnlyList<ILease> leases,
        CancellationToken ct)
    {
        if (_exceptionLabels is not { } source)
        {
            return null;
        }

        var context = await _lines.GetLineAsync(scan.LineId, ct).ConfigureAwait(false);
        if (context is null)
        {
            return null;
        }

        // Effective flag: a defined global PrintExceptionLabels overrides the per-line value.
        var global = await _settings
            .GetAsync<bool?>(KnownSettings.PrintExceptionLabels.Name, null, ct)
            .ConfigureAwait(false);
        if (!ExceptionLabelPolicy.Effective(global, context.Config.PrintExceptionLabels))
        {
            return null;
        }

        if (status == CartonStatus.Bypass || ExceptionLabelPolicy.Classify(status) is not { } exceptionType)
        {
            return null;
        }

        // Orientation: side if the line has any side-apply printer, otherwise top.
        var orientation = context.Config.Printers.Any(p => p.PrinterType == ApplyOrientation.Side)
            ? ApplyOrientation.Side
            : ApplyOrientation.Top;

        if (!context.Config.Printers.Any(p => p.PrinterType == orientation))
        {
            _logger.LogWarning(
                "Exception carton on line {LineId} has no {Orientation} printer; label not emitted.",
                scan.LineId, orientation);
            return null;
        }

        // Pick among available (online, non-spare) printers of that orientation with the same ranking as
        // normal labels (least-recently-printed / configured order).
        var states = await _printerStates.GetLineSnapshotAsync(context.Config, ct).ConfigureAwait(false);
        var printer = SelectExceptionPrinter(context, states, orientation);
        if (printer is null)
        {
            _logger.LogWarning(
                "Exception carton on line {LineId} has no available {Orientation} printer; label not emitted.",
                scan.LineId, orientation);
            return InductResult.NoPrinter();
        }

        // The exception carton id derives from a store-generated run id (source <CartonID> = CartonListID), so
        // it is unique across every host sharing the database.
        var runId = await _runs.ReserveRunIdAsync(ct).ConfigureAwait(false);
        var cartonId = ExceptionLabelPolicy.MintCartonId(exceptionType, scan.SeqNum, runId);
        var lpn = ExceptionLabelPolicy.UsesLpn(exceptionType) ? scan.BlindLabel : null;

        var build = await source.BuildAsync(exceptionType, cartonId, lpn, ct).ConfigureAwait(false);
        if (!build.HasLabel || build.Zpl is null)
        {
            _logger.LogWarning(
                "No active exception template for {Reason}; carton {CartonId} on line {LineId} not labeled.",
                exceptionType, cartonId, scan.LineId);
            return null;
        }

        // F11/F12: exception labels go through the same VetLabel + ~HS pipeline as normal labels.
        var zpl = PrepareZpl(context.Config, build.Zpl);
        if (!await StillOwnsAsync(leases, ct).ConfigureAwait(false))
        {
            return InductResult.LockTimeout();
        }

        try
        {
            await _gateway.SendAsync(
                new PrintJob(printer.PrinterId, printer.Ip, printer.Port, build.LabelType, build.Lpn, zpl, null, null, slot),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await StampLastPrintedAsync(printer.PrinterId, _clock.UtcNow, ct).ConfigureAwait(false);
            _logger.LogError(
                ex,
                "Failed to dispatch {Reason} exception label {CartonId} to printer {PrinterId} on line {LineId}.",
                exceptionType, cartonId, printer.PrinterId, scan.LineId);
            return InductResult.NoPrinter();
        }

        await StampLastPrintedAsync(printer.PrinterId, _clock.UtcNow, ct).ConfigureAwait(false);

        // Owner ruling 2026-10-21 — persist the synthetic exception carton like the SQL's exception PandaData
        // row (LookupCarton L509-536: LabelType1='Exception', LabelBarcode1='ExceptionLabel', VerifyEnabled=1),
        // marked printed by this printer, so the verify station finds it and fails it (reason 0) — counting
        // toward the printer's verify-fail threshold and writing reject history.
        var exceptionOrder = TransportOrder.CreateExceptionCarton(
            cartonId,
            scan.LineId,
            new Label(build.LabelType, build.Lpn, zpl),
            printer.PrinterId,
            _clock.UtcNow);
        exceptionOrder.StampInductScan(scan.ToMeasurements());
        exceptionOrder.StampInductStatus(status);

        // Like every TransportOrder writer, the exception-carton insert holds its own carton lease. The id is
        // freshly minted from a unique run id, so the lease is uncontended; the insert enforces unique TuId.
        var exceptionLease = await _locks.AcquireAsync(LockNames.Carton(cartonId), cancellationToken: ct).ConfigureAwait(false);
        await using (exceptionLease.ConfigureAwait(false))
        {
            await _store.InsertAsync(exceptionOrder, ct).ConfigureAwait(false);
        }

        // F-LOG1: record the synthetic exception carton as an audited run (with the reserved run id the carton
        // id was minted from), marked with its induct status.
        {
            var record = CartonRunRecord.Create(
                tuId: cartonId,
                pandaDataId: StableOrderId(cartonId),
                lineId: scan.LineId,
                sorterNumber: scan.SorterNumber,
                sorterMode: scan.SorterMode,
                deviceId: scan.DeviceId,
                seqNum: scan.SeqNum,
                labelStatus: 0,
                scannedLabels: [.. scan.ScannedLabels],
                length: scan.Length,
                width: scan.Width,
                height: scan.Height,
                weight: scan.Weight,
                frontGap: scan.FrontGap,
                statusAtInduct: status,
                assignedPrinter: printer.PrinterId,
                destinationLane: null,
                createdAt: _clock.UtcNow) with { RunId = runId };
            await _runs.CreateRunAsync(record, ct).ConfigureAwait(false);
        }

        // Verify-then-reject (decision-021): the exception carton is treated as a verify Fail and routed to
        // reject via the F08 routing criterion the adapter projects onto the carton.
        var routing = RoutingCriterion.ForCode(VerifyReasonCode.PandaLogicError);
        _logger.LogWarning(
            "Emitted {Reason} exception label {CartonId} to printer {PrinterId} on line {LineId}; carton routed to reject.",
            exceptionType, cartonId, printer.PrinterId, scan.LineId);

        return InductResult.Exception(cartonId, routing);
    }

    /// <summary>
    /// F10 — choose the exception-label printer among the line's printers of <paramref name="orientation"/>
    /// via the injected selection service (same availability filter + load-balance ranking as normal labels).
    /// Exception labels are not in any printer's LabelMap, so each candidate is projected as able to print
    /// the exception type. Returns null when none is available.
    /// </summary>
    private PrinterConfig? SelectExceptionPrinter(
        LineContext context,
        IReadOnlyDictionary<string, PrinterState> states,
        ApplyOrientation orientation)
    {
        var candidates = context.Config.Printers
            .Where(p => p.PrinterType == orientation)
            .ToDictionary(p => p.PrinterId, StringComparer.OrdinalIgnoreCase);

        var view = new LineConfig(
            context.Config.LineId,
            candidates.Values.Select(p => new PrinterConfig(
                p.PrinterId,
                p.Ip,
                p.Port,
                [ExceptionBuildResult.ExceptionLabelType],
                p.PrinterType,
                p.ConfigOrder,
                p.DefaultApplyDistance,
                p.LabelWidthInches)),
            context.Config.LoadBalance);

        var probe = new PandaLabelSet(
            [new Label(ExceptionBuildResult.ExceptionLabelType, ExceptionBuildResult.ExceptionLpn, string.Empty)]);
        var assignment = _selection.Select(view, states, probe).Assignments.FirstOrDefault();

        return assignment is { Status: LabelSelectionStatus.Assigned, PrinterId: { } id }
            && candidates.TryGetValue(id, out var chosen)
            ? chosen
            : null;
    }

    /// <summary>
    /// PROFSW profile selection: a carton with a host-supplied <see cref="TransportOrder.ProfileName"/>
    /// must resolve to an active profile in the line's registry; otherwise the induct is <c>NoProfile</c>.
    /// A carton without a ProfileName falls back to the line's default profile.
    /// </summary>
    private static (bool Ok, FirePointProfile? Profile) ResolveProfile(LineConfig config, TransportOrder order)
    {
        if (order.ProfileName is { } name)
        {
            return config.ProfileRegistry.TryGetValue(name, out var named)
                ? (true, named)
                : (false, null);
        }

        return (true, config.ActiveProfile);
    }

    /// <summary>
    /// F-LOG1 — our TransportOrder is keyed by the string <see cref="TransportOrder.TuId"/>; the source's
    /// run-history groups by the numeric PandaData RecID. Derive a stable non-negative long from the TuId so
    /// repeat runs of the same carton group together in <c>GetRunsForOrderAsync</c>.
    /// </summary>
    internal static long StableOrderId(string tuId)
    {
        // FNV-1a 64-bit; masked to non-negative.
        ulong hash = 1469598103934665603UL;
        foreach (var c in tuId)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }

        return (long)(hash & 0x7FFFFFFFFFFFFFFFUL);
    }
}
