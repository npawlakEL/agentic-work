namespace PandA.Core.Verification;

/// <summary>
/// One reject-history row (source <c>RejectHistory</c> via <c>sdisp_CUSTOM_RejectHistory_Insert</c>), written
/// for every verify whose code is not 1 (<c>sdisp_PA_Scan_Verify</c> L117-132; owner ruling 2026-10-21).
/// </summary>
/// <param name="PandaDataId">The carton's stable data id (0 when no carton data matched).</param>
/// <param name="CartonListId">The verify scan's carton-list id (0 when not tracked).</param>
/// <param name="Code">The source verify code.</param>
/// <param name="Reason">The GUI-facing reason text for <paramref name="Code"/>.</param>
/// <param name="RejectedAt">When the reject was recorded.</param>
/// <param name="TuId">The carton (blind label / exception carton id), when known.</param>
/// <param name="LineId">The line, when known.</param>
public sealed record RejectRecord(
    long PandaDataId,
    long CartonListId,
    VerifyReasonCode Code,
    string Reason,
    DateTimeOffset RejectedAt,
    string? TuId = null,
    string? LineId = null)
{
    public static RejectRecord Create(
        long pandaDataId,
        long cartonListId,
        VerifyReasonCode code,
        DateTimeOffset? rejectedAt = null,
        string? tuId = null,
        string? lineId = null) =>
        new(pandaDataId, cartonListId, code, code.ToDescription(), rejectedAt ?? DateTimeOffset.UtcNow, tuId, lineId);
}