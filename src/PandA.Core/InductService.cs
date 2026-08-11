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
            await _gateway.SendAsync(
                new PrintJob(printer.PrinterId, printer.Ip, printer.Port, label.LabelType, label.Lpn, label.Zpl),
                cancellationToken).ConfigureAwait(false);

            // Stamp LastPrinted so the next carton rotates (round-robin state).
            if (context.States.TryGetValue(printer.PrinterId, out var state))
            {
                state.LastPrinted = now;
            }
        }

        var result = InductResult.FromAssignments(selection.Assignments);
        if (result.Status is InductStatus.Printed or InductStatus.PartiallyPrinted)
        {
            order.MarkPrinted(now);
            await _store.UpsertAsync(order, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
