namespace PandA.Sim.Line;

public sealed record LineSimulationSettings
{
    public int TrackingEyeCount { get; init; } = 3;

    public double EncoderResolutionInchesPerPulse { get; init; } = 0.25;

    public double BeltSpeedInchesPerSecond { get; init; } = 24;

    public double ConveyorLengthInches { get; init; } = 260;

    public double CartonLengthInches { get; init; } = 24;

    public double CartonWidthInches { get; init; } = 16;

    public double CartonHeightInches { get; init; } = 12;

    public double DefaultApplyDistanceInches { get; init; } = 1;

    public double PrintFirePointOffsetInches { get; init; } = 5;

    public double ApplyFirePointOffsetInches { get; init; } = 0;

    public double CartonSpacingInches { get; init; } = 36;

    public int PrinterCount { get; init; } = 1;

    private static readonly LineSimulationSettings Defaults = new();

    public LineSimulationSettings Normalize() => this with
    {
        TrackingEyeCount = Math.Clamp(TrackingEyeCount, 3, 12),
        EncoderResolutionInchesPerPulse = Math.Clamp(Finite(EncoderResolutionInchesPerPulse, Defaults.EncoderResolutionInchesPerPulse), 0.01, 5),
        BeltSpeedInchesPerSecond = Math.Clamp(Finite(BeltSpeedInchesPerSecond, Defaults.BeltSpeedInchesPerSecond), 6, 72),
        ConveyorLengthInches = Math.Clamp(Finite(ConveyorLengthInches, Defaults.ConveyorLengthInches), 160, 600),
        CartonLengthInches = Math.Clamp(Finite(CartonLengthInches, Defaults.CartonLengthInches), 6, 60),
        CartonWidthInches = Math.Clamp(Finite(CartonWidthInches, Defaults.CartonWidthInches), 6, 48),
        CartonHeightInches = Math.Clamp(Finite(CartonHeightInches, Defaults.CartonHeightInches), 4, 48),
        DefaultApplyDistanceInches = Math.Clamp(Finite(DefaultApplyDistanceInches, Defaults.DefaultApplyDistanceInches), 0, 24),
        PrintFirePointOffsetInches = Math.Clamp(Finite(PrintFirePointOffsetInches, Defaults.PrintFirePointOffsetInches), 0, 96),
        ApplyFirePointOffsetInches = Math.Clamp(Finite(ApplyFirePointOffsetInches, Defaults.ApplyFirePointOffsetInches), -48, 96),
        CartonSpacingInches = Math.Clamp(Finite(CartonSpacingInches, Defaults.CartonSpacingInches), 12, 120),
        PrinterCount = Math.Clamp(PrinterCount, 1, 4),
    };

    // Math.Clamp propagates NaN, so non-finite input falls back to the default before clamping.
    private static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;
}
