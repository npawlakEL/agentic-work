namespace PandA.Core;

public enum LabelSelectionStatus
{
    Assigned = 0,

    /// <summary>No eligible printer serves this label's type/orientation (source: skip label, not fail carton).</summary>
    NoPrinter = 1,
}

/// <summary>The printer chosen for one label of a carton (or none).</summary>
public sealed record LabelAssignment(Label Label, string? PrinterId, LabelSelectionStatus Status)
{
    public static LabelAssignment Assigned(Label label, string printerId) =>
        new(label, printerId, LabelSelectionStatus.Assigned);

    public static LabelAssignment NoPrinter(Label label) =>
        new(label, null, LabelSelectionStatus.NoPrinter);
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
