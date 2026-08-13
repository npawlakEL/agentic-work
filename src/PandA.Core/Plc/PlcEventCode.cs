namespace PandA.Core.Plc;

/// <summary>
/// PLC tracking-event codes carried on the event message (source <c>sdisp_BP2PA_Event</c> lines 73–82).
/// The event stream may carry other (unmapped) integer codes; these named values are the recognised ones.
/// Codes 217 and 218 are silently ignored upstream and are intentionally not modelled here.
/// </summary>
public enum PlcEventCode
{
    CartonLost = 2012,
    LateAssignment = 2015,
    UnexpectedCarton = 2016,
    LateTrackingError = 2017,
    EarlyTrackingError = 2019,
}

/// <summary>Maps raw PLC event codes to their human-readable descriptions (source lines 73–82).</summary>
public static class PlcEventCodes
{
    /// <summary>Codes silently dropped before any logging or recovery (source line 71).</summary>
    public static bool IsIgnored(int eventCode) => eventCode is 217 or 218;

    public static string Describe(int eventCode) => eventCode switch
    {
        2012 => "Carton Lost",
        2015 => "Late Carton Assignment",
        2016 => "Unexpected Carton",
        2017 => "Late Tracking Error",
        2019 => "Early Tracking Error",
        _ => $"Undefined [Event Code: {eventCode}]",
    };
}
