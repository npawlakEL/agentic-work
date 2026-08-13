using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PandA.Core.Settings;

namespace PandA.Core;

/// <summary>
/// MP2 — induct scan. Looks up the active transport order for a scanned blind label, selects a printer for
/// each label (architecture-log 005), dispatches the ZPL, stamps LastPrinted, and marks the order printed.
/// </summary>
public interface IInductService
{
    ValueTask<InductResult> InductAsync(
        string lineId,
        string blindLabel,
        CancellationToken cancellationToken = default);
}

public sealed class InductService : IInductService
{
    private readonly ITransportOrderStore _store;
    private readonly ILineProvider _lines;
    private readonly IPrinterSelectionService _selection;
    private readonly IPrinterGateway _gateway;
    private readonly IClock _clock;
    private readonly ISettingsProvider _settings;
    private readonly ILogger<InductService> _logger;
    private readonly FirePointResolver _firePoints = new();

    public InductService(
        ITransportOrderStore store,
        ILineProvider lines,
        IPrinterSelectionService selection,
        IPrinterGateway gateway,
        IClock clock,
        ISettingsProvider settings,
        ILogger<InductService>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _lines = lines ?? throw new ArgumentNullException(nameof(lines));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? NullLogger<InductService>.Instance;
    }

    public async ValueTask<InductResult> InductAsync(
        string lineId,
        string blindLabel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(blindLabel);

        _ = _settings;

        var order = await _store.FindActiveByTuIdAsync(blindLabel, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            _logger.LogWarning("No active transport order for induct scan {TuId} on line {LineId}.", blindLabel, lineId);
            return InductResult.NoActiveOrder();
        }

        if (order.Labels.Labels.Count == 0)
        {
            _logger.LogWarning("Transport order {TuId} on line {LineId} has no label data.", order.TuId, lineId);
            return InductResult.NoData();
        }

        // F20: read-quality classification here.
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

        foreach (var assignment in selection.Assignments)
        {
            if (assignment.Status != LabelSelectionStatus.Assigned || assignment.PrinterId is null)
            {
                continue;
            }

            var printer = printersById[assignment.PrinterId];
            var label = assignment.Label;

            // Resolve the print/apply firing points from the carton's resolved fire-point profile (if any).
            FirePoint? firePoint = null;
            if (activeProfile is { } profile)
            {
                var resolution = _firePoints.Resolve(profile, printer.PrinterId, label.LabelType);
                firePoint = resolution.FirePoint;
            }

            await _gateway.SendAsync(
                new PrintJob(printer.PrinterId, printer.Ip, printer.Port, label.LabelType, label.Lpn, label.Zpl, firePoint),
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
}
