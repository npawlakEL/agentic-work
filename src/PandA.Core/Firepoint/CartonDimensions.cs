namespace PandA.Core;

/// <summary>
/// Carton dimensions used by decision-016 DynamicApplyPoint, sourced from
/// <c>sdisp_TOOL_CUSTOM_DynamicApplyPoint.sql</c> / <c>PandaCartonList</c>.
/// </summary>
/// <param name="CartonLengthPulses">Carton length in PLC step pulses.</param>
/// <param name="CartonHeightInches">Carton height in inches for top-apply kinematics.</param>
public sealed record CartonDimensions(int CartonLengthPulses, decimal CartonHeightInches = 0m);
