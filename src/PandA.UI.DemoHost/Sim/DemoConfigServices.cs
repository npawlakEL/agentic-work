using System.Collections.Concurrent;
using PandA.UI.Contracts.Common;
using PandA.UI.Contracts.Config;

namespace PandA.UI.DemoHost.Sim;

/// <summary>Sim implementation of the Config Explorer tree.</summary>
public sealed class DemoConfigTreeQuery(DemoDataStore store) : IConfigTreeQuery
{
    public Task<ConfigTreeNode> GetTreeAsync(CancellationToken ct = default)
    {
        var labels = store.LabelDefs.Values
            .OrderBy(l => l.Name, StringComparer.Ordinal)
            .Select(l => new ConfigTreeNode(ConfigNodeKind.LabelDefinition, l.Name, l.LabelDefId, []))
            .ToList();

        var stations = store.Stations.Values
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => new ConfigTreeNode(ConfigNodeKind.MandaStation, s.Name, s.StationId, []))
            .ToList();

        var lines = store.Lines.Values
            .OrderBy(l => l.Name, StringComparer.Ordinal)
            .Select(BuildLineNode)
            .ToList();

        var firePoints = store.FirePoints.Values
            .OrderBy(f => LabelName(f.LabelDefId), StringComparer.Ordinal)
            .ThenBy(f => f.ApplyPointNotation, StringComparer.Ordinal)
            .Select(f => new ConfigTreeNode(ConfigNodeKind.FirePoint, $"{LabelName(f.LabelDefId)} ({f.ApplyPointNotation})", f.FirePointId, []))
            .ToList();

        var root = new ConfigTreeNode(ConfigNodeKind.Root, "System", null,
        [
            new ConfigTreeNode(ConfigNodeKind.SettingsGroup, "Settings", "settings", []),
            ConfigTreeNode.Group(ConfigNodeKind.LabelDefinitions, "Label Definitions", labels),
            ConfigTreeNode.Group(ConfigNodeKind.FirePoints, "Fire Points", firePoints),
            ConfigTreeNode.Group(ConfigNodeKind.MandaStations, "MandA Stations", stations),
            ConfigTreeNode.Group(ConfigNodeKind.Lines, "Lines", lines),
        ]);

