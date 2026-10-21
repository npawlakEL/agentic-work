using PandA.UI.Contracts.Config;
using PandA.UI.DemoHost.Sim;

namespace PandA.E2E.Tests;

/// <summary>
/// Referential-integrity rules enforced by the demo config editors: activating maps, refusing deletes of
/// referenced entities (with the lane cascade as the one explicit exception), map/line consistency,
/// fire-point validation, label/map rename handling, and buffer-order validation.
/// </summary>
public sealed class ConfigIntegrityTests
{
    private static LineDto LineWithMap(DemoDataStore store) =>
        store.Lines.Values.First(l => l.ActiveMapId is not null);

    // ---- #1 active map ----

    [Fact]
    public async Task Setting_a_lines_active_map_to_one_of_its_maps_succeeds()
    {
        var store = new DemoDataStore();
        var line = LineWithMap(store);
        var maps = new DemoMapEditor(store);
        var created = await maps.SaveAsync(new MapDto(null, line.LineId!, "Alt Map", []));
        Assert.True(created.Success, created.Message);
        Assert.NotNull(created.EntityId);

        var result = await new DemoLineEditor(store).SaveAsync(line with { ActiveMapId = created.EntityId });

        Assert.True(result.Success, result.Message);
        Assert.Equal(created.EntityId, store.Lines[line.LineId!].ActiveMapId);
    }

    [Fact]
    public async Task A_line_cannot_activate_another_lines_map_or_a_missing_map()
    {
        var store = new DemoDataStore();
        var lines = store.Lines.Values.Where(l => l.ActiveMapId is not null).Take(2).ToList();
        var editor = new DemoLineEditor(store);

        var foreign = await editor.SaveAsync(lines[0] with { ActiveMapId = lines[1].ActiveMapId });
        var missing = await editor.SaveAsync(lines[0] with { ActiveMapId = "map-nope" });

        Assert.False(foreign.Success);
        Assert.Contains("another line", foreign.Message, StringComparison.Ordinal);
        Assert.False(missing.Success);
        Assert.Equal(lines[0].ActiveMapId, store.Lines[lines[0].LineId!].ActiveMapId);
    }

    [Fact]
    public async Task Deleting_the_active_map_is_refused_until_another_map_is_activated()
    {
        var store = new DemoDataStore();
        var line = LineWithMap(store);
        var maps = new DemoMapEditor(store);
        var activeId = line.ActiveMapId!;

        var refused = await maps.DeleteAsync(activeId);
        Assert.False(refused.Success);
        Assert.Contains("active", refused.Message, StringComparison.Ordinal);
        Assert.True(store.Maps.ContainsKey(activeId));

        var alt = await maps.SaveAsync(new MapDto(null, line.LineId!, "Alt Map", []));
        Assert.True((await new DemoLineEditor(store).SaveAsync(line with { ActiveMapId = alt.EntityId })).Success);

        var deleted = await maps.DeleteAsync(activeId);
        Assert.True(deleted.Success, deleted.Message);
        Assert.False(store.Maps.ContainsKey(activeId));
    }

    // ---- #2 fire point validation ----

    [Theory]
    [InlineData("Leading", -1.0)]
    [InlineData("Trailing", -0.5)]
    public async Task Negative_apply_inches_are_rejected_for_leading_and_trailing(string edge, double inches)
    {
        var store = new DemoDataStore();
        var before = store.FirePoints.Count;
        var result = await new DemoFirePointEditor(store).SaveAsync(new FirePointDto(null, store.LabelDefs.Keys.First(), edge, inches));

        Assert.False(result.Success);
        Assert.Contains("Middle", result.Message, StringComparison.Ordinal);
        Assert.Equal(before, store.FirePoints.Count);
    }

    [Fact]
    public async Task Negative_apply_inches_are_allowed_for_middle()
    {
        var store = new DemoDataStore();
        var result = await new DemoFirePointEditor(store).SaveAsync(new FirePointDto(null, store.LabelDefs.Keys.First(), "Middle", -2));

        Assert.True(result.Success, result.Message);
    }

