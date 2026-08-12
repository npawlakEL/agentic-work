using PandA.UI.Contracts.Common;

namespace PandA.UI.Contracts.Manda;

/// <summary>Provides the list of configured MandA stations for the panel dropdown.</summary>
public interface IMandaStationQuery
{
    Task<IReadOnlyList<MandaStation>> GetStationsAsync(CancellationToken ct = default);
}

/// <summary>Resolves a scanned carton barcode into its printable label slots.</summary>
public interface IMandaScanCommand
{
    Task<MandaScanResult> ScanAsync(string stationId, string scannedBarcode, CancellationToken ct = default);
}

/// <summary>Prints an operator-selected subset of a carton's labels to a station.</summary>
public interface IMandaPrintCommand
{
    /// <param name="stationId">Target station.</param>
    /// <param name="cartonId">Resolved carton.</param>
    /// <param name="slots">1-based slots to print.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<CommandResult> PrintAsync(string stationId, string cartonId, IReadOnlyList<int> slots, CancellationToken ct = default);
}

/// <summary>Verifies a single scanned label at a station (per-label MandA verify).</summary>
public interface IMandaVerifyCommand
{
    Task<MandaVerifyResult> VerifyAsync(string stationId, string cartonId, string scannedBarcode, CancellationToken ct = default);
}

/// <summary>Live state feed for a MandA station panel.</summary>
public interface IMandaStationStream
{
    Task<MandaStationState> GetStateAsync(string stationId, CancellationToken ct = default);

    IAsyncEnumerable<MandaStationState> SubscribeAsync(string stationId, CancellationToken ct = default);
}