        return Task.FromResult(root);
    }

    private ConfigTreeNode BuildLineNode(LineDto line)
    {
        var printers = store.Printers.Values
            .Where(p => string.Equals(p.LineId, line.LineId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.ConfigOrder)
            .Select(BuildPrinterNode)
            .ToList();

        var maps = store.Maps.Values
            .Where(m => string.Equals(m.LineId, line.LineId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .Select(m => new ConfigTreeNode(ConfigNodeKind.Map, m.Name, m.MapId, []))
            .ToList();

        return new ConfigTreeNode(ConfigNodeKind.Line, line.Name, line.LineId,
        [
            ConfigTreeNode.Group(ConfigNodeKind.Printers, "Printers", printers),
            ConfigTreeNode.Group(ConfigNodeKind.Maps, "Maps", maps),
        ]);
    }

    private ConfigTreeNode BuildPrinterNode(PrinterDto printer) =>
        new(ConfigNodeKind.Printer, printer.Name, printer.PrinterId, []);

    private string LabelName(string labelDefId) =>
        store.LabelDefs.TryGetValue(labelDefId, out var def) ? def.Name : labelDefId;
}

/// <summary>Sim settings editor (single record).</summary>
public sealed class DemoSettingsEditor(DemoDataStore store) : ISettingsEditor
{
    public Task<SettingsDto> GetAsync(CancellationToken ct = default) => Task.FromResult(store.Settings);

    public Task<CommandResult> SaveAsync(SettingsDto settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.VerifyFailThreshold < 1)
        {
            return Task.FromResult(CommandResult.Fail("Verify fail threshold must be at least 1."));
        }

        store.Settings = settings;

        // Global encoder resolution propagates to every line (lines default from the global value).
        foreach (var line in store.Lines.Values.ToList())
        {
            if (Math.Abs(line.EncoderResolutionInchesPerPulse - settings.EncoderResolutionInchesPerPulse) > double.Epsilon)
            {
                store.Lines[line.LineId!] = line with { EncoderResolutionInchesPerPulse = settings.EncoderResolutionInchesPerPulse };
            }
        }

        return Task.FromResult(CommandResult.Ok("Settings saved."));
    }
}

/// <summary>Base in-memory CRUD editor over a store dictionary.</summary>
public abstract class DemoConfigEditor<TDto>(ConcurrentDictionary<string, TDto> items, string idPrefix, DemoDataStore store)
    : IConfigEditor<TDto>
    where TDto : class
{
    protected DemoDataStore Store { get; } = store;

    public Task<IReadOnlyList<TDto>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TDto>>(items.Values.ToList());

    public Task<TDto?> GetAsync(string id, CancellationToken ct = default)
    {
        items.TryGetValue(id, out var dto);
        return Task.FromResult(dto);
    }

    public Task<CommandResult> SaveAsync(TDto entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (Validate(entity) is { } failure)
        {
            return Task.FromResult(failure);
        }

        var id = GetId(entity);
        if (string.IsNullOrEmpty(id))
        {
            id = Store.NextId(idPrefix);
            entity = WithId(entity, id);
        }

        items[id] = entity;
        return Task.FromResult(CommandResult.Ok("Saved."));
    }

    /// <summary>Optional pre-save validation; return a failure to block the save, or null to allow.</summary>
    protected virtual CommandResult? Validate(TDto entity) => null;

    public Task<CommandResult> DeleteAsync(string id, CancellationToken ct = default) =>
        Task.FromResult(items.TryRemove(id, out _)
            ? CommandResult.Ok("Deleted.")
            : CommandResult.Fail($"'{id}' not found."));

    protected abstract string? GetId(TDto entity);

    protected abstract TDto WithId(TDto entity, string id);
}

public sealed class DemoLabelDefEditor(DemoDataStore store)
    : DemoConfigEditor<LabelDefDto>(store.LabelDefs, "label", store), ILabelDefEditor
{
    protected override string? GetId(LabelDefDto e) => e.LabelDefId;

    protected override LabelDefDto WithId(LabelDefDto e, string id) => e with { LabelDefId = id };
}

public sealed class DemoOrientationEditor(DemoDataStore store)
    : DemoConfigEditor<OrientationDto>(store.Orientations, "orient", store), IOrientationEditor
{
    protected override string? GetId(OrientationDto e) => e.OrientationId;

    protected override OrientationDto WithId(OrientationDto e, string id) => e with { OrientationId = id };
}

public sealed class DemoMandaStationEditor(DemoDataStore store)
    : DemoConfigEditor<MandaStationDto>(store.Stations, "manda", store), IMandaStationEditor
{
    protected override string? GetId(MandaStationDto e) => e.StationId;

    protected override MandaStationDto WithId(MandaStationDto e, string id) => e with { StationId = id };
}

public sealed class DemoLineEditor(DemoDataStore store)
    : DemoConfigEditor<LineDto>(store.Lines, "line", store), ILineEditor
{
    protected override string? GetId(LineDto e) => e.LineId;

    protected override LineDto WithId(LineDto e, string id) => e with { LineId = id };
}

public sealed class DemoPrinterEditor(DemoDataStore store)
    : DemoConfigEditor<PrinterDto>(store.Printers, "printer", store), IPrinterEditor
{
    protected override string? GetId(PrinterDto e) => e.PrinterId;

    protected override PrinterDto WithId(PrinterDto e, string id) => e with { PrinterId = id };

    public Task<IReadOnlyList<PrinterDto>> ListForLineAsync(string lineId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PrinterDto>>(Store.Printers.Values
            .Where(p => string.Equals(p.LineId, lineId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.ConfigOrder)
            .ToList());
}

public sealed class DemoFirePointEditor(DemoDataStore store)
    : DemoConfigEditor<FirePointDto>(store.FirePoints, "fp", store), IFirePointEditor
{
    protected override string? GetId(FirePointDto e) => e.FirePointId;

    protected override FirePointDto WithId(FirePointDto e, string id) => e with { FirePointId = id };

    // A fire point must be unique across the system on (label definition + apply point).
    protected override CommandResult? Validate(FirePointDto e)
    {
        var dup = Store.FirePoints.Values.FirstOrDefault(f =>
            !string.Equals(f.FirePointId, e.FirePointId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(f.LabelDefId, e.LabelDefId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(f.ApplyPointNotation, e.ApplyPointNotation, StringComparison.OrdinalIgnoreCase));
        if (dup is not null)
        {
            var label = Store.LabelDefs.TryGetValue(e.LabelDefId, out var l) ? l.Name : e.LabelDefId;
            return CommandResult.Fail($"A fire point for {label} ({e.ApplyPointNotation}) already exists.");
        }

        return null;
    }
}

public sealed class DemoMapEditor(DemoDataStore store)
    : DemoConfigEditor<MapDto>(store.Maps, "map", store), IMapEditor
{
    protected override string? GetId(MapDto e) => e.MapId;

    protected override MapDto WithId(MapDto e, string id) => e with { MapId = id };

    public Task<IReadOnlyList<MapDto>> ListForLineAsync(string lineId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MapDto>>(Store.Maps.Values
            .Where(m => string.Equals(m.LineId, lineId, StringComparison.OrdinalIgnoreCase))
            .ToList());
}
