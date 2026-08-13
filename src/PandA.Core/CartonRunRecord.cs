using PandA.Core.Induct;

namespace PandA.Core;

/// <summary>
/// Captures one carton pass through the induct scanner for run-history auditing.
/// </summary>
public sealed record CartonRunRecord(
    long RunId,
    string TuId,
    long PandaDataId,
    string LineId,
    int SorterNumber,
    int SorterMode,
    int DeviceId,
    int SeqNum,
    int LabelStatus,
    string[] ScannedLabels,
    int Length,
    int Width,
    int Height,
    int Weight,
    int FrontGap,
    CartonStatus StatusAtInduct,
    string? AssignedPrinter,
    int? DestinationLane,
    DateTimeOffset CreatedAt)
{
    public static CartonRunRecord Create(
        string tuId,
        long pandaDataId,
        string lineId,
        int sorterNumber,
        int sorterMode,
        int deviceId,
        int seqNum,
        int labelStatus,
        string[] scannedLabels,
        int length,
        int width,
        int height,
        int weight,
        int frontGap,
        CartonStatus statusAtInduct,
        string? assignedPrinter,
        int? destinationLane,
        DateTimeOffset? createdAt = null) =>
        new(
            0,
            tuId,
            pandaDataId,
            lineId,
            sorterNumber,
            sorterMode,
            deviceId,
            seqNum,
            labelStatus,
            scannedLabels,
            length,
            width,
            height,
            weight,
            frontGap,
            statusAtInduct,
            assignedPrinter,
            destinationLane,
            createdAt ?? DateTimeOffset.UtcNow);
}
