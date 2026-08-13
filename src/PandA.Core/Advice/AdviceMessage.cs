namespace PandA.Core.Advice;

public sealed record AdviceMessage(
    string LineId,
    string BlindLabel,
    PandaLabelSet Labels,
    string? WaveId = null,
    string? ProfileName = null,
    bool Bypass = false,
    bool VerifyEnabled = true,
    string? VerifyPassDest = null,
    string? VerifyFailDest = null,
    long? RecId = null,
    int? OrderPriority = null);
