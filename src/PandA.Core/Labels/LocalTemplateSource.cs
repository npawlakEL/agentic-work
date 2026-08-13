namespace PandA.Core.Labels;

/// <summary>
/// Default in-process <see cref="IExceptionLabelSource"/> (decision-021): looks up the active exception
/// template for the reason and renders it via <see cref="ExceptionLabelBuilder"/>. Returns
/// <see cref="ExceptionBuildResult.NoTemplate"/> when no active template exists for the reason.
/// </summary>
public sealed class LocalTemplateSource : IExceptionLabelSource
{
    private readonly ILabelTemplateRepository _templates;
    private readonly ExceptionLabelBuilder _builder = new();

    public LocalTemplateSource(ILabelTemplateRepository templates)
    {
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    }

    public async ValueTask<ExceptionBuildResult> BuildAsync(
        ExceptionType type,
        string cartonId,
        string? lpn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cartonId);

        var tag = ExceptionLabelBuilder.TagFor(type);
        var template = tag is null
            ? null
            : await _templates.FindActiveAsync(tag, cancellationToken).ConfigureAwait(false);

        return _builder.Build(type, cartonId, lpn, template);
    }
}
