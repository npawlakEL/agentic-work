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
        int configOrder = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(labelMap);

        PrinterId = printerId;
        Ip = ip;
        Port = port;
        LabelMap = [.. labelMap];
        PrinterType = printerType;
        ConfigOrder = configOrder;
    }

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
