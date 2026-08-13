namespace PandA.Core.Verification;

/// <summary>
/// A transport-agnostic routing decision derived from PandA's verify/exception outcome (decision-008).
/// PandA does NOT pick lanes/places; it annotates the carton with this criterion. The econtroller adapter
/// projects it onto <c>SortCriteriaExtension</c> as a <c>SortCriteria {Type, Value}</c>, and
/// <c>CriteriaBasedSorting</c> maps <c>{Type, Value}</c> → <c>PlaceID</c> per site via <c>CriteriaConfig.json</c>.
/// </summary>
/// <param name="Type">The criterion type key (always <see cref="PandaVerifyType"/> for verify results).</param>
/// <param name="Value">The routing value: <c>"Pass"</c> when the carton may proceed, otherwise the
/// verify outcome name (<c>"Fail"</c>/<c>"NoRead"</c>/<c>"NoData"</c>/<c>"Conflict"</c>) a site maps to a
/// reject/rework place.</param>
public sealed record RoutingCriterion(string Type, string Value)
{
    /// <summary>The criterion type key PandA emits for verify results.</summary>
    public const string PandaVerifyType = "PandaVerify";

    /// <summary>Map a verify result to its routing criterion. Pass/Ignore proceed; all else route by outcome.</summary>
    public static RoutingCriterion ForVerify(VerifyResult verify)
    {
        ArgumentNullException.ThrowIfNull(verify);

        var value = verify.Outcome is VerifyOutcome.Pass or VerifyOutcome.Ignore
            ? "Pass"
            : verify.Outcome.ToString();

        return new RoutingCriterion(PandaVerifyType, value);
    }
}
