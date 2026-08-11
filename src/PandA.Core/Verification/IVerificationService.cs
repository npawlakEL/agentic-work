namespace PandA.Core.Verification;

/// <summary>
/// Compares the labels scanned at the verify station against a carton's expected label set and
/// produces a <see cref="VerifyResult"/>. Port of <c>sdisp_TOOL_PA_VerifyLabel</c> (architecture-log 006).
/// </summary>
public interface IVerificationService
{
    /// <summary>
    /// Verify a carton.
    /// </summary>
    /// <param name="expected">The carton's advised label set (expected barcodes per type).</param>
    /// <param name="scanned">Labels read at the verify station, pre-typed.</param>
    /// <param name="options">Verify toggles (enable/bypass/content).</param>
    /// <param name="xref">Optional alternate acceptable barcodes per type (source PandaDataXRef).</param>
    VerifyResult Verify(
        PandaLabelSet expected,
        IReadOnlyList<ScannedLabel> scanned,
        VerifyOptions options,
        IReadOnlyList<LabelXref>? xref = null);
}
