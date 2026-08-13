namespace PandA.Sim.Line;

public sealed record SimPoint(double X, double Y, double Z);

/// <summary>A configured printer station on the line (input): its id and apply surface.</summary>
public sealed record SimPrinterStation(string PrinterId, string Orientation);

/// <summary>A printer station positioned on the belt for rendering (output).</summary>
public sealed record SimPrinterSnapshot(string PrinterId, double PositionInches, string Orientation);

public sealed record LabelPlacementSnapshot(
    string LabelType,
    string PrinterId,
    string Lpn,
    SimPoint PrintPoint,
    SimPoint ApplyPoint,
    double CartonOffsetInches,
    string ApplyPointNotation,
    string Orientation,
    bool Applied);

public sealed record SimCartonSnapshot(
    string CartonId,
    string BlindLabel,
    double PositionInches,
    double LengthInches,
    double WidthInches,
    double HeightInches,
    string State,
    IReadOnlyList<LabelPlacementSnapshot> Labels);

public sealed record TrackingEyeSnapshot(
    string Id,
    double PositionInches,
    bool Active);

public sealed record LineSimulationSnapshot(
    bool Running,
    double BeltSpeedInchesPerSecond,
    double ConveyorLengthInches,
    string LineId,
    string LineName,
    LineSimulationSettings Settings,
    IReadOnlyList<TrackingEyeSnapshot> Eyes,
    IReadOnlyList<SimPrinterSnapshot> Printers,
    IReadOnlyList<SimCartonSnapshot> Cartons,
    string LastEvent);
