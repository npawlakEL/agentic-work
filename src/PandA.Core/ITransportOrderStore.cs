namespace PandA.Core;

/// <summary>
/// Persistence port for transport orders (cartons). The econtroller adapter implements this over
/// MfcTransportOrder; the Sim uses an in-memory dictionary.
/// </summary>
public interface ITransportOrderStore
{
    /// <summary>Find the active (in-flight) transport order for a blind label, or null.</summary>
    ValueTask<TransportOrder?> FindActiveByTuIdAsync(string tuId, CancellationToken cancellationToken = default);

    /// <summary>Insert a new transport order or update an existing one.</summary>
    ValueTask UpsertAsync(TransportOrder transportOrder, CancellationToken cancellationToken = default);
}
