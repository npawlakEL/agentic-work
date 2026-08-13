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
        IReadOnlyDictionary<ApplyOrientation, PrinterGroupPolicy>? printerPolicies = null,
        IEnumerable<LaneDef>? lanes = null,
        bool printerStatusSuffix = false,
        string? plcZone = null,
        int? sorterPlcRecId = null,
        string? plcDbName = null,
        decimal encoderResolution = 0.2m,
        bool dynamicPrintPoint = true,
        IReadOnlyDictionary<string, FirePointProfile>? profileRegistry = null,
        string? defaultProfile = null,
        bool filterLabels = true)
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
        Lanes = lanes is null ? [] : [.. lanes];
        PrinterStatusSuffix = printerStatusSuffix;
        PlcZone = plcZone;
        SorterPlcRecId = sorterPlcRecId;
        PlcDbName = plcDbName;
        EncoderResolution = encoderResolution;
        DynamicPrintPoint = dynamicPrintPoint;
        ProfileRegistry = profileRegistry ?? new Dictionary<string, FirePointProfile>(StringComparer.OrdinalIgnoreCase);
        DefaultProfile = defaultProfile;
        FilterLabels = filterLabels;
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

    public IReadOnlyList<LaneDef> Lanes { get; }

    public bool PrinterStatusSuffix { get; }

    public string? PlcZone { get; }

    public int? SorterPlcRecId { get; }

    public string? PlcDbName { get; }

    public decimal EncoderResolution { get; }

    public bool DynamicPrintPoint { get; }

    public IReadOnlyDictionary<string, FirePointProfile> ProfileRegistry { get; }

    public string? DefaultProfile { get; }

    public bool FilterLabels { get; }
}
