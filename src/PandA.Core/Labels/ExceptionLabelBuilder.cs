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

        var zpl = template.Template.Replace("<CartonID>", EscapeFieldData(cartonId), StringComparison.Ordinal);

        if (ExceptionLabelPolicy.UsesLpn(type))
        {
            zpl = zpl.Replace("<LPN>", EscapeFieldData(lpn), StringComparison.Ordinal);
        }

        return ExceptionBuildResult.Success(zpl);
    }

    /// <summary>
    /// Neutralise a value substituted into ZPL field data: the raw scan text must not be able to inject ZPL
    /// commands, so the format/control prefixes <c>^</c> and <c>~</c> and any control characters are dropped.
    /// </summary>
    public static string EscapeFieldData(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '^' or '~' || char.IsControl(c))
            {
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
