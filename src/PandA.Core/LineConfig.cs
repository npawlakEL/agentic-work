namespace PandA.Core;

/// <summary>
/// Commissioning config for one PandA line (an entry in PandaLine.json). Multi-line capable by design;
/// Phase-1 tests exercise a single line (spec §6a).
/// </summary>
public sealed class LineConfig
{
    public LineConfig(
        string lineId,
        IEnumerable<PrinterConfig> printers,
        bool loadBalance = true,
        string? placeId = null,
        LabelBufferOrder? bufferOrder = null,
        FirePointProfile? activeProfile = null,
        IReadOnlyDictionary<ApplyOrientation, PrinterGroupPolicy>? printerPolicies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        ArgumentNullException.ThrowIfNull(printers);

        LineId = lineId;
        Printers = [.. printers];
        LoadBalance = loadBalance;
        PlaceId = placeId;
        BufferOrder = bufferOrder ?? LabelBufferOrder.Default;
        ActiveProfile = activeProfile;
        PrinterPolicies = printerPolicies ?? new Dictionary<ApplyOrientation, PrinterGroupPolicy>();
    }

    public string LineId { get; }

    public string? PlaceId { get; }

    /// <summary>When true, distribute by least-recently-printed; when false, first-configured wins.</summary>
    public bool LoadBalance { get; }

    /// <summary>Per-line scanner-buffer layout (position → label type) used to type verify reads.</summary>
    public LabelBufferOrder BufferOrder { get; }

    /// <summary>
    /// The line's active fire-point profile (per-printer-per-label print/apply firing points).
    /// This slice carries a single static generic profile; profile switching / host-driven
    /// selection are backlog (see architecture-log 010). Null when no profile is configured.
    /// </summary>
    public FirePointProfile? ActiveProfile { get; }

    public IReadOnlyList<PrinterConfig> Printers { get; }

    /// <summary>
    /// Per-orientation printer thresholds (source <c>PandADetails</c> OnlinePrinterMin/PrinterCount).
    /// Consumed by lane evaluation to maintain spares and slow/shut the line. Empty when unconfigured.
    /// </summary>
    public IReadOnlyDictionary<ApplyOrientation, PrinterGroupPolicy> PrinterPolicies { get; }
}
