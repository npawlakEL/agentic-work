using PandA.Core;

namespace PandA.Core.Control;

// Sources: sdisp_TOOL_PA_ShutLineDown.sql emits vConv.ZoneAr[n].RemoteStop; sdisp_TOOL_PA_SlowLineDown.sql emits vPanda.SlowPanda[n].SlowFlag.
public sealed record LineControlCommand(
    LineControl Control,
    string LineId,
    int PlcArrayIndex,
    string PlcTagNamespace,
    string PlcDbName = "",
    int TagValue = 1,
    string TagMember = "")
{
    public string TagName => string.IsNullOrEmpty(PlcTagNamespace) || string.IsNullOrEmpty(TagMember)
        ? string.Empty
        : $"{PlcTagNamespace}[{PlcArrayIndex}].{TagMember}";

    public static LineControlCommand Balanced(string lineId) =>
        new(LineControl.Balanced, RequireLineId(lineId), 0, string.Empty, TagValue: 0);

    public static LineControlCommand ShutLine(string lineId, int plcZone, string plcDbName = "") =>
        Create(LineControl.ShutLine, lineId, plcZone, "vConv.ZoneAr", "RemoteStop", plcDbName);

    public static LineControlCommand ShutZoneLine(string lineId, int plcZone, string plcDbName = "") =>
        Create(LineControl.ShutZone, lineId, plcZone, "vConv.ZoneAr", "RemoteStop", plcDbName);

    public static LineControlCommand SlowLine(string lineId, int sorterPlcRecId, string plcDbName = "") =>
        Create(LineControl.SlowLine, lineId, sorterPlcRecId, "vPanda.SlowPanda", "SlowFlag", plcDbName);

    private static LineControlCommand Create(
        LineControl control,
        string lineId,
        int plcArrayIndex,
        string plcTagNamespace,
        string tagMember,
        string plcDbName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(plcArrayIndex);
        return new(control, RequireLineId(lineId), plcArrayIndex, plcTagNamespace, plcDbName, 1, tagMember);
    }

    private static string RequireLineId(string lineId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        return lineId;
    }
}


