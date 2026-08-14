namespace PandA.Core;

/// <summary>
/// Selects which printer prints each label of a carton, implementing the authoritative algorithm in
/// architecture-log 005: eligibility (online + not spare + LabelMap has the type), per-label-type
/// least-recently-printed round robin, same-carton collision→backup routing, and a configured-order
/// tie-break. Orientation is a physical attribute of the printer, NOT a selection filter — a label
/// routes to whichever printer maps its type regardless of orientation.
/// </summary>
public interface IPrinterSelectionService
{
    /// <param name="line">The line whose printers are candidates.</param>
    /// <param name="states">Runtime state per printer id (online/spare/last-printed).</param>
    /// <param name="labels">The carton's typed label set.</param>
    PrinterSelectionResult Select(
        LineConfig line,
        IReadOnlyDictionary<string, PrinterState> states,
        PandaLabelSet labels);
}
