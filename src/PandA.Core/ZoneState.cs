namespace PandA.Core;

/// <summary>
/// Mutable runtime state for a line's conveyor zone (source <c>PandAState</c> table). When the zone
/// goes down, the whole line is shut regardless of printer health. See architecture-log 012.
/// </summary>
public sealed class ZoneState
{
    public ZoneState(bool zoneOnline = true, long activeTrainId = 0)
    {
        ZoneOnline = zoneOnline;
        ActiveTrainId = activeTrainId;
    }

    /// <summary>Conveyor zone up/down (source <c>ZoneStatus</c> 0/1).</summary>
    public bool ZoneOnline { get; set; }

    /// <summary>Current train on the line (source <c>ActiveTrainID</c>); carried for parity, unused by lane-eval.</summary>
    public long ActiveTrainId { get; set; }
}
