namespace PandA.UI.Contracts.Config;

/// <summary>Global settings (flags/thresholds) edited as a single form. These are site-wide.</summary>
/// <param name="EncoderResolutionInchesPerPulse">Global default encoder resolution; seeds/propagates to lines.</param>
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

/// <summary>A label definition — identity only. Orientation and print position live on the fire point.</summary>
/// <param name="LabelDefId">Identifier (null when creating).</param>
/// <param name="Name">Label type/name (the key used across cartons and buffer order).</param>
/// <param name="Description">Free-form description.</param>
/// <param name="LabelWidthInches">Physical label width (moved off the printer).</param>
public sealed record LabelDefDto(string? LabelDefId, string Name, string Description, double LabelWidthInches);

/// <summary>A MandA station config entity.</summary>
/// <param name="StationId">Identifier (null when creating).</param>
/// <param name="Name">Display name.</param>
/// <param name="Ip">Printer IP.</param>
/// <param name="Port">Printer port.</param>
public sealed record MandaStationDto(string? StationId, string Name, string Ip, int Port);

/// <summary>How the simulator animates an orientation's applicator. New orientations map to the closest kind.</summary>
public enum ApplyMotionKind
{
    Side,
    Top,
    Front,
}

/// <summary>A user-defined apply orientation attached to printers (replaces the hard-coded Side/Top string).</summary>
/// <param name="OrientationId">Identifier (null when creating).</param>
/// <param name="Name">Display name (e.g. Side, Top, Front Apply).</param>
/// <param name="MotionKind">Sim applicator motion this orientation maps to.</param>
public sealed record OrientationDto(string? OrientationId, string Name, ApplyMotionKind MotionKind);

/// <summary>Line-control policy for degraded operation.</summary>
public enum LineControlPolicy
{
    AllowDegraded,
    SlowFloor,
    OnlineMinimum,
}

/// <summary>Minimum online printers required for a given orientation (used under the OnlineMinimum policy).</summary>
/// <param name="OrientationId">Orientation the minimum applies to.</param>
/// <param name="Minimum">Minimum online (non-spare) printers of that orientation.</param>
public sealed record OrientationMinimum(string OrientationId, int Minimum);

/// <summary>A line: zones, buffer order, active Map, control policy, per-orientation minimums, belt/encoder.</summary>
/// <param name="LineId">Identifier (null when creating).</param>
/// <param name="Name">Display name.</param>
/// <param name="Zones">Zone identifiers on the line.</param>
/// <param name="BufferOrder">Per-position label-type buffer order.</param>
/// <param name="ActiveMapId">The currently active fire-point Map.</param>
/// <param name="ControlPolicy">Degraded-operation policy.</param>
/// <param name="OnlineMinimums">Per-orientation minimum online printers (policy = OnlineMinimum).</param>
/// <param name="EncoderResolutionInchesPerPulse">Line encoder resolution (defaults from global settings).</param>
/// <param name="BeltSpeedInchesPerSecond">Line belt speed.</param>
public sealed record LineDto(
    string? LineId,
    string Name,
    IReadOnlyList<string> Zones,
    IReadOnlyList<string> BufferOrder,
    string? ActiveMapId,
    LineControlPolicy ControlPolicy,
    IReadOnlyList<OrientationMinimum> OnlineMinimums,
    double EncoderResolutionInchesPerPulse,
    double BeltSpeedInchesPerSecond);

/// <summary>A printer on a line. Owns its orientation, print/apply devices, print point and dynamic-apply toggle.</summary>
/// <param name="PrinterId">Identifier (null when creating).</param>
/// <param name="LineId">Owning line.</param>
/// <param name="Name">Display name.</param>
/// <param name="Ip">Printer IP.</param>
/// <param name="Port">Printer port.</param>
/// <param name="OrientationId">Orientation entity this printer applies with.</param>
/// <param name="LabelTypes">Label types this printer can print.</param>
/// <param name="ConfigOrder">Tie-break ordering for selection.</param>
/// <param name="PrintDevice">Tracking device where printing starts (auto-fills fire points).</param>
/// <param name="ApplyDevice">Tracking device where applying starts (auto-fills fire points).</param>
/// <param name="PrintPoint">Static print fire point (integer position); line can bulk-set this.</param>
/// <param name="DynamicApply">Whether dynamic apply-point adjustment is enabled.</param>
/// <param name="SpareEligible">Whether the printer can act as a spare.</param>
/// <param name="TampMountHeightInches">Top-apply kinematics: tamp mount height.</param>
/// <param name="TampSpeedInchesPerSecond">Top-apply kinematics: tamp speed.</param>
public sealed record PrinterDto(
    string? PrinterId,
    string LineId,
    string Name,
    string Ip,
    int Port,
    string OrientationId,
    IReadOnlyList<string> LabelTypes,
    int ConfigOrder,
    string PrintDevice,
    string ApplyDevice,
    int PrintPoint,
    bool DynamicApply,
    bool SpareEligible,
    double TampMountHeightInches,
    double TampSpeedInchesPerSecond);

/// <summary>A fire point = (printer × label definition) plus the apply point. Devices/print point come from the printer.</summary>
/// <param name="FirePointId">Identifier (null when creating).</param>
/// <param name="PrinterId">Owning printer (supplies orientation, print/apply device, print point, dynamic apply).</param>
/// <param name="LabelDefId">Label definition this fire point applies.</param>
/// <param name="ApplyEdge">Apply edge: Leading, Middle, or Trailing.</param>
/// <param name="ApplyInches">Apply distance from the edge, in inches (negative only for Middle).</param>
public sealed record FirePointDto(
    string? FirePointId,
    string PrinterId,
    string LabelDefId,
    string ApplyEdge,
    double ApplyInches)
{
    /// <summary>Source apply-point notation (e.g. <c>1L</c>, <c>0M</c>, <c>1T</c>) composed from edge + inches.</summary>
    public string ApplyPointNotation =>
        $"{ApplyInches.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}" +
        ApplyEdge switch { "Leading" => "L", "Trailing" => "T", _ => "M" };
}

/// <summary>A Map: a named collection of fire-point profiles; exactly one active per line.</summary>
/// <param name="MapId">Identifier (null when creating).</param>
/// <param name="LineId">Owning line.</param>
/// <param name="Name">Display name.</param>
/// <param name="FirePointIds">Fire points belonging to this map.</param>
public sealed record MapDto(string? MapId, string LineId, string Name, IReadOnlyList<string> FirePointIds);
