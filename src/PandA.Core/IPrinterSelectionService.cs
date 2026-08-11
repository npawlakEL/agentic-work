namespace PandA.Core;

/// <summary>
/// Selects which printer prints each label of a carton, implementing the authoritative algorithm in
/// architecture-log 005: eligibility (online + not spare + LabelMap has the type + orientation),
/// per-label-type least-recently-printed round robin, same-carton collision→backup routing, and a
/// configured-order tie-break.
/// </summary>
public interface IPrinterSelectionService
{
    /// <param name="line">The line whose printers are candidates.</param>
    /// <param name="states">Runtime state per printer id (online/spare/last-printed).</param>
    /// <param name="labels">The carton's typed label set.</param>
    /// <param name="orientation">Required apply orientation for this carton (default Side).</param>
    PrinterSelectionResult Select(
        LineConfig line,
        IReadOnlyDictionary<string, PrinterState> states,
        PandaLabelSet labels,
        ApplyOrientation orientation = ApplyOrientation.Side);
}
