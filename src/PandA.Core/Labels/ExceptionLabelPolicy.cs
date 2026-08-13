using PandA.Core.Induct;

namespace PandA.Core.Labels;

/// <summary>
/// F10 exception-label policy helpers (decision-021). Resolves the effective per-carton print decision
/// and maps a read-quality <see cref="CartonStatus"/> to the exception reason that names its template.
/// </summary>
public static class ExceptionLabelPolicy
{
    /// <summary>
    /// Effective <c>PrintExceptionLabels</c> flag. A <em>defined</em> global value overrides every line;
    /// only when the global is unset (<c>null</c>) do the per-line value (then <c>false</c>) apply.
    /// </summary>
    public static bool Effective(bool? global, bool? line) => global ?? line ?? false;

    /// <summary>
    /// Map the induct read-quality classification to the exception reason. An unmatched carton with a clean
    /// read (or a gap-only fault) is <see cref="ExceptionType.NotReceived"/> — we read it but have no advice.
    /// Returns <c>null</c> for statuses that never produce an exception label (e.g. Bypass).
    /// </summary>
    public static ExceptionType? Classify(CartonStatus status) => status switch
    {
        CartonStatus.NoRead => ExceptionType.NoRead,
        CartonStatus.NoData => ExceptionType.NoData,
        CartonStatus.LabelConflict => ExceptionType.LabelConflict,
        CartonStatus.NoInformation => ExceptionType.NoInformation,
        CartonStatus.Duplicate => ExceptionType.Duplicate,
        CartonStatus.PrintHold => ExceptionType.NotReceived,
        CartonStatus.GapError => ExceptionType.NotReceived,
        CartonStatus.PrintReady => ExceptionType.NotReceived,
        _ => null,
    };

    /// <summary>Short human-readable reason used to mint the synthetic carton id (e.g. <c>NoRead-000481</c>).</summary>
    public static string ReasonName(ExceptionType type) => type switch
    {
        ExceptionType.NotReceived => "NotReceived",
        ExceptionType.Duplicate => "Duplicate",
        ExceptionType.NoInformation => "NoInfo",
        ExceptionType.NoRead => "NoRead",
        ExceptionType.NoData => "NoData",
        ExceptionType.LabelConflict => "LabelConflict",
        ExceptionType.DataMismatch => "DataMismatch",
        _ => "Exception",
    };

    /// <summary>Whether the exception label substitutes an <c>&lt;LPN&gt;</c> slot (Duplicate/NoInfo/NotReceived).</summary>
    public static bool UsesLpn(ExceptionType type) =>
        type is ExceptionType.Duplicate or ExceptionType.NoInformation or ExceptionType.NotReceived;

    /// <summary>Mint the synthetic human-readable exception carton id: <c>{Reason}-{seq:000000}</c>.</summary>
    public static string MintCartonId(ExceptionType type, int seq) =>
        $"{ReasonName(type)}-{seq:000000}";
}
