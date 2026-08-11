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
    private readonly FirePointResolver _firePoints = new();

    public InductService(
        ITransportOrderStore store,
        ILineProvider lines,
        IPrinterSelectionService selection,
        IPrinterGateway gateway,
        IClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _lines = lines ?? throw new ArgumentNullException(nameof(lines));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async ValueTask<InductResult> InductAsync(
        string lineId,
        string blindLabel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(blindLabel);

        var order = await _store.FindActiveByTuIdAsync(blindLabel, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return InductResult.NoActiveOrder();
        }

        if (order.Labels.Labels.Count == 0)
        {
            return InductResult.NoData();
        }

        // Reprint policy (decision-003): an already-printed carton may only reprint when an operator has
        // authorized it. Otherwise nothing is printed.
        if (!order.CanPrint)
        {
            return InductResult.NoReprint();
        }

        var context = await _lines.GetLineAsync(lineId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No line configuration for line '{lineId}'.");

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

            // Resolve the print/apply firing points from the line's active fire-point profile (if any).
            FirePoint? firePoint = null;
            if (context.Config.ActiveProfile is { } profile)
            {
                var resolution = _firePoints.Resolve(profile, printer.PrinterId, label.LabelType);
                firePoint = resolution.FirePoint;
            }

            await _gateway.SendAsync(
                new PrintJob(printer.PrinterId, printer.Ip, printer.Port, label.LabelType, label.Lpn, label.Zpl, firePoint),
                cancellationToken).ConfigureAwait(false);

            // Stamp LastPrinted so the next carton rotates (round-robin state).
            if (context.States.TryGetValue(printer.PrinterId, out var state))
            {
                state.LastPrinted = now;
            }

            // Record which label type printed, to which printer (per-label outcome; decision-003).
            order.MarkLabelPrinted(label.LabelType, printer.PrinterId, now);
        }

        var result = InductResult.FromAssignments(selection.Assignments);

        // Only a FULL run counts as a print run and increments the monotonic counter (decision-003).
        if (result.Status == InductStatus.Printed)
        {
            order.CompletePrintRun(now);
            await _store.UpsertAsync(order, cancellationToken).ConfigureAwait(false);
        }
        else if (result.Status == InductStatus.PartiallyPrinted)
        {
            // Persist per-label print state, but do not count the run or advance status.
            await _store.UpsertAsync(order, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
