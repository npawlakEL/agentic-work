namespace PandA.Core.Verification;

/// <summary>
/// One label read at the verify station. Phase 2 accepts <b>pre-typed</b> scanned labels; the raw
/// delimited-string + <c>Settings_LabelBufferOrder</c> position parser is bookmarked (backlog).
/// </summary>
/// <param name="LabelType">The label type this scan position corresponds to (e.g. "Shipping").</param>
/// <param name="ScannedValue">The barcode value the scanner returned (may carry sentinel chars).</param>
public sealed record ScannedLabel(string LabelType, string ScannedValue);

/// <summary>
/// An additional acceptable barcode for a label type (source <c>PandaDataXRef</c> / BlindLabel backups).
/// A scanned value matching any xref for its type is treated as a match.
/// </summary>
/// <param name="LabelType">The label type the alternate applies to.</param>
/// <param name="Barcode">An alternate acceptable barcode value.</param>
public sealed record LabelXref(string LabelType, string Barcode);

/// <summary>Toggles governing a verify pass (source PandA attributes).</summary>
/// <param name="VerifyEnabled">When false, verify is skipped: <see cref="Bypass"/> decides Ignore vs Fail.</param>
/// <param name="Bypass">When verify is disabled, a bypassed carton is <see cref="VerifyOutcome.Ignore"/>.</param>
/// <param name="VerifyContentLabel">When false, only Shipping/Exception labels are verified.</param>
public sealed record VerifyOptions(
    bool VerifyEnabled = true,
    bool Bypass = false,
    bool VerifyContentLabel = true);

/// <summary>Per-label detail row in a <see cref="VerifyResult"/>.</summary>
/// <param name="LabelType">The label type.</param>
/// <param name="Expected">The expected barcode, or null when nothing was expected for this type.</param>
/// <param name="Scanned">The scanned value, or null when the expected label was never read.</param>
/// <param name="Reason">Why this label passed or failed.</param>
public sealed record VerifyLabelDetail(
    string LabelType,
    string? Expected,
    string? Scanned,
    VerifyLabelReason Reason);

/// <summary>
/// Result of verifying a carton. <see cref="Outcome"/> reflects the source short-circuit: the first
/// failing label determines the outcome; otherwise <see cref="VerifyOutcome.Pass"/>.
/// </summary>
public sealed record VerifyResult(VerifyOutcome Outcome, IReadOnlyList<VerifyLabelDetail> Details)
{
    /// <summary>True only when every expected label matched.</summary>
    public bool IsPass => Outcome == VerifyOutcome.Pass;
}
