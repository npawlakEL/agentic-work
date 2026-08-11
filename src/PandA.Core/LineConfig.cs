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
        string? placeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        ArgumentNullException.ThrowIfNull(printers);

        LineId = lineId;
        Printers = [.. printers];
        LoadBalance = loadBalance;
        PlaceId = placeId;
    }

    public string LineId { get; }

    public string? PlaceId { get; }

    /// <summary>When true, distribute by least-recently-printed; when false, first-configured wins.</summary>
    public bool LoadBalance { get; }

    public IReadOnlyList<PrinterConfig> Printers { get; }
}
