namespace PandA.Core;

/// <summary>
/// Per-line, per-orientation printer thresholds (source <c>PandADetails</c> attributes
/// <c>OnlinePrinterMin_&lt;T&gt;</c> / <c>PrinterCount_&lt;T&gt;</c>). Drives lane evaluation:
/// keep exactly <see cref="OnlineMin"/> usable printers of this orientation online, parking any
/// surplus as spares. See architecture-log 012.
/// <para>
/// <see cref="AllowDegraded"/> + <see cref="SlowLineFloor"/> generalize the source's hard-coded
/// "2 Printer Rule": when the group can't meet <see cref="OnlineMin"/> and has no spare to promote,
/// keep the line running slow (degraded) so long as usable ≥ <see cref="SlowLineFloor"/>, instead of
/// shutting it down. A group with PrinterCount=2, OnlineMin=2, SlowLineFloor=1, AllowDegraded=true
/// reproduces the exact source behavior.
/// </para>
/// </summary>
public sealed class PrinterGroupPolicy
{
    public PrinterGroupPolicy(
        int onlineMin,
        int printerCount,
        int slowLineFloor = 1,
        bool allowDegraded = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(onlineMin);
        ArgumentOutOfRangeException.ThrowIfNegative(printerCount);
        ArgumentOutOfRangeException.ThrowIfNegative(slowLineFloor);

        OnlineMin = onlineMin;
        PrinterCount = printerCount;
        SlowLineFloor = slowLineFloor;
        AllowDegraded = allowDegraded;
    }

    /// <summary>Usable printers required for full-speed running (source <c>OnlinePrinterMin_&lt;T&gt;</c>).</summary>
    public int OnlineMin { get; }

    /// <summary>Installed printers of this orientation (source <c>PrinterCount_&lt;T&gt;</c>).</summary>
    public int PrinterCount { get; }

    /// <summary>Minimum usable printers to keep running degraded (slow line) when below <see cref="OnlineMin"/>.</summary>
    public int SlowLineFloor { get; }

    /// <summary>When true, the customer opts this group into slow-line operation instead of a hard shutdown.</summary>
    public bool AllowDegraded { get; }
}
