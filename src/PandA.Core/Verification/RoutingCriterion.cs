namespace PandA.Core.Verification;

/// <summary>
/// A transport-agnostic routing decision derived from PandA's verify/exception outcome (decision-008).
/// PandA does NOT pick lanes/places; it annotates the carton with this criterion. The econtroller adapter
/// projects it onto <c>SortCriteriaExtension</c> as a <c>SortCriteria {Type, Value}</c>, and
/// <c>CriteriaBasedSorting</c> maps <c>{Type, Value}</c> → <c>PlaceID</c> per site via <c>CriteriaConfig.json</c>.
/// </summary>
/// <param name="Type">The criterion type key (always <see cref="PandaVerifyType"/> for verify results).</param>
/// <param name="Value"><c>"Pass"</c> only when the source <c>@VerifyPass</c> is 1; every other code
/// (including bypass, 3) is <c>"Fail"</c> — source <c>@FinalDestination = CASE WHEN @VerifyPass = 1 THEN
/// pass ELSE fail END</c> (sdisp_TOOL_PA_VerifyLabel L470; owner ruling 2026-10-21).</param>
/// <param name="ReasonCode">The source verify code (<see cref="VerifyReasonCode"/>) — the
/// <c>&lt;RejectReasonCode&gt;</c> a site may map to a specific reject/rework place.</param>
/// <param name="Destination">The carton's final destination name: its advised <c>VerifyPassDestName</c> /
/// <c>VerifyFailDestName</c>, defaulting to <see cref="DefaultPassDestination"/> /
/// <see cref="DefaultFailDestination"/> as the source does (L126-127, L137).</param>
public sealed record RoutingCriterion(
    string Type,
    string Value,
    VerifyReasonCode? ReasonCode = null,
    string? Destination = null)
{
    /// <summary>The criterion type key PandA emits for verify results.</summary>
    public const string PandaVerifyType = "PandaVerify";

    /// <summary>Source default pass destination (carton status 'Verify - PASS').</summary>
    public const string DefaultPassDestination = "Verify - PASS";

    /// <summary>Source default fail destination (carton status 'Verify - FAIL').</summary>
    public const string DefaultFailDestination = "Verify - FAIL";

    /// <summary>Map a verify result (and the carton's advised destinations) to its routing criterion.</summary>
    public static RoutingCriterion ForVerify(VerifyResult verify, string? passDestination = null, string? failDestination = null)
    {
        ArgumentNullException.ThrowIfNull(verify);
        return ForCode(VerifyReasonCodes.For(verify), passDestination, failDestination);
    }

    /// <summary>Map a source verify code to its routing criterion.</summary>
    public static RoutingCriterion ForCode(VerifyReasonCode code, string? passDestination = null, string? failDestination = null)
    {
        var pass = code == VerifyReasonCode.VerificationPassed;
        return new RoutingCriterion(
            PandaVerifyType,
            pass ? "Pass" : "Fail",
            code,
            pass ? passDestination ?? DefaultPassDestination : failDestination ?? DefaultFailDestination);
    }
}