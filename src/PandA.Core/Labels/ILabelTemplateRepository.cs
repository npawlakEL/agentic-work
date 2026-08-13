namespace PandA.Core.Labels;

public interface ILabelTemplateRepository
{
    ValueTask<LabelTemplate?> FindActiveAsync(string labelType, CancellationToken ct = default);
}
