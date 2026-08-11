namespace PandA.Core.Verification;

/// <summary>Per-label reason within a <see cref="VerifyResult"/>, mirroring source classification.</summary>
public enum VerifyLabelReason
{
    /// <summary>Scanned value matched the expected barcode (or an xref backup).</summary>
    Matched,

    /// <summary>Scanner no-read ('?').</summary>
    NoRead,

    /// <summary>Scanner error / no data ('!','~' or literal '0').</summary>
    NoData,

    /// <summary>Label conflict ('#').</summary>
    Conflict,

    /// <summary>Read a value that did not match the expected barcode.</summary>
    Mismatch,

    /// <summary>An expected label was never presented to the scanner.</summary>
    Missing,

    /// <summary>A label was read that the carton data did not expect.</summary>
    Extra,
}
