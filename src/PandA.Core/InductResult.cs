namespace PandA.Core;

public enum InductStatus
{
    /// <summary>Every label was dispatched to a printer.</summary>
    Printed = 0,

    /// <summary>Some labels printed; at least one had no eligible printer.</summary>
    PartiallyPrinted = 1,

    /// <summary>No label could be printed (no eligible printer for any type).</summary>
    NoPrinter = 2,

    /// <summary>A transport order exists but has no labels to print.</summary>
    NoData = 3,

    /// <summary>No active transport order matched the scanned blind label.</summary>
    NoActiveOrder = 4,
}

/// <summary>Outcome of an induct scan: overall status plus the per-label selection assignments.</summary>
public sealed class InductResult
{
    private InductResult(InductStatus status, IReadOnlyList<LabelAssignment> assignments)
    {
        Status = status;
        Assignments = assignments;
    }

    public InductStatus Status { get; }

    public IReadOnlyList<LabelAssignment> Assignments { get; }

    public static InductResult NoActiveOrder() => new(InductStatus.NoActiveOrder, []);

    public static InductResult NoData() => new(InductStatus.NoData, []);

    public static InductResult FromAssignments(IReadOnlyList<LabelAssignment> assignments)
    {
        var printed = assignments.Count(a => a.Status == LabelSelectionStatus.Assigned);
        var status = printed == assignments.Count ? InductStatus.Printed
            : printed == 0 ? InductStatus.NoPrinter
            : InductStatus.PartiallyPrinted;
        return new InductResult(status, assignments);
    }
}
