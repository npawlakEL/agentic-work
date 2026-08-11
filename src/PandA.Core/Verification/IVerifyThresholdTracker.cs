namespace PandA.Core.Verification;

/// <summary>Outcome of registering a verify result against a line's consecutive-fail counter.</summary>
/// <param name="ConsecutiveFailures">The line's current consecutive-fail count after this result.</param>
/// <param name="PausePrinter">
/// True when the count has reached the line's threshold and the printer should be paused
/// (source <c>~PP</c>). The pause egress itself is a connector concern (deferred).
/// </param>
public sealed record VerifyThresholdResult(int ConsecutiveFailures, bool PausePrinter);

/// <summary>
/// Tracks <b>consecutive</b> verify failures per line and signals when a line has failed enough times
/// in a row to warrant pausing the printer. Port of <c>sdisp_PA_VerifyThreshold_Update</c>
/// (architecture-log 006 §D). A pass resets the streak.
/// </summary>
public interface IVerifyThresholdTracker
{
    /// <summary>
    /// Register a verify result for a line.
    /// </summary>
    /// <param name="lineId">The line identifier.</param>
    /// <param name="pass">True if the carton passed verification.</param>
    /// <param name="failThreshold">
    /// Consecutive-fail count at which the printer should pause. Values &lt;= 0 disable pausing.
    /// </param>
    VerifyThresholdResult Register(string lineId, bool pass, int failThreshold);

    /// <summary>The current consecutive-fail count for a line (0 if unknown).</summary>
    int CurrentCount(string lineId);

    /// <summary>Clear a line's consecutive-fail count.</summary>
    void Reset(string lineId);
}
