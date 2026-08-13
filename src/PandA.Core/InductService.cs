using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PandA.Core.Induct;
using PandA.Core.Settings;

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
    private readonly ILogger<InductService> _logger;
    private readonly FirePointResolver _firePoints = new();

    public InductService(
        ITransportOrderStore store,
        ILineProvider lines,
        IPrinterSelectionService selection,
        IPrinterGateway gateway,
        IClock clock,
        ISettingsProvider settings,
        IMinGapProvider? minGap = null,
        ICartonRunRepository? runs = null,
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

        // Orientation is a provisioned dimension; Phase 1 uses the default (Side). See architecture-log 005.
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
            if (activeProfile is { } profile)
            {
                var resolution = _firePoints.Resolve(profile, printer.PrinterId, label.LabelType);
                firePoint = resolution.FirePoint;
            }

            // F12 (decision-014): request Zebra host status by appending ~HS when the line opts in.
            var zpl = context.Config.PrinterStatusSuffix ? ZplStatusSuffix.Append(label.Zpl) : label.Zpl;

            await _gateway.SendAsync(
                new PrintJob(printer.PrinterId, printer.Ip, printer.Port, label.LabelType, label.Lpn, zpl, firePoint),
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
        // DYNAP: dynamic apply-point calculation here.
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
