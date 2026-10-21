using PandA.Core.Induct;
using PandA.Core.Verification;

namespace PandA.Core;

public enum InductStatus
{
    /// <summary>Every label was dispatched to a printer.</summary>
    Printed = 0,

    /// <summary>Some labels printed; at least one had no eligible printer or failed to dispatch.</summary>
    PartiallyPrinted = 1,

    /// <summary>No label could be printed (no eligible printer for any type, or every dispatch failed).</summary>
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

    /// <summary>
    /// The induct read was not print-ready (no read, no data, label conflict, gap error) and no exception
    /// label was emitted. Source <c>sdisp_PA_Scan_Induct</c> NoPrintFlag: the carton's real labels never
    /// print on a non-PrintReady read, even when an order resolves (owner ruling 2026-10-21).
    /// </summary>
    NotPrintReady = 8,

    /// <summary>
    /// The resolved order is flagged <c>Bypass</c> (source LookupCarton L392 → status Bypass, NoPrintFlag).
    /// Nothing is printed and no exception label is emitted (owner ruling 2026-10-21).
    /// </summary>
    Bypass = 9,

    /// <summary>
    /// The shared carton lease (decision-010) could not be acquired within the timeout — another host is
    /// processing the same carton. Fail safe: nothing was printed and nothing was persisted.
    /// </summary>
    LockTimeout = 10,
}

/// <summary>Outcome of an induct scan: overall status plus the per-label selection assignments.</summary>
public sealed class InductResult
{
    private InductResult(
        InductStatus status,
        IReadOnlyList<LabelAssignment> assignments,
        string? exceptionCartonId = null,
        RoutingCriterion? routing = null,
        CartonStatus? classification = null)
    {
        Status = status;
        Assignments = assignments;
        ExceptionCartonId = exceptionCartonId;
        Routing = routing;
        Classification = classification;
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

    /// <summary>The F20 read-quality classification of the induct scan, when it was not print-ready.</summary>
    public CartonStatus? Classification { get; }

    /// <summary>
    /// The PLC carton-assign slot (source <c>sdisp_TOOL_GetSlotNumber</c>, 1..300 cycle) drawn for this
    /// induct; assigned on every induct, including no-print outcomes (owner ruling 2026-10-21).
    /// </summary>
    public int? Slot { get; private init; }

    /// <summary>Copy of this result carrying the assigned PLC <paramref name="slot"/>.</summary>
    public InductResult WithSlot(int? slot) =>
        new(Status, Assignments, ExceptionCartonId, Routing, Classification) { Slot = slot };

    public static InductResult NoActiveOrder() => new(InductStatus.NoActiveOrder, []);

    /// <summary>The carton lease could not be acquired (another host holds it); nothing printed.</summary>
    public static InductResult LockTimeout() => new(InductStatus.LockTimeout, []);

    /// <summary>The scan was not print-ready (<paramref name="classification"/>); nothing printed.</summary>
    public static InductResult NotPrintReady(CartonStatus classification) =>
        new(InductStatus.NotPrintReady, [], classification: classification);

    /// <summary>The order is flagged Bypass; nothing printed.</summary>
    public static InductResult Bypass() => new(InductStatus.Bypass, []);

    public static InductResult NoData() => new(InductStatus.NoData, []);

    public static InductResult NoReprint() => new(InductStatus.NoReprint, []);

    public static InductResult NoProfile() => new(InductStatus.NoProfile, []);

    /// <summary>Nothing could be printed: no available printer (or every dispatch failed).</summary>
    public static InductResult NoPrinter() => new(InductStatus.NoPrinter, []);

    /// <summary>F10 — an exception label was printed against <paramref name="cartonId"/> and routed by <paramref name="routing"/>.</summary>
    public static InductResult Exception(string cartonId, RoutingCriterion routing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cartonId);
        ArgumentNullException.ThrowIfNull(routing);
        return new(InductStatus.ExceptionLabel, [], cartonId, routing);
    }

    public static InductResult FromAssignments(IReadOnlyList<LabelAssignment> assignments)
    {
        // A label skipped for a missing/neglect fire point (SQL ErrorCode 2) counts as not printed.
        var printed = assignments.Count(a => a.Status == LabelSelectionStatus.Assigned);
        var status = printed == assignments.Count ? InductStatus.Printed
            : printed == 0 ? InductStatus.NoPrinter
            : InductStatus.PartiallyPrinted;
        return new InductResult(status, assignments);
    }
}
