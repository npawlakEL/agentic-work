namespace PandA.Sim.Line;

public sealed record SimPoint(double X, double Y, double Z);

public sealed record LabelPlacementSnapshot(
    string LabelType,
    string PrinterId,
    string Lpn,
    SimPoint PrintPoint,
    SimPoint ApplyPoint,
    double CartonOffsetInches,
    string ApplyPointNotation,
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
    IReadOnlyList<SimCartonSnapshot> Cartons,
    string LastEvent);
