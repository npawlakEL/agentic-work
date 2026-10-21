using System.Collections.Concurrent;
using PandA.Sim.Line;

namespace PandA.UI.DemoHost.Sim;

/// <summary>Result of looking up a line's simulation: either a simulation or a human-readable reason there is none.</summary>
public sealed record SimLineLookup(LineSimulation? Simulation, string? Error)
{
    public bool Found => Simulation is not null;
}

/// <summary>
/// Builds and caches one <see cref="LineSimulation"/> per configured line so the simulator page
/// can switch between lines. Lines are read live from the store; a cached sim is rebuilt whenever
/// its line's config version changes (line, printers, maps, fire points, label defs, settings).
/// </summary>
public sealed class SimLineCatalog
{
    private readonly DemoDataStore _store;
    private readonly ConcurrentDictionary<string, (LineSimulation Sim, long Version)> _sims = new(StringComparer.OrdinalIgnoreCase);

    public SimLineCatalog(DemoDataStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>Currently configured lines (live), ordered by name.</summary>
    public IReadOnlyList<(string Id, string Name)> Lines => LineSimulationFactory.Lines(_store);

    /// <summary>First line by name, or empty when no lines are configured.</summary>
    public string DefaultLineId => Lines is { Count: > 0 } lines ? lines[0].Id : string.Empty;

    /// <summary>Gets (building or rebuilding as needed) the simulation for a line; never throws for a missing/broken line.</summary>
    public SimLineLookup TryGet(string? lineId)
    {
        var id = string.IsNullOrWhiteSpace(lineId) ? DefaultLineId : lineId;
        if (string.IsNullOrEmpty(id))
        {
            return new(null, "No lines are configured. Add a lane on the Config page.");
        }

        if (!_store.Lines.ContainsKey(id))
        {
            _sims.TryRemove(id, out _);
            return new(null, $"Line '{id}' no longer exists.");
        }

        var version = _store.ConfigVersionFor(id);
        if (_sims.TryGetValue(id, out var cached) && cached.Version == version)
        {
            return new(cached.Sim, null);
        }

        try
        {
            var sim = LineSimulationFactory.Create(_store, id);
            _sims[id] = (sim, version);
            return new(sim, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _sims.TryRemove(id, out _);
            return new(null, $"Line '{id}' cannot be simulated: {ex.Message}");
        }
    }

    /// <summary>Gets the simulation for a line; throws <see cref="KeyNotFoundException"/> when there is none.</summary>
    public LineSimulation Get(string? lineId)
    {
        var lookup = TryGet(lineId);
        return lookup.Simulation ?? throw new KeyNotFoundException(lookup.Error);
    }

    /// <summary>Drops a line's cached simulation so the next request rebuilds it from current config.</summary>
    public void Invalidate(string lineId) => _sims.TryRemove(lineId, out _);
}
