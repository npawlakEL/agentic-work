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
            .OrderBy(p => p.PlcNumber)
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

        lock (store.SyncRoot)
        {
            store.Settings = settings;

            // Global encoder resolution propagates to every line (lines default from the global value).
            foreach (var line in store.Lines.Values.ToList())
            {
                if (Math.Abs(line.EncoderResolutionInchesPerPulse - settings.EncoderResolutionInchesPerPulse) > double.Epsilon)
                {
                    store.Lines[line.LineId!] = line with { EncoderResolutionInchesPerPulse = settings.EncoderResolutionInchesPerPulse };
                }
            }

            store.MarkConfigChanged();
        }

        return Task.FromResult(CommandResult.Ok("Settings saved."));
    }
}

/// <summary>
/// Base in-memory CRUD editor over a store dictionary. Validate-then-write and delete run under the
/// store-wide <see cref="DemoDataStore.SyncRoot"/> so concurrent circuits cannot interleave.
/// Deletes REFUSE (with a clear message) while the entity is still referenced; the only cascading
/// delete is the explicit <see cref="DemoLineEditor.DeleteWithDependentsAsync"/>.
/// </summary>
public abstract class DemoConfigEditor<TDto>(ConcurrentDictionary<string, TDto> items, string idPrefix, DemoDataStore store)
    : IConfigEditor<TDto>
    where TDto : class
{
    protected DemoDataStore Store { get; } = store;

    protected ConcurrentDictionary<string, TDto> Items { get; } = items;

    public Task<IReadOnlyList<TDto>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TDto>>(Items.Values.ToList());

    public Task<TDto?> GetAsync(string id, CancellationToken ct = default)
    {
        Items.TryGetValue(id, out var dto);
        return Task.FromResult(dto);
    }

    public Task<CommandResult> SaveAsync(TDto entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        lock (Store.SyncRoot)
        {
            if (Validate(entity) is { } failure)
            {
                return Task.FromResult(failure);
            }

            var id = GetId(entity);
            TDto? previous = null;
            if (string.IsNullOrEmpty(id))
            {
                id = Store.NextId(idPrefix);
                entity = WithId(entity, id);
            }
            else
            {
                Items.TryGetValue(id, out previous);
            }

            Items[id] = entity;
            OnSaved(previous, entity);
            return Task.FromResult(CommandResult.Ok("Saved.", id));
        }
    }

    public Task<CommandResult> DeleteAsync(string id, CancellationToken ct = default)
    {
        lock (Store.SyncRoot)
        {
            if (string.IsNullOrEmpty(id) || !Items.TryGetValue(id, out var existing))
            {
                return Task.FromResult(CommandResult.Fail($"'{id}' not found."));
            }

            if (ValidateDelete(existing) is { } failure)
            {
                return Task.FromResult(failure);
            }

            Items.TryRemove(id, out _);
            OnDeleted(existing);
            return Task.FromResult(CommandResult.Ok("Deleted."));
        }
    }

    /// <summary>Optional pre-save validation (runs under the store lock); return a failure to block the save, or null to allow.</summary>
    protected virtual CommandResult? Validate(TDto entity) => null;

    /// <summary>Optional pre-delete reference check (runs under the store lock); return a failure to refuse the delete.</summary>
    protected virtual CommandResult? ValidateDelete(TDto existing) => null;

    /// <summary>Post-write hook (under the store lock). Defaults to invalidating every line's sim.</summary>
    protected virtual void OnSaved(TDto? previous, TDto saved) => Store.MarkConfigChanged();

    /// <summary>Post-delete hook (under the store lock). Defaults to invalidating every line's sim.</summary>
    protected virtual void OnDeleted(TDto removed) => Store.MarkConfigChanged();

    /// <summary>The stored entity with the same id as <paramref name="entity"/> (null when creating).</summary>
    protected TDto? Existing(TDto entity) =>
        GetId(entity) is { Length: > 0 } id && Items.TryGetValue(id, out var existing) ? existing : null;

    protected abstract string? GetId(TDto entity);

    protected abstract TDto WithId(TDto entity, string id);
}

