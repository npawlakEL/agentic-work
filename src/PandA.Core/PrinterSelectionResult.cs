namespace PandA.Core;

public enum LabelSelectionStatus
{
    Assigned = 0,

    /// <summary>No eligible printer maps this label's type (source: skip label, not fail carton).</summary>
    NoPrinter = 1,

    /// <summary>A printer was selected but dispatching the label to it failed; the label is not printed.</summary>
    DispatchFailed = 2,

    /// <summary>
    /// The line's fire-point profile has no fire point for this printer/label (or it is a NeglectPrint
    /// <c>0…</c> point): source <c>sdisp_PA2BP_SendPrinterFirePoints</c> ErrorCode 2 — the label is skipped,
    /// not printed (owner ruling 2026-10-21).
    /// </summary>
    NoFirePoint = 3,
}

/// <summary>The printer chosen for one label of a carton (or none).</summary>
public sealed record LabelAssignment(Label Label, string? PrinterId, LabelSelectionStatus Status)
{
    public static LabelAssignment Assigned(Label label, string printerId) =>
        new(label, printerId, LabelSelectionStatus.Assigned);

    public static LabelAssignment NoPrinter(Label label) =>
        new(label, null, LabelSelectionStatus.NoPrinter);

    public static LabelAssignment DispatchFailed(Label label, string printerId) =>
        new(label, printerId, LabelSelectionStatus.DispatchFailed);

    public static LabelAssignment NoFirePoint(Label label, string printerId) =>
        new(label, printerId, LabelSelectionStatus.NoFirePoint);
}

/// <summary>Outcome of selecting printers for every label in a carton's <see cref="PandaLabelSet"/>.</summary>
public sealed class PrinterSelectionResult
{
    public PrinterSelectionResult(IEnumerable<LabelAssignment> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        Assignments = [.. assignments];
    }

    public IReadOnlyList<LabelAssignment> Assignments { get; }

    public bool AllAssigned => Assignments.All(a => a.Status == LabelSelectionStatus.Assigned);
}
