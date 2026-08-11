namespace PandA.Core;

/// <summary>
/// The set of typed labels advised for one carton, stored as a Transport Order extension (decision-002 D7).
/// Replaces source PandA's 6 parallel label slots with a typed list.
/// </summary>
public sealed class PandaLabelSet
{
    private readonly List<Label> _labels;

    public PandaLabelSet(IEnumerable<Label> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        _labels = [.. labels];
    }

    public IReadOnlyList<Label> Labels => _labels;

    /// <summary>Distinct label types present, in first-seen order.</summary>
    public IReadOnlyList<string> DistinctLabelTypes()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var label in _labels)
        {
            if (seen.Add(label.LabelType))
            {
                result.Add(label.LabelType);
            }
        }

        return result;
    }
}
