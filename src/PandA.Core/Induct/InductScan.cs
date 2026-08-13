namespace PandA.Core.Induct;

/// <summary>
/// The decoded carton-arrival event at the induct scanner — the transport-agnostic projection of the
/// sorter PLC inbound message.
/// <para>
/// <b>Transport boundary (decision-020).</b> On econtroller the raw induct message (<b>PLC 281</b>) is
/// received by the <b>ADS plugin</b>, which materializes it into an <c>MfcTransportOrder</c> carrying the
/// per-carton data as <b>TU extension data</b>. The thin <c>PandA.EController</c> adapter then maps that
/// <c>MfcTransportOrder</c> + extension onto this <see cref="InductScan"/>. <c>PandA.Core</c> never parses a
/// 281 and never sees an <c>MfcTransportOrder</c>: it consumes only this decoded event, so the domain stays
/// backend-agnostic (decision-002/006). In the Sim, <see cref="InductScan"/> is produced directly.
/// </para>
/// <para>
/// It is the payload later features read: F-LOG1 run history (dimensions + gap + index), F20 read-quality
/// classification (<see cref="FrontGap"/>), F18 barcode-based resolution (<see cref="BlindLabel"/> /
/// <see cref="ScannedLabels"/>), and DYNAP apply-point calculation (<see cref="Length"/> /
/// <see cref="Height"/>).
/// </para>
/// </summary>
/// <param name="LineId">The PandA line the carton inducted on.</param>
/// <param name="BlindLabel">The scanned blind/tracking barcode used to match the advised order.</param>
/// <param name="FrontGap">Measured gap to the preceding carton, in the line's gap units (F20 input).</param>
/// <param name="Length">Carton length along travel, in PLC step pulses (DYNAP apply-point input).</param>
/// <param name="Width">Carton width, in PLC units.</param>
/// <param name="Height">Carton height, in PLC units (DYNAP top-apply input).</param>
/// <param name="Weight">Carton weight, in PLC units.</param>
/// <param name="SorterNumber">The sorter that produced the scan (PLC index bundle).</param>
/// <param name="SorterMode">The sorter's operating mode at scan time.</param>
/// <param name="DeviceId">The scanning device/tracking-eye id.</param>
/// <param name="SeqNum">The PLC sequence number for this carton pass.</param>
/// <param name="ScannedLabels">Raw scanner reads captured at induct (may be empty).</param>
public sealed record InductScan(
    string LineId,
    string BlindLabel,
    int FrontGap = int.MaxValue,
    int Length = 0,
    int Width = 0,
    int Height = 0,
    int Weight = 0,
    int SorterNumber = 0,
    int SorterMode = 0,
    int DeviceId = 0,
    int SeqNum = 0,
    IReadOnlyList<string>? ScannedLabels = null)
{
    /// <summary>Raw scanner reads captured at induct (never null).</summary>
    public IReadOnlyList<string> ScannedLabels { get; } = ScannedLabels ?? [];

    /// <summary>
    /// A bare scan carrying only the identity (line + blind label) and no physical measurements — the
    /// backward-compatible shape used by callers that have not yet plumbed the PLC measurement bundle.
    /// <see cref="FrontGap"/> defaults to <see cref="int.MaxValue"/> so gap classification always passes.
    /// </summary>
    public static InductScan ForBlindLabel(string lineId, string blindLabel) =>
        new(lineId, blindLabel);

    /// <summary>Project the physical measurements captured at induct for persistence on the carton.</summary>
    public InductScanMeasurements ToMeasurements() =>
        new(FrontGap, Length, Width, Height, Weight, SorterNumber, SorterMode, DeviceId, SeqNum);
}

/// <summary>
/// The physical measurement snapshot from an <see cref="InductScan"/>, persisted on the carton
/// (<see cref="PandA.Core.TransportOrder.InductMeasurements"/>). Identity fields (line/blind label) are
/// omitted — the carton already carries them. Read by F-LOG1, F20, and DYNAP once wired onto the induct path.
/// </summary>
public sealed record InductScanMeasurements(
    int FrontGap,
    int Length,
    int Width,
    int Height,
    int Weight,
    int SorterNumber,
    int SorterMode,
    int DeviceId,
    int SeqNum);

