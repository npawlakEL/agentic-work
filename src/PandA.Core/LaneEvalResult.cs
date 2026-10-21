namespace PandA.Core;

/// <summary>Line-control outcome of a lane evaluation (architecture-log 012 §6).</summary>
public enum LineControl
{
    /// <summary>Every orientation group meets its minimum; the line runs at full speed.</summary>
    Balanced = 0,

    /// <summary>A group is below minimum but degraded operation is allowed — run the line slow.</summary>
    SlowLine = 1,

    /// <summary>A group is below minimum with no spare left and no degraded fallback — shut the line.</summary>
    ShutLine = 2,

    /// <summary>The conveyor zone is down — the whole line is shut regardless of printer health.</summary>
    ShutZone = 3,
}

/// <summary>Whether a printer was promoted from, or demoted to, the spare pool by lane evaluation.</summary>
public enum SpareChange
{
    PromotedFromSpare = 0,
    DemotedToSpare = 1,
}

/// <summary>A single spare-pool mutation lane evaluation applied to a printer.</summary>
public sealed record PrinterChange(string PrinterId, SpareChange Change);

/// <summary>
/// Result of a lane evaluation: the line-control decision plus the spare-pool changes applied.
/// <see cref="LaneEvalService.EvaluateAsync"/> writes the spare changes through the shared
/// <see cref="IPrinterStateStore"/> under the line's <c>PrinterState_{line}</c> lease; this record reports
/// what happened.
/// The real BluePaw slow/shut egress is a separate (stubbed) concern.
/// </summary>
public sealed record LaneEvalResult(
    LineControl Control,
    IReadOnlyList<PrinterChange> Changes,
    string Reason)
{
    /// <summary>
    /// True when the evaluation was skipped because the line's lease could not be acquired (another host is
    /// evaluating the same line). Nothing was changed; the next printer/zone status event re-evaluates.
    /// </summary>
    public bool Skipped { get; init; }

    /// <summary>A skipped (lease-timeout) evaluation result.</summary>
    public static LaneEvalResult SkippedResult() => new(LineControl.Balanced, [], "skipped") { Skipped = true };
}
