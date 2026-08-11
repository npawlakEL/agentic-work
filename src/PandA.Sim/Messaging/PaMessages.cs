namespace PandA.Sim.Messaging;

/// <summary>
/// Message code 281 — <c>PANDA_SCAN_INBOUND</c>. The induct scan: <c>DeviceId == 1</c> is the panda
/// (induct) scan; <c>DeviceId &gt; 1</c> is a verify scanner. Field layout (after the code):
/// SourceMode, SorterNumber, SorterMode, DeviceId, SeqNum, LabelStatus, Label1..Label6, Weight, Gap, Length.
/// For the bare-bones harness the induct scan's blind label is Label1; the remaining slots are unused (0).
/// </summary>
/// <param name="SourceMode">param02 — which application database to route to.</param>
/// <param name="SorterNumber">param03 — sorter / panda lane number.</param>
/// <param name="SorterMode">param04 — 1=normal, 2=aim sort, 3=alignment, 4=round robin.</param>
/// <param name="DeviceId">param05 — tracking device; 1 = induct scan, &gt;1 = verify.</param>
/// <param name="SeqNum">param06 — rolling carton sequence number (rolls at ~2000).</param>
/// <param name="LabelStatus">param07 — 0=good, 1=lowboy, 2=no data, 3=data error/multi-scan.</param>
/// <param name="BlindLabel">param08 — Label1, the scanned blind/transport-unit label.</param>
public sealed record InductScanMessage(
    int SourceMode,
    int SorterNumber,
    int SorterMode,
    int DeviceId,
    int SeqNum,
    int LabelStatus,
    string BlindLabel)
{
    public const int Code = 281;

    /// <summary>True when this scan is the induct (panda) scan rather than a verify scanner read.</summary>
    public bool IsInduct => DeviceId == 1;

    /// <summary>Build the full 15-field 281 frame, zero-filling the unused label/measurement slots.</summary>
    public PaFrame ToFrame() => new(Code,
    [
        SourceMode.ToString(),
        SorterNumber.ToString(),
        SorterMode.ToString(),
        DeviceId.ToString(),
        SeqNum.ToString(),
        LabelStatus.ToString(),
        BlindLabel,     // Label1
        "0", "0", "0", "0", "0", // Label2..6
        "0",            // Weight
        "0",            // Gap
        "0",            // Length
    ]);

    public static InductScanMessage FromFrame(PaFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Code != Code)
        {
            throw new FormatException($"Expected code {Code} but got {frame.Code}.");
        }

        return new InductScanMessage(
            SourceMode: int.Parse(frame.Field(2)),
            SorterNumber: int.Parse(frame.Field(3)),
            SorterMode: int.Parse(frame.Field(4)),
            DeviceId: int.Parse(frame.Field(5)),
            SeqNum: int.Parse(frame.Field(6)),
            LabelStatus: int.Parse(frame.Field(7)),
            BlindLabel: frame.Field(8));
    }
}

/// <summary>
/// Message code 286 — <c>PANDA_SCAN_VERIFY</c>. Carries the verify scanner's read as a single
/// pipe-delimited label buffer (e.g. <c>123456798|0154006001</c>). Field layout (after the code):
/// SourceMode, SorterNumber, SorterMode, DeviceId, SeqNum, LabelBuffer, Length.
/// Mapping buffer positions to label types uses the line's <c>LabelBufferOrder</c> (port of
/// <c>Settings_LabelBufferOrder</c>), configurable per line.
/// </summary>
/// <param name="SourceMode">param02 — application database.</param>
/// <param name="SorterNumber">param03 — sorter / lane number.</param>
/// <param name="SorterMode">param04 — sorter mode.</param>
/// <param name="DeviceId">param05 — verify scanner device (&gt;1).</param>
/// <param name="SeqNum">param06 — rolling carton sequence number.</param>
/// <param name="LabelBuffer">param07 — pipe-delimited scanned barcode values.</param>
public sealed record VerifyScanMessage(
    int SourceMode,
    int SorterNumber,
    int SorterMode,
    int DeviceId,
    int SeqNum,
    IReadOnlyList<string> LabelBuffer)
{
    public const int Code = 286;

    public PaFrame ToFrame() => new(Code,
    [
        SourceMode.ToString(),
        SorterNumber.ToString(),
        SorterMode.ToString(),
        DeviceId.ToString(),
        SeqNum.ToString(),
        string.Join('|', LabelBuffer),
        "0", // Length
    ]);

    public static VerifyScanMessage FromFrame(PaFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Code != Code)
        {
            throw new FormatException($"Expected code {Code} but got {frame.Code}.");
        }

        return new VerifyScanMessage(
            SourceMode: int.Parse(frame.Field(2)),
            SorterNumber: int.Parse(frame.Field(3)),
            SorterMode: int.Parse(frame.Field(4)),
            DeviceId: int.Parse(frame.Field(5)),
            SeqNum: int.Parse(frame.Field(6)),
            LabelBuffer: [.. frame.Field(7).Split('|')]);
    }
}
