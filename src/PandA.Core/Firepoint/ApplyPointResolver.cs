namespace PandA.Core;

/// <summary>
/// Resolves decision-016 DynamicApplyPoint values from human inch/edge notation into PLC fire pulses.
/// Ports the apply-point math from <c>sdisp_TOOL_CUSTOM_DynamicApplyPoint.sql</c>.
/// </summary>
public sealed class ApplyPointResolver
{
    /// <summary>Resolve a carton-aware apply point to the integer PLC pulse the TAMP head fires at.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="ApplyPointConfig.EncoderResolution"/> is not positive.</exception>
    /// <exception cref="ArgumentException">Top orientation is requested without complete tamp kinematic inputs.</exception>
    public int Resolve(
        ApplyPoint applyPoint,
        ApplyOrientation orientation,
        CartonDimensions carton,
        ApplyPointConfig config)
    {
        ArgumentNullException.ThrowIfNull(applyPoint);
        ArgumentNullException.ThrowIfNull(carton);
        ArgumentNullException.ThrowIfNull(config);

        if (config.EncoderResolution <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(config.EncoderResolution),
                config.EncoderResolution,
                "EncoderResolution must be greater than zero inches per pulse.");
        }

        var sideFirePoint = ResolveSide(applyPoint, carton, config);
        var firePoint = orientation switch
        {
            ApplyOrientation.Side => sideFirePoint,
            ApplyOrientation.Top => sideFirePoint - ResolveTopLeadCorrection(carton, config),
            _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Unknown apply orientation."),
        };

        // PLC fire points are integral pulses; round halves away from zero before casting.
        return (int)Math.Round(firePoint, MidpointRounding.AwayFromZero);
    }

    private static decimal ResolveSide(ApplyPoint applyPoint, CartonDimensions carton, ApplyPointConfig config) =>
        applyPoint.Edge switch
        {
            Edge.Leading => applyPoint.Inches / config.EncoderResolution + config.DefaultApplyDistance,
            Edge.Trailing => carton.CartonLengthPulses
                - (applyPoint.Inches + config.LabelWidthInches) / config.EncoderResolution
                + config.DefaultApplyDistance,
            // decision-016 line 40 specifies labelWidth/2 ∓ x; the port uses LW/2 - x.
            Edge.Middle => carton.CartonLengthPulses / 2m
                - (config.LabelWidthInches / 2m - applyPoint.Inches) / config.EncoderResolution
                + config.DefaultApplyDistance,
            _ => throw new ArgumentOutOfRangeException(nameof(applyPoint), applyPoint.Edge, "Unknown apply-point edge."),
        };

    private static decimal ResolveTopLeadCorrection(CartonDimensions carton, ApplyPointConfig config)
    {
        if (config.TampMountHeightInches is null || config.BeltSpeed is null || config.TampSpeed is null)
        {
            throw new ArgumentException(
                "Top apply resolution requires TampMountHeightInches, BeltSpeed, and TampSpeed.",
                nameof(config));
        }

        if (config.TampSpeed.Value == 0m)
        {
            throw new ArgumentException("Top apply resolution requires TampSpeed to be non-zero.", nameof(config));
        }

        var k = config.BeltSpeed.Value / config.TampSpeed.Value;
        var leadCorrectionInches = k * (config.TampMountHeightInches.Value - carton.CartonHeightInches);

        return leadCorrectionInches / config.EncoderResolution;
    }
}
