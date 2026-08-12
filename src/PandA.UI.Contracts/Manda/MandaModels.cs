using PandA.UI.Contracts.Common;

namespace PandA.UI.Contracts.Manda;

/// <summary>A MandA station available in the station dropdown.</summary>
/// <param name="StationId">Station identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Ip">Printer IP.</param>
/// <param name="Port">Printer port.</param>
public sealed record MandaStation(string StationId, string Name, string Ip, int Port);

/// <summary>A label slot resolved from a scanned carton, offered for manual printing.</summary>
/// <param name="Slot">1-based slot index.</param>
/// <param name="LabelType">Label type.</param>
/// <param name="LabelBarcode">Encoded barcode used to verify.</param>
/// <param name="Zpl">Raw ZPL that would be sent.</param>
/// <param name="Printed">Whether this slot has already been printed at this station.</param>
/// <param name="Verified">Whether this slot has been verified.</param>
public sealed record MandaLabel(int Slot, string LabelType, string LabelBarcode, string Zpl, bool Printed, bool Verified);

/// <summary>Result of scanning a carton barcode at a MandA station.</summary>
/// <param name="Found">True when the carton resolved.</param>
/// <param name="CartonId">The resolved carton (empty when not found).</param>
/// <param name="BlindLabel">Blind/tracking label.</param>
/// <param name="Labels">Available label slots to print.</param>
/// <param name="Message">Detail when not found or on a warning.</param>
public sealed record MandaScanResult(
    bool Found,
    string CartonId,
    string BlindLabel,
    IReadOnlyList<MandaLabel> Labels,
    string? Message = null)
{
    public static MandaScanResult NotFound(string message) => new(false, "", "", [], message);
}

/// <summary>Per-label verify outcome at a MandA station.</summary>
/// <param name="Slot">The slot verified.</param>
/// <param name="Passed">True when the scanned barcode matched the expected label.</param>
/// <param name="Message">Detail (mismatch, unexpected scan, etc.).</param>
public sealed record MandaVerifyResult(int Slot, bool Passed, string? Message = null);

/// <summary>Live state of a MandA station panel.</summary>
/// <param name="StationId">Station identifier.</param>
/// <param name="Online">Whether the station printer is reachable.</param>
/// <param name="ActiveCartonId">Carton currently in progress (empty when idle).</param>
/// <param name="Labels">Current label slots and their print/verify state.</param>
public sealed record MandaStationState(
    string StationId,
    bool Online,
    string ActiveCartonId,
    IReadOnlyList<MandaLabel> Labels);
