namespace PandA.Core.Verification;

/// <summary>
/// Overall verify result for a carton. Abstracts the source's numeric VerifyPass code space
/// (architecture-log 006) to a stable enum; only <see cref="Pass"/> lets a carton proceed.
/// A later host/GUI mapper can translate these + per-label detail back to the exact codes.
/// </summary>
public enum VerifyOutcome
{
    /// <summary>All expected labels matched (source VerifyPass=1).</summary>
    Pass,

    /// <summary>Genuine barcode mismatch or a required label missing from the box.</summary>
    Fail,

    /// <summary>Scanner reported a no-read for a label (source '?' sentinel).</summary>
    NoRead,

    /// <summary>Scanner error / no data (source '!','~' sentinels or literal '0').</summary>
    NoData,

    /// <summary>Label conflict — more than one candidate read (source '#' sentinel).</summary>
    Conflict,

    /// <summary>Verify skipped/bypassed while disabled (source VerifyPass=3).</summary>
    Ignore,
}
