namespace PandA.Core;

/// <summary>
/// Commissioning config for one printer on a line (from PandaLine.json). Static/authored data.
/// Runtime health (online/spare/last-printed) lives in <see cref="PrinterState"/>.
/// </summary>
public sealed class PrinterConfig
{
    public PrinterConfig(
        string printerId,
        string ip,
        int port,
        IEnumerable<string> labelMap,
        ApplyOrientation printerType = ApplyOrientation.Side,
        int configOrder = 0,
        int defaultApplyDistance = 0,
        decimal labelWidthInches = 4m)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(labelMap);
        if (labelWidthInches <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(labelWidthInches), labelWidthInches, "Label width must be greater than zero inches.");
        }

        PrinterId = printerId;
        Ip = ip;
        Port = port;
        LabelMap = [.. labelMap];
        PrinterType = printerType;
        ConfigOrder = configOrder;
        DefaultApplyDistance = defaultApplyDistance;
        LabelWidthInches = labelWidthInches;
    }

    /// <summary>
    /// DYNAP (decision-016): per-printer base apply offset in PLC pulses (source <c>PrinterDetails</c>
    /// <c>DefaultApplyDistance</c>). Added to every resolved apply pulse. Defaults to 0.
    /// </summary>
    public int DefaultApplyDistance { get; }

    /// <summary>DYNAP (decision-016): width of the label this printer applies, in inches. Defaults to 4.</summary>
    public decimal LabelWidthInches { get; }

    public string PrinterId { get; }

    public string Ip { get; }

    public int Port { get; }

    /// <summary>Label types this printer can emit (source LabelProfileMap → LabelDef.LabelName).</summary>
    public IReadOnlyList<string> LabelMap { get; }

    /// <summary>TOP/SIDE apply orientation (source PrinterDetails AttributeName='PrinterType').</summary>
    public ApplyOrientation PrinterType { get; }

    /// <summary>Position of this printer in the line's configured order; used as the load-balance tie-break.</summary>
    public int ConfigOrder { get; }

    public bool CanPrint(string labelType) =>
        LabelMap.Contains(labelType, StringComparer.OrdinalIgnoreCase);
}
