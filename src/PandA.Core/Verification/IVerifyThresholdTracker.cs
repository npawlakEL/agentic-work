namespace PandA.Core.Verification;

/// <summary>Outcome of registering a verify result against the consecutive-fail counter(s).</summary>
/// <param name="ConsecutiveFailures">The current consecutive-fail count after this result (for a multi-printer
/// registration, the highest count among the carton's printers — the streak that tripped, if any).</param>
/// <param name="PausePrinter">
/// True when a count has reached the threshold and the printer should be paused (source <c>~PP</c>). The
/// pause egress itself is a connector concern (deferred).
/// </param>
/// <param name="PrintersToPause">The printer ids whose streak tripped the threshold on this result.</param>
public sealed record VerifyThresholdResult(
    int ConsecutiveFailures,
    bool PausePrinter,
    IReadOnlyList<string>? PrintersToPause = null)
{
    /// <summary>The printer ids to pause (never null).</summary>
    public IReadOnlyList<string> PrintersToPause { get; init; } = PrintersToPause ?? [];
}

/// <summary>
/// Tracks <b>consecutive</b> verify failures per key and signals when a key has failed enough times in a
/// row to warrant pausing the printer. Port of <c>sdisp_PA_VerifyThreshold_Update</c> (architecture-log 006
/// §D). The source keys the streak by <b>printer</b> (<c>PrinterState.VerifyFailCount</c>) for every printer
/// that printed the carton (owner ruling 2026-10-21); a pass resets the streak.
/// </summary>
public interface IVerifyThresholdTracker
{
    /// <summary>
    /// Register a verify result for one key (a printer id).
    /// </summary>
    /// <param name="lineId">The counter key (a printer id; historically the line id).</param>
    /// <param name="pass">True if the carton passed verification.</param>
    /// <param name="failThreshold">
    /// Consecutive-fail count at which the printer should pause. Values &lt;= 0 disable pausing.
    /// </param>
    VerifyThresholdResult Register(string lineId, bool pass, int failThreshold);

    /// <summary>
    /// Register a verify result for every printer that printed the carton (source cursor over the
    /// comma-separated <c>@PrinterID</c> list): a fail increments each printer's streak, a pass resets it,
    /// and each printer whose streak reaches the threshold is returned in
    /// <see cref="VerifyThresholdResult.PrintersToPause"/>.
    /// </summary>
    VerifyThresholdResult RegisterPrinters(IReadOnlyCollection<string> printerIds, bool pass, int failThreshold)
    {
        ArgumentNullException.ThrowIfNull(printerIds);
        var max = 0;
        var pause = new List<string>();
        foreach (var printerId in printerIds)
        {
            var result = Register(printerId, pass, failThreshold);
            max = Math.Max(max, result.ConsecutiveFailures);
            if (result.PausePrinter)
            {
                pause.Add(printerId);
            }
        }

        return new VerifyThresholdResult(max, pause.Count > 0, pause);
    }

    /// <summary>The current consecutive-fail count for a key (0 if unknown).</summary>
    int CurrentCount(string lineId);

    /// <summary>Clear a key's consecutive-fail count.</summary>
    void Reset(string lineId);
}