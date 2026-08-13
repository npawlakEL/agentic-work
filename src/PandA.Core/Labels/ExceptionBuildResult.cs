using PandA.Core;

namespace PandA.Core.Labels;

public sealed record ExceptionBuildResult(bool HasLabel, string? Zpl, string LabelType, string Lpn)
{
    public const string ExceptionLabelType = "Exception";
    public const string ExceptionLpn = "ExceptionLabel";

    public Label? Label => HasLabel && Zpl is not null ? new Label(LabelType, Lpn, Zpl) : null;

    public static ExceptionBuildResult NoTemplate() => new(false, null, ExceptionLabelType, ExceptionLpn);

    public static ExceptionBuildResult Success(string zpl)
    {
        ArgumentNullException.ThrowIfNull(zpl);

        return new(true, zpl, ExceptionLabelType, ExceptionLpn);
    }
}
