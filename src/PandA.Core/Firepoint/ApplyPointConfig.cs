namespace PandA.Core;

/// <summary>
/// Per-printer DynamicApplyPoint inputs from decision-016 and source
/// <c>sdisp_TOOL_CUSTOM_DynamicApplyPoint.sql</c>.
/// </summary>
public sealed record ApplyPointConfig(
    decimal EncoderResolution = 0.25m,
    decimal LabelWidthInches = 4m,
    int DefaultApplyDistance = 0,
    decimal? TampMountHeightInches = null,
    decimal? BeltSpeed = null,
    decimal? TampSpeed = null);
