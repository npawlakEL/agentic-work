using PandA.UI.Contracts.Config;
using PandA.UI.DemoHost.Sim;

namespace PandA.E2E.Tests;

/// <summary>
/// Simulator line catalog resilience (live lines, missing lines, config-change invalidation) plus the
/// MandA verify carton guard and label-slot copy semantics.
/// </summary>
public sealed class SimCatalogAndRuntimeTests
{
    [Fact]
    public async Task Catalog_reads_lines_live_and_switches_between_lines()
    {
        var store = new DemoDataStore();
        var catalog = new SimLineCatalog(store);
        var lines = catalog.Lines;
        Assert.True(lines.Count >= 2);

        var first = await catalog.Get(lines[0].Id).GetSnapshotAsync();
        var second = await catalog.Get(lines[1].Id).GetSnapshotAsync();
        Assert.Equal(lines[0].Id, first.LineId);
        Assert.Equal(lines[1].Id, second.LineId);

        var added = await new DemoLineEditor(store).SaveAsync(
            new LineDto(null, "AAA Late Lane", 55, [], [], null, LineControlPolicy.AllowDegraded, [], 0.25, 24, false, false));
        Assert.Contains(catalog.Lines, l => string.Equals(l.Id, added.EntityId, StringComparison.Ordinal));
        Assert.Equal(added.EntityId, catalog.DefaultLineId);
    }

    [Fact]
    public async Task Deleted_line_yields_an_error_instead_of_throwing()
    {
        var store = new DemoDataStore();
        var catalog = new SimLineCatalog(store);
        var lineId = catalog.DefaultLineId;
        Assert.True(catalog.TryGet(lineId).Found);

        Assert.True((await new DemoLineEditor(store).DeleteWithDependentsAsync(lineId)).Success);

        var lookup = catalog.TryGet(lineId);
        Assert.False(lookup.Found);
        Assert.Contains("no longer exists", lookup.Error, StringComparison.Ordinal);
        Assert.False(catalog.TryGet("line-does-not-exist").Found);
    }

    [Fact]
    public void No_lines_at_all_yields_an_error_for_the_default_line()
    {
        var store = new DemoDataStore();
        store.Lines.Clear();
        var catalog = new SimLineCatalog(store);

        Assert.Equal(string.Empty, catalog.DefaultLineId);
        var lookup = catalog.TryGet("");
        Assert.False(lookup.Found);
        Assert.NotNull(lookup.Error);
    }

    [Fact]
    public async Task Cached_simulation_is_rebuilt_when_its_lines_config_changes_and_kept_otherwise()
    {
        var store = new DemoDataStore();
        var catalog = new SimLineCatalog(store);
        var lineIds = catalog.Lines.Select(l => l.Id).ToList();
        var target = lineIds[0];
        var other = lineIds[1];

        var simA = catalog.Get(target);
        var otherSim = catalog.Get(other);
        Assert.Same(simA, catalog.Get(target));

        // A printer change on the target line invalidates only that line's sim.
        var printer = store.Printers.Values.First(p => string.Equals(p.LineId, target, StringComparison.Ordinal));
        Assert.True((await new DemoPrinterEditor(store).SaveAsync(printer with { PrintPoint = printer.PrintPoint + 5 })).Success);
        var simB = catalog.Get(target);
        Assert.NotSame(simA, simB);
        Assert.Same(otherSim, catalog.Get(other));

        // Shared config (a label definition) invalidates every line.
        var label = store.LabelDefs.Values.First();
        Assert.True((await new DemoLabelDefEditor(store).SaveAsync(label with { Description = "changed" })).Success);
        Assert.NotSame(simB, catalog.Get(target));
        Assert.NotSame(otherSim, catalog.Get(other));

        // MandA stations are not part of the sim: no rebuild.
        var current = catalog.Get(target);
        Assert.True((await new DemoMandaStationEditor(store).SaveAsync(new MandaStationDto(null, "Spare Station", "10.1.1.1", 9100))).Success);
        Assert.Same(current, catalog.Get(target));

        // Explicit invalidation also forces a rebuild.
        catalog.Invalidate(target);
        Assert.NotSame(current, catalog.Get(target));
    }

    [Fact]
    public async Task Manda_verify_requires_the_active_carton()
    {
        var store = new DemoDataStore();
        var manda = new DemoMandaServices(store);
        var stationId = store.Stations.Keys.First();
        var cartons = store.Cartons.Values.Where(c => c.Slots.Count > 0).Take(2).ToList();
        var active = cartons[0];
        var other = cartons[1];

        var scan = await manda.ScanAsync(stationId, active.BlindLabel);
        Assert.True(scan.Found);
        var slot = active.Slots[0];
        Assert.True((await manda.PrintAsync(stationId, active.CartonId, [slot.Slot])).Success);

        var wrongCarton = await manda.VerifyAsync(stationId, other.CartonId, slot.LabelBarcode);
        Assert.False(wrongCarton.Passed);
        Assert.Contains("not the active carton", wrongCarton.Message, StringComparison.Ordinal);

        var right = await manda.VerifyAsync(stationId, active.CartonId, slot.LabelBarcode);
        Assert.True(right.Passed, right.Message);
    }

    [Fact]
    public async Task Label_slots_query_returns_a_copy_of_the_cartons_slots()
    {
        var store = new DemoDataStore();
        var carton = store.Cartons.Values.First(c => c.Slots.Count > 0);
        var count = carton.Slots.Count;

        var slots = await new DemoLookupQueries(store).GetLabelSlotsAsync(carton.CartonId);

        Assert.NotSame(carton.Slots, slots);
        Assert.Equal(count, slots.Count);
        carton.Slots.Clear();
        Assert.Equal(count, slots.Count);
    }
}
