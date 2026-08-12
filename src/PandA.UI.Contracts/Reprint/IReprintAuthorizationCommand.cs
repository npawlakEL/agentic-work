namespace PandA.UI.Contracts.Reprint;

/// <summary>
/// Audited "Authorize Reprint" action shared by the Label Data Lookup grid and the
/// Reject Cartons list. Releases a held carton (or a specific label slot) for reprint.
/// </summary>
public interface IReprintAuthorizationCommand
{
    /// <summary>Authorize a reprint for the whole carton.</summary>
    /// <param name="cartonId">The carton (transport-order) identifier.</param>
    /// <param name="operatorName">Who authorized it (for the audit trail).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Common.CommandResult> AuthorizeReprintAsync(string cartonId, string operatorName, CancellationToken ct = default);

    /// <summary>Authorize a reprint for a single label slot on the carton.</summary>
    /// <param name="cartonId">The carton (transport-order) identifier.</param>
    /// <param name="slot">The 1-based label slot to reprint.</param>
    /// <param name="operatorName">Who authorized it (for the audit trail).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Common.CommandResult> AuthorizeSlotReprintAsync(string cartonId, int slot, string operatorName, CancellationToken ct = default);
}
