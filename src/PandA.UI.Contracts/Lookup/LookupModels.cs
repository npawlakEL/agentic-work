namespace PandA.UI.Contracts.Lookup;

/// <summary>Verify state filter for the Label Data Lookup grid.</summary>
public enum VerifyStateFilter
{
    Any,
    Enabled,
    Passed,
    Failed,
}

/// <summary>Filter set for the Label Data Lookup grid (mirrors the source GetPandaData query).</summary>
/// <param name="FuzzyText">Matches BlindLabel OR UPC OR GTIN OR EAN OR LabelBarcode (contains).</param>
/// <param name="WaveId">Restrict to a wave.</param>
/// <param name="CreatedFromUtc">Creation/verify window start.</param>
/// <param name="CreatedToUtc">Creation/verify window end.</param>
/// <param name="CartonStatus">Restrict to a carton status.</param>
/// <param name="VerifyState">Verify-state filter.</param>
/// <param name="PrintedOnly">When true, only cartons that have been printed at least once.</param>
/// <param name="ProfileName">Restrict to a fire-point profile/map name.</param>
/// <param name="LineId">Restrict to a line.</param>
/// <param name="PrinterId">Restrict to a printer.</param>
/// <param name="Page">Zero-based page index.</param>
/// <param name="PageSize">Page size (source caps at 500).</param>
public sealed record TransportOrderFilter(
    string? FuzzyText = null,
    string? WaveId = null,
    DateTimeOffset? CreatedFromUtc = null,
    DateTimeOffset? CreatedToUtc = null,
    string? CartonStatus = null,
    VerifyStateFilter VerifyState = VerifyStateFilter.Any,
    bool PrintedOnly = false,
    string? ProfileName = null,
    string? LineId = null,
    string? PrinterId = null,
    int Page = 0,
    int PageSize = 100);

/// <summary>A transport-order row in the lookup grid (collapsed view).</summary>
/// <param name="CartonId">Carton (transport-order) identifier.</param>
/// <param name="BlindLabel">Blind/tracking label.</param>
/// <param name="CartonStatus">Current status.</param>
/// <param name="VerifyResult">Last verify result (empty when not verified).</param>
/// <param name="VerifiedUtc">When last verified.</param>
/// <param name="PrintedCount">Monotonic print count.</param>
/// <param name="ProfileName">Active fire-point profile/map.</param>
/// <param name="PassFailDestination">Routing destination for pass/fail.</param>
/// <param name="WaveId">Owning wave.</param>
/// <param name="IsHeld">True when held for intervention (reprint can be authorized).</param>
/// <param name="LineId">Owning line.</param>
public sealed record TransportOrderRow(
    string CartonId,
    string BlindLabel,
    string CartonStatus,
    string VerifyResult,
    DateTimeOffset? VerifiedUtc,
    int PrintedCount,
    string ProfileName,
    string PassFailDestination,
    string WaveId,
    bool IsHeld,
    string LineId);

/// <summary>One of the (up to 6) label slots on a carton.</summary>
/// <param name="Slot">1-based slot index.</param>
/// <param name="LabelType">Label type for this slot.</param>
/// <param name="LabelBarcode">Encoded barcode.</param>
/// <param name="Lpn">License-plate number.</param>
/// <param name="Zpl">Raw ZPL (collapsible in the UI).</param>
/// <param name="Printed">Whether this slot has been printed.</param>
public sealed record CartonLabelSlot(
    int Slot,
    string LabelType,
    string LabelBarcode,
    string Lpn,
    string Zpl,
    bool Printed);
