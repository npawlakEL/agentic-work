namespace PandA.Core.Labels;

/// <summary>
/// F10 exception-label source (decision-021). Produces the ZPL for a locally-generated exception label
/// given the exception reason, synthesized carton id, and optional LPN. Two concrete sources exist:
/// <list type="bullet">
///   <item><see cref="LocalTemplateSource"/> — renders from the local <see cref="ILabelTemplateRepository"/>
///   (the default, in-process source).</item>
///   <item>A DCMS/eHub-backed source (<c>DCMSExceptions = 1</c>) that requests the ZPL from an external
///   connector — bookmarked here as a port; the concrete transport is owned by the EController adapter.</item>
/// </list>
/// </summary>
public interface IExceptionLabelSource
{
    ValueTask<ExceptionBuildResult> BuildAsync(
        ExceptionType type,
        string cartonId,
        string? lpn,
        CancellationToken cancellationToken = default);
}
