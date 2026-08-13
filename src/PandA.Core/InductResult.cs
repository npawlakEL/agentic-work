using PandA.Core.Verification;

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

    /// <summary>
    /// The carton has already been printed and no reprint is authorized (decision-003 reprint policy).
    /// Nothing was printed.
    /// </summary>
    NoReprint = 5,

    /// <summary>
    /// The carton's host-supplied <c>ProfileName</c> was set but is not among the line's active profiles
    /// (GAP F19 / PROFSW). Nothing was printed.
    /// </summary>
    NoProfile = 6,

    /// <summary>
    /// F10 (decision-021): no active order matched, so a locally-generated exception label was printed
    /// against a synthesized carton id. The carton is marked printed and routed to reject at verify.
    /// </summary>
    ExceptionLabel = 7,
}

/// <summary>Outcome of an induct scan: overall status plus the per-label selection assignments.</summary>
public sealed class InductResult
{
    private InductResult(
        InductStatus status,
        IReadOnlyList<LabelAssignment> assignments,
        string? exceptionCartonId = null,
        RoutingCriterion? routing = null)
    {
        Status = status;
        Assignments = assignments;
        ExceptionCartonId = exceptionCartonId;
        Routing = routing;
    }

    public InductStatus Status { get; }

    public IReadOnlyList<LabelAssignment> Assignments { get; }

    /// <summary>F10 — the synthesized carton id an exception label was printed against (else <c>null</c>).</summary>
    public string? ExceptionCartonId { get; }

    /// <summary>
    /// The routing criterion the adapter projects onto the carton (decision-008). Populated for exception
    /// labels (verify-then-reject → <c>"Fail"</c>); <c>null</c> for normal induct outcomes.
    /// </summary>
    public RoutingCriterion? Routing { get; }

    public static InductResult NoActiveOrder() => new(InductStatus.NoActiveOrder, []);

    public static InductResult NoData() => new(InductStatus.NoData, []);

    public static InductResult NoReprint() => new(InductStatus.NoReprint, []);

    public static InductResult NoProfile() => new(InductStatus.NoProfile, []);

    /// <summary>F10 — an exception label was printed against <paramref name="cartonId"/> and routed by <paramref name="routing"/>.</summary>
    public static InductResult Exception(string cartonId, RoutingCriterion routing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartonId);
        ArgumentNullException.ThrowIfNull(routing);
        return new(InductStatus.ExceptionLabel, [], cartonId, routing);
    }

    public static InductResult FromAssignments(IReadOnlyList<LabelAssignment> assignments)
    {
        var printed = assignments.Count(a => a.Status == LabelSelectionStatus.Assigned);
        var status = printed == assignments.Count ? InductStatus.Printed
            : printed == 0 ? InductStatus.NoPrinter
            : InductStatus.PartiallyPrinted;
        return new InductResult(status, assignments);
    }
}
