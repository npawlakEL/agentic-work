using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PandA.Core.Induct;
using PandA.Core.Labels;
using PandA.Core.Ports;
using PandA.Core.Settings;
using PandA.Core.Verification;

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

public sealed class InductService : IInductService
{
    private readonly ITransportOrderStore _store;
    private readonly ILineProvider _lines;
    private readonly IPrinterSelectionService _selection;
    private readonly IPrinterGateway _gateway;
    private readonly IClock _clock;
    private readonly ISettingsProvider _settings;
    private readonly IMinGapProvider? _minGap;
    private readonly ICartonRunRepository? _runs;
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
        IMinGapProvider? minGap = null,
        ICartonRunRepository? runs = null,
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
        _minGap = minGap;
        _runs = runs;
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
        var lineId = scan.LineId;
        var blindLabel = scan.BlindLabel;
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(blindLabel);

        _ = _settings;

        var order = await _store.FindActiveByTuIdAsync(blindLabel, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            // F18: a non-BL scan (oLPN/UPC/…) doesn't match a blind label directly; resolve it through the
            // barcode cross-reference populated at advice time.
            order = await ResolveByBarcodeAsync(scan, cancellationToken).ConfigureAwait(false);
        }

        if (order is null)
        {
            // F10 (decision-021): an unmatched carton with exceptions enabled gets a locally-generated
            // exception label against a synthesized identity, then routes to reject at verify.
            var exception = await TryEmitExceptionLabelAsync(scan, cancellationToken).ConfigureAwait(false);
            if (exception is not null)
            {
                return exception;
            }

            _logger.LogWarning("No active transport order for induct scan {TuId} on line {LineId}.", blindLabel, lineId);
            return InductResult.NoActiveOrder();
        }

        // Wave-0 inbound foundation: stamp the carton with its physical induct measurements so downstream
        // features (F-LOG1/F20/DYNAP) can read them. Pure capture; no gating behavior yet (decision-020).
        order.StampInductScan(scan.ToMeasurements());

        // F20: classify read-quality from the blind-label markers + front gap. Stamped for run-history
        // (F-LOG1) and the exception trigger (F10); does not gate the print decision on its own here.
        var inductStatus = InductQualityClassifier.Classify(blindLabel, scan.FrontGap, _minGap?.GetMinGap() ?? 0);
        order.StampInductStatus(inductStatus);
        if (inductStatus != CartonStatus.PrintReady)
        {
            _logger.LogInformation(
                "Induct scan {TuId} on line {LineId} classified {Status} (front gap {FrontGap}).",
                blindLabel, lineId, inductStatus, scan.FrontGap);
        }

