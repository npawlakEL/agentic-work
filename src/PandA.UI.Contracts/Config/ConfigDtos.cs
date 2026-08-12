namespace PandA.UI.Contracts.Config;

/// <summary>Global settings (flags/thresholds) edited as a single form.</summary>
/// <param name="EncoderResolutionInchesPerPulse">Encoder resolution used by the apply-point resolver.</param>
/// <param name="ReprintLabelsEnabled">Master "Reprint Labels" allow flag.</param>
/// <param name="LoadBalanceEnabled">Round-robin printer load balancing.</param>
/// <param name="TwoPrinterRuleEnabled">Legacy 2-printer degraded policy toggle.</param>
/// <param name="VerifyFailThreshold">Consecutive verify failures before a line pauses.</param>
/// <param name="PostTripResetCount">Successful verifies required to clear a paused line.</param>
public sealed record SettingsDto(
    double EncoderResolutionInchesPerPulse,
    bool ReprintLabelsEnabled,
    bool LoadBalanceEnabled,
    bool TwoPrinterRuleEnabled,
    int VerifyFailThreshold,
    int PostTripResetCount);

/// <summary>A label definition (type/name, orientation, print position).</summary>
/// <param name="LabelDefId">Identifier (null when creating).</param>
/// <param name="Name">Label type/name.</param>
/// <param name="Orientation">Apply orientation (Side/Top).</param>
/// <param name="PrintPosition">Default print position/point.</param>
public sealed record LabelDefDto(string? LabelDefId, string Name, string Orientation, int PrintPosition);

/// <summary>A MandA station config entity.</summary>
/// <param name="StationId">Identifier (null when creating).</param>
/// <param name="Name">Display name.</param>
/// <param name="Ip">Printer IP.</param>
/// <param name="Port">Printer port.</param>
public sealed record MandaStationDto(string? StationId, string Name, string Ip, int Port);

/// <summary>Line-control policy for degraded operation.</summary>
public enum LineControlPolicy
{
    AllowDegraded,
    SlowFloor,
    OnlineMinimum,
}

/// <summary>A line: zones, tracking devices, buffer order, active Map, control policy.</summary>
/// <param name="LineId">Identifier (null when creating).</param>
/// <param name="Name">Display name.</param>
/// <param name="Zones">Zone identifiers on the line.</param>
/// <param name="TrackingDevices">Tracking photo-eye identifiers, in order.</param>
/// <param name="BufferOrder">Per-position label-type buffer order.</param>
/// <param name="ActiveMapId">The currently active fire-point Map.</param>
/// <param name="ControlPolicy">Degraded-operation policy.</param>
/// <param name="OnlineMinimum">Minimum online printers when policy = OnlineMinimum.</param>
public sealed record LineDto(
    string? LineId,
    string Name,
    IReadOnlyList<string> Zones,
    IReadOnlyList<string> TrackingDevices,
    IReadOnlyList<string> BufferOrder,
    string? ActiveMapId,
    LineControlPolicy ControlPolicy,
    int OnlineMinimum);

/// <summary>A printer on a line.</summary>
/// <param name="PrinterId">Identifier (null when creating).</param>
/// <param name="LineId">Owning line.</param>
/// <param name="Name">Display name.</param>
/// <param name="Ip">Printer IP.</param>
/// <param name="Port">Printer port.</param>
/// <param name="Orientation">Apply orientation (Side/Top).</param>
/// <param name="LabelTypes">Label types this printer can print.</param>
/// <param name="ConfigOrder">Tie-break ordering for selection.</param>
/// <param name="EncoderResolutionInchesPerPulse">Per-printer encoder resolution override.</param>
/// <param name="LabelWidthInches">Physical label width (fixes the source hardcoded-4 bug).</param>
/// <param name="SpareEligible">Whether the printer can act as a spare.</param>
/// <param name="TampMountHeightInches">Top-apply kinematics: tamp mount height.</param>
/// <param name="BeltSpeedInchesPerSecond">Top-apply kinematics: belt speed.</param>
/// <param name="TampSpeedInchesPerSecond">Top-apply kinematics: tamp speed.</param>
public sealed record PrinterDto(
    string? PrinterId,
    string LineId,
    string Name,
    string Ip,
    int Port,
    string Orientation,
    IReadOnlyList<string> LabelTypes,
    int ConfigOrder,
    double EncoderResolutionInchesPerPulse,
    double LabelWidthInches,
    bool SpareEligible,
    double TampMountHeightInches,
    double BeltSpeedInchesPerSecond,
    double TampSpeedInchesPerSecond);

/// <summary>A fire point for a (printer × label type): print + apply tracking devices and points.</summary>
/// <param name="FirePointId">Identifier (null when creating).</param>
/// <param name="PrinterId">Owning printer.</param>
/// <param name="LabelType">Label type this fire point applies to.</param>
/// <param name="PrintTrackingDevice">Tracking device where printing starts.</param>
/// <param name="PrintPoint">Static print fire point (integer position).</param>
/// <param name="ApplyTrackingDevice">Tracking device where applying starts.</param>
/// <param name="ApplyPointNotation">Apply point in inch+edge notation (e.g. 1T, 1L, 0M).</param>
/// <param name="DynamicApply">Whether dynamic apply-point adjustment is enabled.</param>
public sealed record FirePointDto(
    string? FirePointId,
    string PrinterId,
    string LabelType,
    string PrintTrackingDevice,
    int PrintPoint,
    string ApplyTrackingDevice,
    string ApplyPointNotation,
    bool DynamicApply);

/// <summary>A Map: a named collection of fire-point profiles; exactly one active per line.</summary>
/// <param name="MapId">Identifier (null when creating).</param>
/// <param name="LineId">Owning line.</param>
/// <param name="Name">Display name.</param>
/// <param name="FirePointIds">Fire points belonging to this map.</param>
public sealed record MapDto(string? MapId, string LineId, string Name, IReadOnlyList<string> FirePointIds);
