namespace PandA.Core;

/// <summary>
/// Runtime activation state for fire-point profiles (PROFSW). The source models this as
/// <c>LabelProfileHeader.Active</c> (available for host selection) vs deactivated. Multiple profiles
/// may be active at once — <c>Active</c> means "selectable", not "currently selected". The operator GUI
/// (future Blazor phase) drives activate/deactivate; the induct path reads the active set via the line
/// config's registry. Provisioned now, wired to the GUI later.
/// </summary>
public interface IProfileStore
{
    /// <summary>The profiles currently active (selectable) on a line.</summary>
    ValueTask<IReadOnlyList<FirePointProfile>> GetActiveProfilesAsync(
        string lineId,
        CancellationToken cancellationToken = default);

    /// <summary>Mark a profile active (selectable) on a line. Idempotent.</summary>
    ValueTask ActivateAsync(string lineId, string profileName, CancellationToken cancellationToken = default);

    /// <summary>Mark a profile inactive on a line so the host can no longer select it. Idempotent.</summary>
    ValueTask DeactivateAsync(string lineId, string profileName, CancellationToken cancellationToken = default);
}
