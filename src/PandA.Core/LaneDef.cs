namespace PandA.Core;

public sealed record LaneDef(string LaneId, int LaneNumber, DateTimeOffset? LastDiverted = null);