        // F-LOG1: record this carton pass through the induct scanner for run-history auditing. The assigned
        // printer is filled in later (UpdatePrinterAsync) once a printer is matched in the print loop.
        long? runId = null;
        if (_runs is { } runs)
        {
            var record = CartonRunRecord.Create(
                tuId: order.TuId,
                pandaDataId: StableOrderId(order.TuId),
                lineId: lineId,
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
            runId = await runs.CreateRunAsync(record, cancellationToken).ConfigureAwait(false);
        }

        if (order.Labels.Labels.Count == 0)
        {
            _logger.LogWarning("Transport order {TuId} on line {LineId} has no label data.", order.TuId, lineId);
            return InductResult.NoData();
        }

        // Reprint policy (decision-003): an already-printed carton may only reprint when an operator has
        // authorized it. Otherwise nothing is printed.
        if (!order.CanPrint)
        {
            _logger.LogInformation(
                "Transport order {TuId} on line {LineId} is not eligible for reprint. Status {Status}, print count {PrintCount}.",
                order.TuId, lineId, order.Status, order.PrintCount);
            return InductResult.NoReprint();
        }

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

        // Selection is orientation-agnostic: each label routes to whatever printer maps to its type,
        // so a carton's side (Shipping) and top (Content) labels each reach their own printer.
        var selection = _selection.Select(context.Config, context.States, order.Labels);

        var now = _clock.UtcNow;
        var printersById = context.Config.Printers.ToDictionary(p => p.PrinterId, StringComparer.OrdinalIgnoreCase);
        var runPrinterRecorded = false;

        foreach (var assignment in selection.Assignments)
        {
            if (assignment.Status != LabelSelectionStatus.Assigned || assignment.PrinterId is null)
            {
                continue;
            }

            var printer = printersById[assignment.PrinterId];
            var label = assignment.Label;

            // F-LOG1: attach the first matched printer to the run-history record for this carton.
            if (runId is { } id && !runPrinterRecorded && _runs is { } runsRepo)
            {
                await runsRepo.UpdatePrinterAsync(id, printer.PrinterId, cancellationToken).ConfigureAwait(false);
                runPrinterRecorded = true;
            }

            // Resolve the print/apply firing points from the carton's resolved fire-point profile (if any).
            FirePoint? firePoint = null;
            int? applyPulse = null;
            if (activeProfile is { } profile)
            {
                var resolution = _firePoints.Resolve(profile, printer.PrinterId, label.LabelType);
                firePoint = resolution.FirePoint;

                // DYNAP (decision-016): convert the human APPLY point (inch/edge) into a carton-aware PLC
                // pulse when the carton's physical dimensions are known. PRINT point stays static. Top-apply
                // additionally needs tamp kinematics (not yet commissioned) so it is left null for now.
                if (firePoint is { } fp
                    && order.InductMeasurements is { } m
                    && m.Length > 0
                    && printer.PrinterType == ApplyOrientation.Side)
                {
                    var applyConfig = new ApplyPointConfig(EncoderResolution: context.Config.EncoderResolution);
                    var dims = new CartonDimensions(m.Length, m.Height);
                    applyPulse = _applyPoints.Resolve(fp.ApplyFirePoint, ApplyOrientation.Side, dims, applyConfig);
                }
            }

            // F12 (decision-014): request Zebra host status by appending ~HS when the line opts in.
            var zpl = context.Config.PrinterStatusSuffix ? ZplStatusSuffix.Append(label.Zpl) : label.Zpl;

            await _gateway.SendAsync(
                new PrintJob(printer.PrinterId, printer.Ip, printer.Port, label.LabelType, label.Lpn, zpl, firePoint, applyPulse),
                cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Dispatched label {LabelType} for transport order {TuId} to printer {PrinterId} on line {LineId}.",
                label.LabelType, order.TuId, printer.PrinterId, lineId);

            // Stamp LastPrinted so the next carton rotates (round-robin state).
            if (context.States.TryGetValue(printer.PrinterId, out var state))
            {
                state.LastPrinted = now;
            }

            // Record which label type printed, to which printer (per-label outcome; decision-003).
            order.MarkLabelPrinted(label.LabelType, printer.PrinterId, now);
        }

        var result = InductResult.FromAssignments(selection.Assignments);
        // F08: routing here.

        // Only a FULL run counts as a print run and increments the monotonic counter (decision-003).
        if (result.Status == InductStatus.Printed)
        {
            order.CompletePrintRun(now);
            await _store.UpsertAsync(order, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Completed print run for transport order {TuId} on line {LineId}; print count is {PrintCount}.",
                order.TuId, lineId, order.PrintCount);
        }
        else if (result.Status == InductStatus.PartiallyPrinted)
        {
            // Persist per-label print state, but do not count the run or advance status.
            await _store.UpsertAsync(order, cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "Partially printed transport order {TuId} on line {LineId}.",
                order.TuId, lineId);
        }

        return result;
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
    /// human-readable id (<c>{Reason}-{seq}</c>), builds the ZPL from the injected
    /// <see cref="IExceptionLabelSource"/> (LocalTemplate by default; DCMS/eHub via the adapter), prints it to
    /// a printer of the line's apply orientation (Side if any side printer, else Top), records the synthetic
    /// carton as printed (F-LOG1), and returns a verify-then-reject routing criterion (F08). Returns
    /// <c>null</c> when exceptions are disabled, no source is wired, or the read yields no exception reason.
    /// </summary>
    private async ValueTask<InductResult?> TryEmitExceptionLabelAsync(InductScan scan, CancellationToken ct)
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

        var status = InductQualityClassifier.Classify(scan.BlindLabel, scan.FrontGap, _minGap?.GetMinGap() ?? 0);
        if (status == CartonStatus.Bypass || ExceptionLabelPolicy.Classify(status) is not { } exceptionType)
        {
            return null;
        }

        // Orientation: side if the line has any side-apply printer, otherwise top.
        var orientation = context.Config.Printers.Any(p => p.PrinterType == ApplyOrientation.Side)
            ? ApplyOrientation.Side
            : ApplyOrientation.Top;

        var printer = context.Config.Printers.FirstOrDefault(p => p.PrinterType == orientation);
        if (printer is null)
        {
            _logger.LogWarning(
                "Exception carton on line {LineId} has no {Orientation} printer; label not emitted.",
                scan.LineId, orientation);
            return null;
        }

        var cartonId = ExceptionLabelPolicy.MintCartonId(exceptionType, scan.SeqNum);
        var lpn = ExceptionLabelPolicy.UsesLpn(exceptionType) ? scan.BlindLabel : null;

        var build = await source.BuildAsync(exceptionType, cartonId, lpn, ct).ConfigureAwait(false);
        if (!build.HasLabel || build.Zpl is null)
        {
            _logger.LogWarning(
                "No active exception template for {Reason}; carton {CartonId} on line {LineId} not labeled.",
                exceptionType, cartonId, scan.LineId);
            return null;
        }

        await _gateway.SendAsync(
            new PrintJob(printer.PrinterId, printer.Ip, printer.Port, build.LabelType, build.Lpn, build.Zpl, null, null),
            ct).ConfigureAwait(false);

        // F-LOG1: record the synthetic exception carton as an audited run, marked with its induct status.
        if (_runs is { } runs)
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
                createdAt: _clock.UtcNow);
            await runs.CreateRunAsync(record, ct).ConfigureAwait(false);
        }

        // Verify-then-reject (decision-021): the exception carton is treated as a verify Fail and routed to
        // reject via the F08 routing criterion the adapter projects onto the carton.
        var routing = new RoutingCriterion(RoutingCriterion.PandaVerifyType, "Fail");
        _logger.LogWarning(
            "Emitted {Reason} exception label {CartonId} to printer {PrinterId} on line {LineId}; carton routed to reject.",
            exceptionType, cartonId, printer.PrinterId, scan.LineId);

        return InductResult.Exception(cartonId, routing);
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
    private static long StableOrderId(string tuId)
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
