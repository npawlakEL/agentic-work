namespace PandA.Sim.Line;

public sealed record SimPoint(double X, double Y, double Z);

/// <summary>
/// A configured printer station on the line (input): its id, apply surface, and the tracking-device
/// ids its fire point is anchored to. The apply device seats the printer on the belt; the print device
/// drives when the printed label appears on the tamp head.
/// </summary>
public sealed record SimPrinterStation(
    string PrinterId,
    string Orientation,
    int PrintTrackingDevice = 1,
    int ApplyTrackingDevice = 2,
    int PrintFirePointPulses = 0);

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
    bool Applied,
    bool OnTamp = false);

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
