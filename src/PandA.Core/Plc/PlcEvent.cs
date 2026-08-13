namespace PandA.Core.Plc;

/// <summary>
/// PLC tracking-event message reported for one carton position (source <c>sdisp_BP2PA_Event</c> params).
/// <paramref name="EventCode"/> is the raw code (may be unmapped); <paramref name="CartonListId"/> is the
/// run identifier (F-LOG1 <c>CartonListID</c>), null/0 when no carton is identified; <paramref name="DeviceId"/>
/// is the reporting device (photo-eye/scanner) position; <paramref name="LineId"/> is the PandA line.
/// </summary>
public sealed record PlcEvent(int EventCode, long? CartonListId, int DeviceId, string LineId);