    [Theory]
    [InlineData("Sideways")]
    [InlineData("")]
    [InlineData("leading")]
    public async Task Unknown_apply_edges_are_rejected(string edge)
    {
        var store = new DemoDataStore();
        var result = await new DemoFirePointEditor(store).SaveAsync(new FirePointDto(null, store.LabelDefs.Keys.First(), edge, 1));

        Assert.False(result.Success);
        Assert.Contains("edge", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fire_point_must_reference_an_existing_label_definition()
    {
        var store = new DemoDataStore();
        var result = await new DemoFirePointEditor(store).SaveAsync(new FirePointDto(null, "label-nope", "Middle", 0));

        Assert.False(result.Success);
    }

    [Fact]
    public void A_corrupt_stored_fire_point_is_skipped_instead_of_crashing_the_simulation()
    {
        var store = new DemoDataStore();
        var line = LineWithMap(store);
        var map = store.Maps[line.ActiveMapId!];
        var printer = store.Printers.Values.First(p => string.Equals(p.LineId, line.LineId, StringComparison.Ordinal));

        // Bypass the editor (as stale/legacy data would) with an invalid negative Leading apply point.
        store.FirePoints["fp-bad"] = new FirePointDto("fp-bad", store.LabelDefs.Keys.First(), "Leading", -3);
        store.Maps[map.MapId!] = map with { Assignments = [.. map.Assignments, new FirePointAssignment("fp-bad", [printer.PrinterId!])] };

        var lookup = new SimLineCatalog(store).TryGet(line.LineId);

        Assert.True(lookup.Found, lookup.Error);
    }

    // ---- #3 delete refusal / lane cascade ----

    [Fact]
    public async Task Deleting_a_line_with_printers_or_maps_is_refused()
    {
        var store = new DemoDataStore();
        var line = LineWithMap(store);

        var result = await new DemoLineEditor(store).DeleteAsync(line.LineId!);

        Assert.False(result.Success);
        Assert.Contains("printer", result.Message, StringComparison.Ordinal);
        Assert.True(store.Lines.ContainsKey(line.LineId!));
    }

    [Fact]
    public async Task Lane_cascade_delete_removes_the_line_its_printers_maps_and_runtime()
    {
        var store = new DemoDataStore();
        var line = LineWithMap(store);
        var lineId = line.LineId!;
        var printerIds = store.Printers.Values.Where(p => string.Equals(p.LineId, lineId, StringComparison.Ordinal)).Select(p => p.PrinterId!).ToList();
        Assert.NotEmpty(printerIds);
        var otherPrinters = store.Printers.Count - printerIds.Count;

        var result = await new DemoLineEditor(store).DeleteWithDependentsAsync(lineId);

        Assert.True(result.Success, result.Message);
        Assert.False(store.Lines.ContainsKey(lineId));
        Assert.DoesNotContain(store.Printers.Values, p => string.Equals(p.LineId, lineId, StringComparison.Ordinal));
        Assert.DoesNotContain(store.Maps.Values, m => string.Equals(m.LineId, lineId, StringComparison.Ordinal));
        Assert.All(printerIds, id => Assert.False(store.PrinterRuntime.ContainsKey(id)));
        Assert.Equal(otherPrinters, store.Printers.Count);
        // No surviving map references a deleted printer.
        Assert.DoesNotContain(store.Maps.Values.SelectMany(m => m.Assignments).SelectMany(a => a.PrinterIds), printerIds.Contains);
    }

    [Fact]
    public async Task Deleting_a_printer_assigned_in_a_map_is_refused_and_allowed_once_unassigned()
    {
        var store = new DemoDataStore();
        var map = store.Maps[LineWithMap(store).ActiveMapId!];
        var printerId = map.Assignments.SelectMany(a => a.PrinterIds).First();
        var editor = new DemoPrinterEditor(store);

        var refused = await editor.DeleteAsync(printerId);
        Assert.False(refused.Success);
        Assert.Contains(map.Name, refused.Message, StringComparison.Ordinal);

        var stripped = map with
        {
            Assignments = map.Assignments
                .Select(a => a with { PrinterIds = a.PrinterIds.Where(p => !string.Equals(p, printerId, StringComparison.Ordinal)).ToList() })
                .ToList(),
        };
        Assert.True((await new DemoMapEditor(store).SaveAsync(stripped)).Success);

        var deleted = await editor.DeleteAsync(printerId);
        Assert.True(deleted.Success, deleted.Message);
        Assert.False(store.PrinterRuntime.ContainsKey(printerId));
    }

    [Fact]
    public async Task Deleting_a_label_definition_used_by_fire_points_or_buffer_order_is_refused()
    {
        var store = new DemoDataStore();
        var usedByFirePoint = store.FirePoints.Values.First().LabelDefId;

        var refused = await new DemoLabelDefEditor(store).DeleteAsync(usedByFirePoint);
        Assert.False(refused.Success);
        Assert.Contains("fire point", refused.Message, StringComparison.Ordinal);

        // A label used only by a buffer order is also protected.
        var labels = new DemoLabelDefEditor(store);
        var created = await labels.SaveAsync(new LabelDefDto(null, "Buffer Only", "", 4));
        var line = store.Lines.Values.First();
        Assert.True((await new DemoLineEditor(store).SaveAsync(line with { BufferOrder = [.. line.BufferOrder, "Buffer Only"] })).Success);

        var refusedBuffer = await labels.DeleteAsync(created.EntityId!);
        Assert.False(refusedBuffer.Success);
        Assert.Contains("buffer order", refusedBuffer.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_a_fire_point_assigned_in_a_map_is_refused()
    {
        var store = new DemoDataStore();
        var fpId = store.Maps.Values.SelectMany(m => m.Assignments).First().FirePointId;

        var result = await new DemoFirePointEditor(store).DeleteAsync(fpId);

        Assert.False(result.Success);
        Assert.True(store.FirePoints.ContainsKey(fpId));
    }

    [Fact]
    public async Task Deleting_an_orientation_used_by_printers_is_refused()
    {
        var store = new DemoDataStore();
        var used = store.Printers.Values.First().OrientationId;

        var result = await new DemoOrientationEditor(store).DeleteAsync(used);

        Assert.False(result.Success);
        Assert.True(store.Orientations.ContainsKey(used));
    }

    [Fact]
    public async Task Unreferenced_entities_can_still_be_deleted()
    {
        var store = new DemoDataStore();
        var labels = new DemoLabelDefEditor(store);
        var label = await labels.SaveAsync(new LabelDefDto(null, "Spare", "", 4));
        var fps = new DemoFirePointEditor(store);
        var fp = await fps.SaveAsync(new FirePointDto(null, label.EntityId!, "Trailing", 3));
        var orientations = new DemoOrientationEditor(store);
        var orientation = await orientations.SaveAsync(new OrientationDto(null, "Unused", ApplyMotionKind.Front));
        var lines = new DemoLineEditor(store);
        var line = await lines.SaveAsync(new LineDto(null, "Empty Lane", 99, [], [], null, LineControlPolicy.AllowDegraded, [], 0.25, 24, false, false));

        Assert.True((await fps.DeleteAsync(fp.EntityId!)).Success);
        Assert.True((await labels.DeleteAsync(label.EntityId!)).Success);
        Assert.True((await orientations.DeleteAsync(orientation.EntityId!)).Success);
        Assert.True((await lines.DeleteAsync(line.EntityId!)).Success);
    }

    // ---- #4 map/line consistency ----

    [Fact]
    public async Task Map_assignments_must_target_printers_on_the_maps_line()
    {
        var store = new DemoDataStore();
        var lines = store.Lines.Values.Take(2).ToList();
        var foreignPrinter = store.Printers.Values.First(p => string.Equals(p.LineId, lines[1].LineId, StringComparison.Ordinal));
        var fpId = store.FirePoints.Keys.First();

        var result = await new DemoMapEditor(store).SaveAsync(
            new MapDto(null, lines[0].LineId!, "Cross Line", [new FirePointAssignment(fpId, [foreignPrinter.PrinterId!])]));

        Assert.False(result.Success);
        Assert.Contains(foreignPrinter.Name, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Map_assignments_must_reference_existing_fire_points()
    {
        var store = new DemoDataStore();
        var line = store.Lines.Values.First();
        var printer = store.Printers.Values.First(p => string.Equals(p.LineId, line.LineId, StringComparison.Ordinal));

        var result = await new DemoMapEditor(store).SaveAsync(
            new MapDto(null, line.LineId!, "Ghost", [new FirePointAssignment("fp-nope", [printer.PrinterId!])]));

        Assert.False(result.Success);
    }

    [Fact]
    public async Task An_active_map_cannot_move_to_another_line()
    {
        var store = new DemoDataStore();
        var line = LineWithMap(store);
        var other = store.Lines.Values.First(l => !string.Equals(l.LineId, line.LineId, StringComparison.Ordinal));
        var map = store.Maps[line.ActiveMapId!];

        var result = await new DemoMapEditor(store).SaveAsync(map with { LineId = other.LineId!, Assignments = [] });

        Assert.False(result.Success);
        Assert.Equal(line.LineId, store.Maps[map.MapId!].LineId);
    }

    [Fact]
    public async Task An_inactive_map_can_move_lines_once_its_assignments_are_cleared()
    {
        var store = new DemoDataStore();
        var lines = store.Lines.Values.Take(2).ToList();
        var editor = new DemoMapEditor(store);
        var printer = store.Printers.Values.First(p => string.Equals(p.LineId, lines[0].LineId, StringComparison.Ordinal));
        var created = await editor.SaveAsync(new MapDto(null, lines[0].LineId!, "Mover", [new FirePointAssignment(store.FirePoints.Keys.First(), [printer.PrinterId!])]));
        var saved = store.Maps[created.EntityId!];

        var withStale = await editor.SaveAsync(saved with { LineId = lines[1].LineId! });
        var cleared = await editor.SaveAsync(saved with { LineId = lines[1].LineId!, Assignments = [] });

        Assert.False(withStale.Success);
        Assert.True(cleared.Success, cleared.Message);
    }

    // ---- #8 renames and buffer order ----

    [Fact]
    public async Task Renaming_a_label_definition_cascades_into_buffer_orders()
    {
        var store = new DemoDataStore();
        var line = store.Lines.Values.First(l => l.BufferOrder.Count > 0);
        var oldName = line.BufferOrder[0];
        var label = store.LabelDefs.Values.First(l => string.Equals(l.Name, oldName, StringComparison.OrdinalIgnoreCase));

        var result = await new DemoLabelDefEditor(store).SaveAsync(label with { Name = "Renamed Label" });

        Assert.True(result.Success, result.Message);
        Assert.All(store.Lines.Values, l => Assert.DoesNotContain(oldName, l.BufferOrder));
        Assert.Equal("Renamed Label", store.Lines[line.LineId!].BufferOrder[0]);
        Assert.Equal(line.BufferOrder.Count, store.Lines[line.LineId!].BufferOrder.Count);
    }

    [Fact]
    public async Task An_active_map_cannot_be_renamed_but_an_inactive_one_can()
    {
        var store = new DemoDataStore();
        var line = LineWithMap(store);
        var editor = new DemoMapEditor(store);
        var active = store.Maps[line.ActiveMapId!];

        var refused = await editor.SaveAsync(active with { Name = "Renamed Active" });
        Assert.False(refused.Success);
        Assert.Contains("renamed", refused.Message, StringComparison.Ordinal);

        var inactive = await editor.SaveAsync(new MapDto(null, line.LineId!, "Draft", []));
        var renamed = await editor.SaveAsync(store.Maps[inactive.EntityId!] with { Name = "Draft 2" });
        Assert.True(renamed.Success, renamed.Message);
    }

    [Fact]
    public async Task Buffer_order_may_only_list_existing_label_definitions_once()
    {
        var store = new DemoDataStore();
        var line = store.Lines.Values.First(l => l.BufferOrder.Count > 0);
        var editor = new DemoLineEditor(store);

        var unknown = await editor.SaveAsync(line with { BufferOrder = [.. line.BufferOrder, "Not A Label"] });
        var duplicate = await editor.SaveAsync(line with { BufferOrder = [.. line.BufferOrder, line.BufferOrder[0]] });

        Assert.False(unknown.Success);
        Assert.Contains("Not A Label", unknown.Message, StringComparison.Ordinal);
        Assert.False(duplicate.Success);
        Assert.True((await editor.SaveAsync(line)).Success);
    }

    // ---- #5 new entity id ----

    [Fact]
    public async Task Save_reports_the_id_of_a_newly_created_entity()
    {
        var store = new DemoDataStore();
        var result = await new DemoLineEditor(store).SaveAsync(
            new LineDto(null, "Fresh Lane", 77, [], [], null, LineControlPolicy.AllowDegraded, [], 0.25, 24, false, false));

        Assert.True(result.Success);
        Assert.NotNull(result.EntityId);
        Assert.Equal("Fresh Lane", store.Lines[result.EntityId!].Name);
    }

    // ---- #12 store lock ----

    [Fact]
    public async Task Concurrent_saves_cannot_both_claim_a_unique_name()
    {
        var store = new DemoDataStore();
        var editor = new DemoLabelDefEditor(store);
        var before = store.LabelDefs.Count;

        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            Task.Run(() => editor.SaveAsync(new LabelDefDto(null, "Racy", "", 4)))));

        Assert.Single(results, r => r.Success);
        Assert.Equal(before + 1, store.LabelDefs.Count);
    }
}
