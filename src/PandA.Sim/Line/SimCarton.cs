using PandA.Core;

namespace PandA.Sim.Line;

public enum SimCartonState
{
    Moving,
    Scanned,
    Printed,
    Applied,
    Verified,
    Rejected,
}

public sealed class SimCarton
{
    private readonly HashSet<string> _crossedEyes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LabelPlacementSnapshot> _labels = [];

    public SimCarton(string cartonId, string blindLabel, TransportOrder order)
    {
        CartonId = cartonId;
        BlindLabel = blindLabel;
        Order = order;
    }

    public string CartonId { get; }

    public string BlindLabel { get; }

    public TransportOrder Order { get; }

    public double PositionInches { get; set; }

    public double PreviousPositionInches { get; set; }

    public double LengthInches { get; init; } = 24;

    public double WidthInches { get; init; } = 16;

    public double HeightInches { get; init; } = 12;

    public SimCartonState State { get; set; } = SimCartonState.Moving;

    public IReadOnlyList<LabelPlacementSnapshot> Labels => _labels;

    public bool HasCrossed(string eyeId) => _crossedEyes.Contains(eyeId);

    public void MarkCrossed(string eyeId) => _crossedEyes.Add(eyeId);

    public void ReplaceLabels(IEnumerable<LabelPlacementSnapshot> labels)
    {
        _labels.Clear();
        _labels.AddRange(labels);
    }

    public void MutateLabels(Func<LabelPlacementSnapshot, LabelPlacementSnapshot> transform)
    {
        for (var i = 0; i < _labels.Count; i++)
        {
            _labels[i] = transform(_labels[i]);
        }
    }
}
