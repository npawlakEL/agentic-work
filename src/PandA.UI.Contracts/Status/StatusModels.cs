namespace PandA.UI.Contracts.Status;

/// <summary>Coarse running state of a line, shown on the Status Dashboard.</summary>
public enum LineRunState
{
    Running,
    Slow,
    Stopped,
}

/// <summary>Live health snapshot for one line.</summary>
/// <param name="LineId">Line identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="RunState">Running / slow / stopped.</param>
/// <param name="Reason">Why it is slow/stopped (empty when running).</param>
/// <param name="ActiveMapName">The currently active fire-point Map.</param>
/// <param name="PrinterCount">Printers configured on the line.</param>
/// <param name="OnlinePrinterCount">Printers currently online.</param>
public sealed record LineStatus(
    string LineId,
    string Name,
    LineRunState RunState,
    string Reason,
    string ActiveMapName,
    int PrinterCount,
    int OnlinePrinterCount);

/// <summary>Live health snapshot for one printer.</summary>
/// <param name="PrinterId">Printer identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="LineId">Owning line.</param>
/// <param name="Online">True when online and reachable.</param>
/// <param name="IsSpare">True when currently acting as a spare.</param>
/// <param name="Orientation">Apply orientation (Side/Top).</param>
/// <param name="VerifyFailCount">Consecutive verify failures.</param>
/// <param name="VerifyFailThreshold">Threshold at which the line pauses.</param>
/// <param name="LabelTypes">Label types this printer can print.</param>
/// <param name="LastPrintedUtc">When it last printed (null if never).</param>
public sealed record PrinterStatus(
    string PrinterId,
    string Name,
    string LineId,
    bool Online,
    bool IsSpare,
    string Orientation,
    int VerifyFailCount,
    int VerifyFailThreshold,
    IReadOnlyList<string> LabelTypes,
    DateTimeOffset? LastPrintedUtc);
