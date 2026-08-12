namespace PandA.Core;

/// <summary>
/// Mutable runtime health/rotation state for a printer (source PrinterState table). Drives eligibility
/// (online + not spare) and load balancing (LastPrinted). See architecture-log 005.
/// </summary>
public sealed class PrinterState
{
    public PrinterState(
        string printerId,
        bool plcOnline = true,
        bool engineOnline = true,
        bool isSpare = false,
        DateTimeOffset? lastPrinted = null,
        DateTimeOffset? lastStatusUpdate = null,
        int verifyFailCount = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);

        PrinterId = printerId;
        PlcOnline = plcOnline;
        EngineOnline = engineOnline;
        IsSpare = isSpare;
        LastPrinted = lastPrinted;
        LastStatusUpdate = lastStatusUpdate;
        VerifyFailCount = verifyFailCount;
    }

    public string PrinterId { get; }

    public bool PlcOnline { get; set; }

    public bool EngineOnline { get; set; }

    public bool IsSpare { get; set; }

    /// <summary>Timestamp of the last successful print; null = never printed (oldest, picked first).</summary>
    public DateTimeOffset? LastPrinted { get; set; }

    /// <summary>
    /// When PLC/engine/spare status last changed (source <c>LastStatusUpdate</c>). Lane-eval uses the
    /// newest (DESC) spare to promote and the newest active to demote. Null = never updated.
    /// </summary>
    public DateTimeOffset? LastStatusUpdate { get; set; }

    /// <summary>Consecutive verify failures (source <c>VerifyFailCount</c>); drives the per-line pause threshold.</summary>
    public int VerifyFailCount { get; set; }

    /// <summary>Online when up on both the PLC/applicator and the print engine (source PLCStatus AND EngineStatus).</summary>
    public bool IsOnline => PlcOnline && EngineOnline;

    /// <summary>Eligible for selection when online on both PLC and print engine and not held as spare.</summary>
    public bool IsAvailable => PlcOnline && EngineOnline && !IsSpare;
}
