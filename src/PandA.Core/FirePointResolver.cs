namespace PandA.Core;

/// <summary>Outcome of resolving a fire point from a profile.</summary>
public enum FirePointStatus
{
    /// <summary>A fire point was found for the printer + label in the active profile.</summary>
    Resolved,

    /// <summary>The active profile has no fire point for this printer + label slot.</summary>
    NotInProfile,
}

/// <summary>
/// Result of <see cref="FirePointResolver.Resolve"/>: the resolved <see cref="FirePoint"/> (when
/// <see cref="Status"/> is <see cref="FirePointStatus.Resolved"/>) or a reason it was not found.
/// Mirrors the source SP's <c>@ErrorCode</c>/<c>@ErrorMsg</c> outputs rather than throwing on a miss.
/// </summary>
/// <param name="Status">Whether a fire point was resolved.</param>
/// <param name="FirePoint">The resolved fire point, or <c>null</c> when not resolved.</param>
/// <param name="Message">A human-readable reason when not resolved; otherwise <c>null</c>.</param>
public sealed record FirePointResolution(FirePointStatus Status, FirePoint? FirePoint, string? Message)
{
    /// <summary>True when a fire point was resolved.</summary>
    public bool IsResolved => Status == FirePointStatus.Resolved;
}

/// <summary>
/// Resolves the fire point for a given printer + label type from an active profile. Ports the join
/// in <c>sdisp_PA2BP_SendPrinterFirePoints</c> (<c>CurrentProfile</c> ⋈ <c>CurrentFirepoints</c>
/// filtered to the printer + label). Carton-size / orientation adjustment
/// (<c>sdisp_TOOL_CUSTOM_DynamicApplyPoint</c>) is deferred — see 010 backlog.
/// </summary>
public sealed class FirePointResolver
{
    /// <summary>Resolve the fire point for <paramref name="printerId"/> + <paramref name="labelType"/> in <paramref name="profile"/>.</summary>
    public FirePointResolution Resolve(FirePointProfile profile, string printerId, string labelType)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(labelType);

        if (profile.TryGet(printerId, labelType, out var firePoint))
        {
            return new FirePointResolution(FirePointStatus.Resolved, firePoint, null);
        }

        return new FirePointResolution(
            FirePointStatus.NotInProfile,
            null,
            $"Profile '{profile.Name}' has no fire point for printer '{printerId}' + label '{labelType}'.");
    }
}
