using PandA.UI.Contracts.Config;
using PandA.UI.DemoHost.Sim;

namespace PandA.E2E.Tests;

/// <summary>
/// Covers config uniqueness constraints enforced by the demo editors: no duplicate line names or line
/// PLC numbers, no duplicate printer PLC numbers/names within a line, no shared printer endpoints, and
/// unique map / MandA / label / orientation names.
/// </summary>
public sealed class ConfigUniquenessTests
{
    private static PrinterDto NewPrinter(string lineId, int plc, string name = "P", string ip = "10.99.0.1", int port = 9100)
    {
        var store = new DemoDataStore();
        var orientation = store.Orientations.Values.First().OrientationId;
        return new PrinterDto(null, lineId, name, ip, port, orientation!, plc, "TD1", "TD2", 40, 12, 30);
    }

    [Fact]
    public async Task Two_printers_on_same_line_cannot_share_plc_number()
    {
        var store = new DemoDataStore();
        var editor = new DemoPrinterEditor(store);
        var line = store.Lines.Values.First();
        var existing = store.Printers.Values.First(p => string.Equals(p.LineId, line.LineId, StringComparison.Ordinal));

        var clash = NewPrinter(line.LineId!, existing.PlcNumber, name: "Different name", ip: "10.99.0.9");
        var result = await editor.SaveAsync(clash);

        Assert.False(result.Success);
        Assert.Contains("PLC Number", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Printers_on_different_lines_may_share_plc_number()
    {
        var store = new DemoDataStore();
        var editor = new DemoPrinterEditor(store);
        var lines = store.Lines.Values.Take(2).ToList();

        int PrintersOn(string lineId) => store.Printers.Values.Count(p => string.Equals(p.LineId, lineId, StringComparison.Ordinal));

        // A PLC number that exists on line 1 but is free on line 2 (line 1 has more printers than line 2).
        var line1 = lines.OrderByDescending(l => PrintersOn(l.LineId!)).First();
        var line2 = lines.OrderBy(l => PrintersOn(l.LineId!)).First();
        var sharedPlc = store.Printers.Values
            .Where(p => string.Equals(p.LineId, line1.LineId, StringComparison.Ordinal))
            .Max(p => p.PlcNumber);

        var onLine2 = NewPrinter(line2.LineId!, sharedPlc, name: "Cross-line peer", ip: "10.99.0.8");
        var result = await editor.SaveAsync(onLine2);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Printers_cannot_share_endpoint()
    {
        var store = new DemoDataStore();
        var editor = new DemoPrinterEditor(store);
        var line = store.Lines.Values.First();
        var existing = store.Printers.Values.First();

        var clash = NewPrinter(line.LineId!, plc: 99, name: "Endpoint clash", ip: existing.Ip, port: existing.Port);
        var result = await editor.SaveAsync(clash);

        Assert.False(result.Success);
        Assert.Contains("endpoint", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Lines_cannot_share_a_name()
    {
        var store = new DemoDataStore();
        var editor = new DemoLineEditor(store);
        var existing = store.Lines.Values.First();

        var clash = new LineDto(null, existing.Name, 999, [], [], null, LineControlPolicy.AllowDegraded, [], 0.25, 24, true, false);
        var result = await editor.SaveAsync(clash);

        Assert.False(result.Success);
        Assert.Contains("already exists", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Lines_cannot_share_a_plc_number()
    {
        var store = new DemoDataStore();
        var editor = new DemoLineEditor(store);
        var existing = store.Lines.Values.First();

        var clash = new LineDto(null, "Brand New Line", existing.PlcNumber, [], [], null, LineControlPolicy.AllowDegraded, [], 0.25, 24, true, false);
        var result = await editor.SaveAsync(clash);

        Assert.False(result.Success);
        Assert.Contains("PLC Number", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Map_names_must_be_unique()
    {
        var store = new DemoDataStore();
        var editor = new DemoMapEditor(store);
        var line = store.Lines.Values.First();
        var existing = store.Maps.Values.First();

        var clash = new MapDto(null, line.LineId!, existing.Name, []);
        var result = await editor.SaveAsync(clash);

        Assert.False(result.Success);
        Assert.Contains("already exists", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Manda_names_must_be_unique()
    {
        var store = new DemoDataStore();
        var editor = new DemoMandaStationEditor(store);
        var existing = store.Stations.Values.First();

        var clash = new MandaStationDto(null, existing.Name, "10.99.9.9", 9100);
        var result = await editor.SaveAsync(clash);

        Assert.False(result.Success);
        Assert.Contains("already exists", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Label_definition_names_must_be_unique()
    {
        var store = new DemoDataStore();
        var editor = new DemoLabelDefEditor(store);
        var existing = store.LabelDefs.Values.First();

        var clash = new LabelDefDto(null, existing.Name, "dup", 4.0);
        var result = await editor.SaveAsync(clash);

        Assert.False(result.Success);
        Assert.Contains("already exists", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Editing_a_line_in_place_keeps_its_own_name_and_plc()
    {
        var store = new DemoDataStore();
        var editor = new DemoLineEditor(store);
        var existing = store.Lines.Values.First();

        // Re-saving the same line (same id, same name+PLC) must not trip the uniqueness rules.
        var result = await editor.SaveAsync(existing);

        Assert.True(result.Success);
    }
}
