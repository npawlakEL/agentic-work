using PandA.Core.Labels;

namespace PandA.Sim;

public sealed class InMemoryLabelTemplateRepository : ILabelTemplateRepository
{
    public static IReadOnlyList<LabelTemplate> SeedTemplates { get; } =
    [
        new("ScanError_NoRead", "^XA^FO50,50^FDScanError^FS^FO50,100^FDNoRead^FS^FO50,150^FDCartonID = <CartonID>^FS^XZ", true),
        new("ScanError_NoData", "^XA^FO50,50^FDScanError^FS^FO50,100^FDNoData^FS^FO50,150^FDCartonID = <CartonID>^FS^XZ", true),
        new("DataError_NoInfo", "^XA^FO50,50^FDDataError^FS^FO50,100^FDNoInfo^FS^FO50,150^FDCartonID = <CartonID>^FS^BY3,2,170^FO100,750^BC^FD<LPN>^FS^XZ", true),
        new("DataError_Duplicate", "^XA^FO50,50^FDDataError^FS^FO50,100^FDDuplicate^FS^FO50,150^FDCartonID = <CartonID>^FS^BY3,2,170^FO100,750^BC^FD<LPN>^FS^XZ", true),
        new("Not Received", "^XA^FO50,50^FDNot ^FS^FO50,100^FDReceived^FS^FO50,150^FDCartonID = <CartonID>^FS^XZ", true),
        new("DataError_LabelConflict", "^XA^FO50,50^FDDataError^FS^FO50,100^FDLabelConflict^FS^FO50,150^FDCartonID = <CartonID>^FS^XZ", true),
        new("DataMismatch", "^XA^FO50,50^FDDataMismatch^FS^FO50,100^FDNoData^FS^XZ", true),
        new("ScanError_Conflict", "^XA^FO50,50^FDLabelConflict^FS^FO50,100^FDScanError^FS^FO50,150^FDCartonID = <CartonID>^FS^XZ", true)
    ];

    private readonly IReadOnlyList<LabelTemplate> _templates;

    public InMemoryLabelTemplateRepository()
        : this(SeedTemplates)
    {
    }

    public InMemoryLabelTemplateRepository(IEnumerable<LabelTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);

        _templates = [.. templates];
    }

    public ValueTask<LabelTemplate?> FindActiveAsync(string labelType, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(labelType);
        ct.ThrowIfCancellationRequested();

        var template = _templates.FirstOrDefault(template =>
            template.Active && string.Equals(template.LabelType, labelType, StringComparison.OrdinalIgnoreCase));

        return ValueTask.FromResult(template);
    }
}