/// <summary>Shared helpers for config uniqueness and reference checks.</summary>
internal static class ConfigValidation
{
    public static readonly IReadOnlyList<string> ApplyEdges = ["Leading", "Trailing", "Middle"];

    public static bool NameEquals(string? a, string? b) =>
        string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool IdEquals(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The line the map is currently active on, if any.</summary>
    public static LineDto? ActiveOn(DemoDataStore store, string? mapId) =>
        string.IsNullOrEmpty(mapId) ? null : store.Lines.Values.FirstOrDefault(l => IdEquals(l.ActiveMapId, mapId));

    /// <summary>Maps whose assignments reference the given fire point / printer.</summary>
    public static List<MapDto> MapsReferencing(DemoDataStore store, string? firePointId = null, string? printerId = null) =>
        store.Maps.Values
            .Where(m => m.Assignments.Any(a =>
                (firePointId is not null && IdEquals(a.FirePointId, firePointId)) ||
                (printerId is not null && a.PrinterIds.Any(p => IdEquals(p, printerId)))))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

    public static string NameList(IEnumerable<string> names) =>
        string.Join(", ", names.Select(n => $"'{n}'"));
}

public sealed class DemoLabelDefEditor(DemoDataStore store)
    : DemoConfigEditor<LabelDefDto>(store.LabelDefs, "label", store), ILabelDefEditor
{
    protected override string? GetId(LabelDefDto e) => e.LabelDefId;

    protected override LabelDefDto WithId(LabelDefDto e, string id) => e with { LabelDefId = id };

    // Label-definition names are the buffer/verify keys, so they must be unique across the system.
    protected override CommandResult? Validate(LabelDefDto e)
    {
        if (string.IsNullOrWhiteSpace(e.Name))
        {
            return CommandResult.Fail("Label definition name is required.");
        }

        if (Store.LabelDefs.Values.Any(l => !ConfigValidation.IdEquals(l.LabelDefId, e.LabelDefId) && ConfigValidation.NameEquals(l.Name, e.Name)))
        {
            return CommandResult.Fail($"A label definition named '{e.Name.Trim()}' already exists.");
        }

        return null;
    }

    // Renaming cascades into every line's buffer order (config-only; carton runtime data keeps its old name).
    protected override void OnSaved(LabelDefDto? previous, LabelDefDto saved)
    {
        if (previous is not null && !string.Equals(previous.Name.Trim(), saved.Name.Trim(), StringComparison.Ordinal))
        {
            var oldName = previous.Name.Trim();
            var newName = saved.Name.Trim();
            foreach (var line in Store.Lines.Values.ToList())
            {
                if (line.BufferOrder.Any(t => ConfigValidation.NameEquals(t, oldName)))
                {
                    Store.Lines[line.LineId!] = line with
                    {
                        BufferOrder = line.BufferOrder.Select(t => ConfigValidation.NameEquals(t, oldName) ? newName : t).ToList(),
                    };
                }
            }
        }

        Store.MarkConfigChanged();
    }

    protected override CommandResult? ValidateDelete(LabelDefDto existing)
    {
        var firePoints = Store.FirePoints.Values.Where(f => ConfigValidation.IdEquals(f.LabelDefId, existing.LabelDefId)).ToList();
        if (firePoints.Count > 0)
        {
            return CommandResult.Fail(
                $"Label definition '{existing.Name}' is used by {firePoints.Count} fire point(s) ({ConfigValidation.NameList(firePoints.Select(f => f.ApplyPointNotation))}). Delete those fire points first.");
        }

        var lines = Store.Lines.Values.Where(l => l.BufferOrder.Any(t => ConfigValidation.NameEquals(t, existing.Name))).ToList();
        if (lines.Count > 0)
        {
            return CommandResult.Fail(
                $"Label definition '{existing.Name}' is in the buffer order of {ConfigValidation.NameList(lines.Select(l => l.Name))}. Remove it from those lanes first.");
        }

        return null;
    }
}

public sealed class DemoOrientationEditor(DemoDataStore store)
    : DemoConfigEditor<OrientationDto>(store.Orientations, "orient", store), IOrientationEditor
{
    protected override string? GetId(OrientationDto e) => e.OrientationId;

    protected override OrientationDto WithId(OrientationDto e, string id) => e with { OrientationId = id };

    protected override CommandResult? Validate(OrientationDto e)
    {
        if (string.IsNullOrWhiteSpace(e.Name))
        {
            return CommandResult.Fail("Orientation name is required.");
        }

        if (Store.Orientations.Values.Any(o => !ConfigValidation.IdEquals(o.OrientationId, e.OrientationId) && ConfigValidation.NameEquals(o.Name, e.Name)))
        {
            return CommandResult.Fail($"An orientation named '{e.Name.Trim()}' already exists.");
        }

        return null;
    }

    protected override CommandResult? ValidateDelete(OrientationDto existing)
    {
        var printers = Store.Printers.Values.Where(p => ConfigValidation.IdEquals(p.OrientationId, existing.OrientationId)).ToList();
        return printers.Count > 0
            ? CommandResult.Fail(
                $"Orientation '{existing.Name}' is used by {printers.Count} printer(s) ({ConfigValidation.NameList(printers.Select(p => p.Name))}). Change those printers first.")
            : null;
    }
}

public sealed class DemoMandaStationEditor(DemoDataStore store)
    : DemoConfigEditor<MandaStationDto>(store.Stations, "manda", store), IMandaStationEditor
{
    protected override string? GetId(MandaStationDto e) => e.StationId;

    protected override MandaStationDto WithId(MandaStationDto e, string id) => e with { StationId = id };

    // MandA station names must be unique across the system.
    protected override CommandResult? Validate(MandaStationDto e)
    {
        if (string.IsNullOrWhiteSpace(e.Name))
        {
            return CommandResult.Fail("MandA station name is required.");
        }

        if (Store.Stations.Values.Any(s => !ConfigValidation.IdEquals(s.StationId, e.StationId) && ConfigValidation.NameEquals(s.Name, e.Name)))
        {
            return CommandResult.Fail($"A MandA station named '{e.Name.Trim()}' already exists.");
        }

        return null;
    }

    // Stations are not part of any line's simulation.
    protected override void OnSaved(MandaStationDto? previous, MandaStationDto saved)
    {
    }

    protected override void OnDeleted(MandaStationDto removed)
    {
    }
}

public sealed class DemoLineEditor(DemoDataStore store)
    : DemoConfigEditor<LineDto>(store.Lines, "line", store), ILineEditor
{
    protected override string? GetId(LineDto e) => e.LineId;

    protected override LineDto WithId(LineDto e, string id) => e with { LineId = id };

    // Lines must have a unique name and a unique PLC number across the system; the active map must be one of
    // the line's own maps, and the buffer order may only name existing label definitions (once each).
    protected override CommandResult? Validate(LineDto e)
    {
        if (string.IsNullOrWhiteSpace(e.Name))
        {
            return CommandResult.Fail("Line name is required.");
        }

        if (e.PlcNumber < 1)
        {
            return CommandResult.Fail("Line PLC Number must be at least 1.");
        }

        var others = Store.Lines.Values.Where(l => !ConfigValidation.IdEquals(l.LineId, e.LineId)).ToList();

        if (others.Any(l => ConfigValidation.NameEquals(l.Name, e.Name)))
        {
            return CommandResult.Fail($"A line named '{e.Name.Trim()}' already exists.");
        }

        if (others.Any(l => l.PlcNumber == e.PlcNumber))
        {
            return CommandResult.Fail($"PLC Number {e.PlcNumber} is already used by another line.");
        }

        if (!string.IsNullOrEmpty(e.ActiveMapId))
        {
            if (!Store.Maps.TryGetValue(e.ActiveMapId, out var map))
            {
                return CommandResult.Fail($"Active map '{e.ActiveMapId}' does not exist.");
            }

            if (string.IsNullOrEmpty(e.LineId) || !ConfigValidation.IdEquals(map.LineId, e.LineId))
            {
                return CommandResult.Fail($"Map '{map.Name}' belongs to another line and cannot be active on '{e.Name.Trim()}'.");
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in e.BufferOrder)
        {
            var name = (tag ?? string.Empty).Trim();
            if (!Store.LabelDefs.Values.Any(l => ConfigValidation.NameEquals(l.Name, name)))
            {
                return CommandResult.Fail($"Buffer order entry '{name}' is not a label definition.");
            }

            if (!seen.Add(name))
            {
                return CommandResult.Fail($"Buffer order lists '{name}' more than once.");
            }
        }

        return null;
    }

    protected override void OnSaved(LineDto? previous, LineDto saved) => Store.MarkConfigChanged(saved.LineId);

    protected override CommandResult? ValidateDelete(LineDto existing)
    {
        var printers = PrintersOf(existing.LineId!).Count;
        var maps = MapsOf(existing.LineId!).Count;
        return printers + maps > 0
            ? CommandResult.Fail(
                $"Line '{existing.Name}' still has {printers} printer(s) and {maps} map(s). Delete the lane with its printers and maps, or remove them first.")
            : null;
    }

    protected override void OnDeleted(LineDto removed) => Store.MarkConfigChanged(removed.LineId);

    public Task<CommandResult> DeleteWithDependentsAsync(string lineId, CancellationToken ct = default)
    {
        lock (Store.SyncRoot)
        {
            if (string.IsNullOrEmpty(lineId) || !Store.Lines.TryGetValue(lineId, out var line))
            {
                return Task.FromResult(CommandResult.Fail($"'{lineId}' not found."));
            }

            // Maps first (they reference printers), then printers + their runtime state, then the line.
            var maps = MapsOf(lineId);
            foreach (var map in maps)
            {
                Store.Maps.TryRemove(map.MapId!, out _);
            }

            var printers = PrintersOf(lineId);
            foreach (var printer in printers)
            {
                Store.Printers.TryRemove(printer.PrinterId!, out _);
                Store.PrinterRuntime.TryRemove(printer.PrinterId!, out _);
            }

            Store.Lines.TryRemove(lineId, out _);
            Store.MarkConfigChanged(lineId);
            return Task.FromResult(CommandResult.Ok(
                $"Deleted lane '{line.Name}' with {printers.Count} printer(s) and {maps.Count} map(s)."));
        }
    }

    private List<PrinterDto> PrintersOf(string lineId) =>
        Store.Printers.Values.Where(p => ConfigValidation.IdEquals(p.LineId, lineId)).ToList();

    private List<MapDto> MapsOf(string lineId) =>
        Store.Maps.Values.Where(m => ConfigValidation.IdEquals(m.LineId, lineId)).ToList();
}

public sealed class DemoPrinterEditor(DemoDataStore store)
    : DemoConfigEditor<PrinterDto>(store.Printers, "printer", store), IPrinterEditor
{
    protected override string? GetId(PrinterDto e) => e.PrinterId;

    protected override PrinterDto WithId(PrinterDto e, string id) => e with { PrinterId = id };

    // Within a line, PLC numbers and names must be unique; the IP:port endpoint must be unique system-wide.
    protected override CommandResult? Validate(PrinterDto e)
    {
        if (string.IsNullOrWhiteSpace(e.Name))
        {
            return CommandResult.Fail("Printer name is required.");
        }

        if (e.PlcNumber < 1)
        {
            return CommandResult.Fail("Printer PLC Number must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(e.LineId) || !Store.Lines.ContainsKey(e.LineId))
        {
            return CommandResult.Fail($"Line '{e.LineId}' does not exist.");
        }

        if (string.IsNullOrWhiteSpace(e.OrientationId) || !Store.Orientations.ContainsKey(e.OrientationId))
        {
            return CommandResult.Fail("Select a valid orientation for the printer.");
        }

        // Moving a printer to another line would leave cross-line assignments in its old line's maps.
        if (Existing(e) is { } previous && !ConfigValidation.IdEquals(previous.LineId, e.LineId))
        {
            var maps = ConfigValidation.MapsReferencing(Store, printerId: previous.PrinterId);
            if (maps.Count > 0)
            {
                return CommandResult.Fail(
                    $"Printer '{previous.Name}' is assigned in map(s) {ConfigValidation.NameList(maps.Select(m => m.Name))}; unassign it before moving it to another line.");
            }
        }

        var others = Store.Printers.Values.Where(p => !ConfigValidation.IdEquals(p.PrinterId, e.PrinterId)).ToList();
        var sameLine = others.Where(p => ConfigValidation.IdEquals(p.LineId, e.LineId)).ToList();

        if (sameLine.Any(p => p.PlcNumber == e.PlcNumber))
        {
            return CommandResult.Fail($"PLC Number {e.PlcNumber} is already used by another printer on this line.");
        }

        if (sameLine.Any(p => ConfigValidation.NameEquals(p.Name, e.Name)))
        {
            return CommandResult.Fail($"A printer named '{e.Name.Trim()}' already exists on this line.");
        }

        if (!string.IsNullOrWhiteSpace(e.Ip) &&
            others.Any(p => ConfigValidation.NameEquals(p.Ip, e.Ip) && p.Port == e.Port))
        {
            return CommandResult.Fail($"Another printer already uses endpoint {e.Ip.Trim()}:{e.Port}.");
        }

        return null;
    }

    protected override void OnSaved(PrinterDto? previous, PrinterDto saved)
    {
        if (previous is not null)
        {
            Store.MarkConfigChanged(previous.LineId);
        }

        Store.MarkConfigChanged(saved.LineId);
    }

    // Refuse (rather than silently strip) while a map still assigns fire points to this printer.
    protected override CommandResult? ValidateDelete(PrinterDto existing)
    {
        var maps = ConfigValidation.MapsReferencing(Store, printerId: existing.PrinterId);
        return maps.Count > 0
            ? CommandResult.Fail(
                $"Printer '{existing.Name}' is assigned in map(s) {ConfigValidation.NameList(maps.Select(m => m.Name))}. Unassign it on Maps & Fire Points first.")
            : null;
    }

    protected override void OnDeleted(PrinterDto removed)
    {
        Store.PrinterRuntime.TryRemove(removed.PrinterId!, out _);
        Store.MarkConfigChanged(removed.LineId);
    }

    public Task<IReadOnlyList<PrinterDto>> ListForLineAsync(string lineId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PrinterDto>>(Store.Printers.Values
            .Where(p => string.Equals(p.LineId, lineId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.PlcNumber)
            .ToList());
}

public sealed class DemoFirePointEditor(DemoDataStore store)
    : DemoConfigEditor<FirePointDto>(store.FirePoints, "fp", store), IFirePointEditor
{
    protected override string? GetId(FirePointDto e) => e.FirePointId;

    protected override FirePointDto WithId(FirePointDto e, string id) => e with { FirePointId = id };

    // A fire point must reference an existing label definition, carry a valid apply point (negative inches only
    // for Middle), and be unique across the system on (label definition + apply point).
    protected override CommandResult? Validate(FirePointDto e)
    {
        if (string.IsNullOrWhiteSpace(e.LabelDefId) || !Store.LabelDefs.ContainsKey(e.LabelDefId))
        {
            return CommandResult.Fail("Select an existing label definition for the fire point.");
        }

        if (!ConfigValidation.ApplyEdges.Contains(e.ApplyEdge, StringComparer.Ordinal))
        {
            return CommandResult.Fail($"Apply edge '{e.ApplyEdge}' is not valid; use Leading, Trailing, or Middle.");
        }

        if (!double.IsFinite(e.ApplyInches))
        {
            return CommandResult.Fail("Apply inches must be a number.");
        }

        if (e.ApplyInches < 0 && !string.Equals(e.ApplyEdge, "Middle", StringComparison.Ordinal))
        {
            return CommandResult.Fail($"A negative apply distance is only valid for the Middle edge, not {e.ApplyEdge}.");
        }

        try
        {
            _ = PandA.Core.ApplyPoint.Parse(e.ApplyPointNotation);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            return CommandResult.Fail($"Invalid apply point '{e.ApplyPointNotation}': {ex.Message}");
        }

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

    protected override CommandResult? ValidateDelete(FirePointDto existing)
    {
        var maps = ConfigValidation.MapsReferencing(Store, firePointId: existing.FirePointId);
        return maps.Count > 0
            ? CommandResult.Fail(
                $"Fire point {existing.ApplyPointNotation} is assigned in map(s) {ConfigValidation.NameList(maps.Select(m => m.Name))}. Unassign it first.")
            : null;
    }
}

public sealed class DemoMapEditor(DemoDataStore store)
    : DemoConfigEditor<MapDto>(store.Maps, "map", store), IMapEditor
{
    protected override string? GetId(MapDto e) => e.MapId;

    protected override MapDto WithId(MapDto e, string id) => e with { MapId = id };

    // Map names must be unique across the system; assignments may only bind existing fire points to printers
    // on the map's own line; an active map can neither move to another line nor be renamed (cartons carry
    // the active map's name as their profile name).
    protected override CommandResult? Validate(MapDto e)
    {
        if (string.IsNullOrWhiteSpace(e.Name))
        {
            return CommandResult.Fail("Map name is required.");
        }

        if (Store.Maps.Values.Any(m => !ConfigValidation.IdEquals(m.MapId, e.MapId) && ConfigValidation.NameEquals(m.Name, e.Name)))
        {
            return CommandResult.Fail($"A map named '{e.Name.Trim()}' already exists.");
        }

        if (string.IsNullOrWhiteSpace(e.LineId) || !Store.Lines.ContainsKey(e.LineId))
        {
            return CommandResult.Fail($"Line '{e.LineId}' does not exist.");
        }

        if (Existing(e) is { } previous && ConfigValidation.ActiveOn(Store, previous.MapId) is { } activeLine)
        {
            if (!ConfigValidation.IdEquals(previous.LineId, e.LineId))
            {
                return CommandResult.Fail(
                    $"Map '{previous.Name}' is active on '{activeLine.Name}'; activate another map there before moving it to a different line.");
            }

            if (!string.Equals(previous.Name.Trim(), e.Name.Trim(), StringComparison.Ordinal))
            {
                return CommandResult.Fail(
                    $"Map '{previous.Name}' is active on '{activeLine.Name}' and cannot be renamed while active (cartons reference it by name).");
            }
        }

        foreach (var assignment in e.Assignments)
        {
            if (!Store.FirePoints.ContainsKey(assignment.FirePointId))
            {
                return CommandResult.Fail($"Fire point '{assignment.FirePointId}' does not exist.");
            }

            foreach (var printerId in assignment.PrinterIds)
            {
                if (!Store.Printers.TryGetValue(printerId, out var printer) || !ConfigValidation.IdEquals(printer.LineId, e.LineId))
                {
                    var name = printer?.Name ?? printerId;
                    return CommandResult.Fail($"Printer '{name}' is not on this map's line; clear its assignments first.");
                }
            }
        }

        return null;
    }

    protected override void OnSaved(MapDto? previous, MapDto saved)
    {
        if (previous is not null)
        {
            Store.MarkConfigChanged(previous.LineId);
        }

        Store.MarkConfigChanged(saved.LineId);
    }

    protected override CommandResult? ValidateDelete(MapDto existing) =>
        ConfigValidation.ActiveOn(Store, existing.MapId) is { } line
            ? CommandResult.Fail($"Map '{existing.Name}' is active on '{line.Name}'. Set another map active on that line before deleting it.")
            : null;

    protected override void OnDeleted(MapDto removed) => Store.MarkConfigChanged(removed.LineId);

    public Task<IReadOnlyList<MapDto>> ListForLineAsync(string lineId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MapDto>>(Store.Maps.Values
            .Where(m => string.Equals(m.LineId, lineId, StringComparison.OrdinalIgnoreCase))
            .ToList());
}
