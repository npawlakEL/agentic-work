namespace PandA.Core.Labels;

public sealed class ExceptionLabelBuilder
{
    public static string? TagFor(ExceptionType type) =>
        type switch
        {
            ExceptionType.NotReceived => "Not Received",
            ExceptionType.Duplicate => "DataError_Duplicate",
            ExceptionType.NoInformation => "DataError_NoInfo",
            ExceptionType.NoRead => "ScanError_NoRead",
            ExceptionType.NoData => "ScanError_NoData",
            ExceptionType.LabelConflict => "DataError_LabelConflict",
            ExceptionType.DataMismatch => "DataMismatch",
            _ => null
        };

    public ExceptionBuildResult Build(ExceptionType type, string cartonId, string? lpn, LabelTemplate? template)
    {
        ArgumentNullException.ThrowIfNull(cartonId);

        if (template is null || TagFor(type) is null)
        {
            return ExceptionBuildResult.NoTemplate();
        }

        var zpl = template.Template.Replace("<CartonID>", cartonId, StringComparison.Ordinal);

        if (UsesLpn(type))
        {
            zpl = zpl.Replace("<LPN>", lpn ?? string.Empty, StringComparison.Ordinal);
        }

        return ExceptionBuildResult.Success(zpl);
    }

    private static bool UsesLpn(ExceptionType type) =>
        type is ExceptionType.Duplicate or ExceptionType.NoInformation;
}
