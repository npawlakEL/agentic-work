namespace PandA.Core.Verification;

/// <summary>Station-level outcome of a verify scan, tying the pure verify result to the carton lifecycle.</summary>
public enum VerifyStationStatus
{
    /// <summary>Verify passed (or was bypassed); the carton is marked verified and may proceed.</summary>
    Verified = 0,

    /// <summary>Verify failed; the carton is held for manual intervention (decision-003; no auto re-arm).</summary>
    HeldForIntervention = 1,

    /// <summary>No active transport order matched the scanned blind label.</summary>
    NoActiveOrder = 2,
}

/// <summary>Result of a verify-station scan.</summary>
/// <param name="Status">What happened to the carton.</param>
/// <param name="Verify">The underlying verify result (null when no order matched).</param>
/// <param name="PrinterPaused">True if this result tripped the line's consecutive-fail threshold.</param>
/// <param name="ConsecutiveFailures">The line's consecutive-fail count after this scan.</param>
/// <param name="Routing">
/// The routing criterion the adapter projects onto econtroller <c>SortCriteriaExtension</c> (decision-008);
/// null when no order matched.
/// </param>
public sealed record VerifyStationResult(
    VerifyStationStatus Status,
    VerifyResult? Verify,
    bool PrinterPaused,
    int ConsecutiveFailures,
    RoutingCriterion? Routing = null);
