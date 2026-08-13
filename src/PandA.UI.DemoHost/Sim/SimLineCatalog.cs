using System.Collections.Concurrent;
using PandA.Sim.Line;

namespace PandA.UI.DemoHost.Sim;

/// <summary>
/// Builds and caches one <see cref="LineSimulation"/> per configured line so the simulator page
/// can switch between lines. Each line's sim is lazily created on first request and reused.
/// </summary>
public sealed class SimLineCatalog
{
    private readonly DemoDataStore _store;
    private readonly ConcurrentDictionary<string, LineSimulation> _sims = new(StringComparer.OrdinalIgnoreCase);

    public SimLineCatalog(DemoDataStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Lines = LineSimulationFactory.Lines(store);
        DefaultLineId = Lines.Count > 0 ? Lines[0].Id : string.Empty;
    }

    public IReadOnlyList<(string Id, string Name)> Lines { get; }

    public string DefaultLineId { get; }

    public LineSimulation Get(string? lineId)
    {
        var id = string.IsNullOrWhiteSpace(lineId) ? DefaultLineId : lineId;
        return _sims.GetOrAdd(id, key => LineSimulationFactory.Create(_store, key));
    }
}
