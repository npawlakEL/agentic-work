namespace PandA.Core.Verification;

public sealed record RejectRecord(
    long PandaDataId,
    long CartonListId,
    VerifyReasonCode Code,
    string Reason,
    DateTimeOffset RejectedAt)
{
    public static RejectRecord Create(
        long pandaDataId,
        long cartonListId,
        VerifyReasonCode code,
        DateTimeOffset? rejectedAt = null) =>
        new(pandaDataId, cartonListId, code, code.ToDescription(), rejectedAt ?? DateTimeOffset.UtcNow);
}
