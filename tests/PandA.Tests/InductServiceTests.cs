using PandA.Core;
using PandA.Sim;

namespace PandA.Tests;

public sealed class InductServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTransportOrderStore _store = new();
    private readonly InMemoryLineProvider _lines = new();
    private readonly CapturingPrinterGateway _gateway = new();
    private readonly TestClock _clock = new(T0);
    private readonly CartonAdviceService _advice;
    private readonly InductService _induct;

    public InductServiceTests()
    {
        _advice = new CartonAdviceService(_store, _clock);
        _induct = new InductService(_store, _lines, new PrinterSelectionService(), _gateway, _clock);
    }

    private static PrinterConfig Printer(string id, string[] map, int order) =>
        new(id, "10.0.0." + order, 9100, map, ApplyOrientation.Side, order);

    private static PandaLabelSet Labels(params string[] types) =>
        new(types.Select(t => new Label(t, $"LPN-{t}", $"^XA{t}^XZ")));

    [Fact]
    public async Task Induct_MatchingOrder_PrintsEachLabelToMatchedPrinter_AndMarksPrinted()
    {
        _lines.Add(new LineConfig("L1",
        [
            Printer("Ship1", ["Shipping"], 0),
            Printer("Cont1", ["Content"], 1),
        ]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping", "Content"));

        var result = await _induct.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.Printed, result.Status);
        Assert.Equal(2, _gateway.Jobs.Count);
        Assert.Equal("Ship1", _gateway.Jobs.Single(j => j.LabelType == "Shipping").PrinterId);
        Assert.Equal("Cont1", _gateway.Jobs.Single(j => j.LabelType == "Content").PrinterId);
        Assert.Equal("^XAShipping^XZ", _gateway.Jobs.Single(j => j.LabelType == "Shipping").Zpl);

        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(TransportOrderStatus.Printed, stored!.Status);
    }

    [Fact]
    public async Task Induct_UnknownBlindLabel_ReturnsNoActiveOrder_AndPrintsNothing()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));

        var result = await _induct.InductAsync("L1", "NOPE");

        Assert.Equal(InductStatus.NoActiveOrder, result.Status);
        Assert.Empty(_gateway.Jobs);
    }

    [Fact]
    public async Task Induct_NoPrinterForOneType_PartiallyPrints()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping", "Content")); // no Content printer

        var result = await _induct.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.PartiallyPrinted, result.Status);
        Assert.Single(_gateway.Jobs);
        Assert.Equal("Shipping", _gateway.Jobs[0].LabelType);
    }

    [Fact]
    public async Task Induct_TwoCartons_RoundRobinsAcrossPrintersViaLastPrinted()
    {
        _lines.Add(new LineConfig("L1",
        [
            Printer("P1", ["Shipping"], 0),
            Printer("P2", ["Shipping"], 1),
        ]));

        await _advice.AdviseAsync("L1", "C1", Labels("Shipping"));
        await _advice.AdviseAsync("L1", "C2", Labels("Shipping"));

        // First carton: both never printed → configured order → P1, then P1 gets stamped.
        var r1 = await _induct.InductAsync("L1", "C1");
        _clock.Advance(TimeSpan.FromSeconds(1));
        // Second carton: P1 is now most-recently-printed → least-recently is P2.
        var r2 = await _induct.InductAsync("L1", "C2");

        Assert.Equal(InductStatus.Printed, r1.Status);
        Assert.Equal(InductStatus.Printed, r2.Status);
        Assert.Equal("P1", _gateway.Jobs[0].PrinterId);
        Assert.Equal("P2", _gateway.Jobs[1].PrinterId);
    }
}
