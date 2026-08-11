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
        DateTimeOffset? lastPrinted = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);

        PrinterId = printerId;
        PlcOnline = plcOnline;
        EngineOnline = engineOnline;
        IsSpare = isSpare;
        LastPrinted = lastPrinted;
    }

    public string PrinterId { get; }

    public bool PlcOnline { get; set; }

    public bool EngineOnline { get; set; }

    public bool IsSpare { get; set; }

    /// <summary>Timestamp of the last successful print; null = never printed (oldest, picked first).</summary>
    public DateTimeOffset? LastPrinted { get; set; }

    /// <summary>Eligible for selection when online on both PLC and print engine and not held as spare.</summary>
    public bool IsAvailable => PlcOnline && EngineOnline && !IsSpare;
}
