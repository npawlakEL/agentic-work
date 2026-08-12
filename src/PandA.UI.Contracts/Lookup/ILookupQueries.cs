using PandA.UI.Contracts.Common;

namespace PandA.UI.Contracts.Lookup;

/// <summary>Filtered, paged query over transport orders for the Label Data Lookup grid.</summary>
public interface ITransportOrderQuery
{
    /// <summary>Return a filtered, paged set of transport-order rows.</summary>
    Task<PagedResult<TransportOrderRow>> QueryAsync(TransportOrderFilter filter, CancellationToken ct = default);

    /// <summary>Distinct values available for building filter dropdowns (statuses, waves, profiles, lines, printers).</summary>
    Task<LookupFilterOptions> GetFilterOptionsAsync(CancellationToken ct = default);
}

/// <summary>Distinct filter values for the lookup grid's dropdowns.</summary>
/// <param name="CartonStatuses">Available carton statuses.</param>
/// <param name="WaveIds">Available wave identifiers.</param>
/// <param name="ProfileNames">Available profile/map names.</param>
/// <param name="LineIds">Available line identifiers.</param>
/// <param name="PrinterIds">Available printer identifiers.</param>
public sealed record LookupFilterOptions(
    IReadOnlyList<string> CartonStatuses,
    IReadOnlyList<string> WaveIds,
    IReadOnlyList<string> ProfileNames,
    IReadOnlyList<string> LineIds,
    IReadOnlyList<string> PrinterIds);

/// <summary>Loads the label slots for an expanded carton row.</summary>
public interface ICartonLabelDetailQuery
{
    /// <summary>Return the label slots (with raw ZPL) for a carton.</summary>
    Task<IReadOnlyList<CartonLabelSlot>> GetLabelSlotsAsync(string cartonId, CancellationToken ct = default);
}
