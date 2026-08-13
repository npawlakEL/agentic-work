using PandA.Core;
using PandA.Core.Advice;
using PandA.Core.Domain;
using PandA.Core.Induct;
using PandA.Core.Services;
using PandA.Sim;
using PandA.Sim.Stores;

namespace PandA.Tests;

/// <summary>
/// F18 — a non-blind-label induct scan (oLPN/UPC/…) resolves to its transport order through the barcode
/// cross-reference populated at advice time, and the election prefers the least-printed carton.
/// </summary>
public sealed class XRefInductResolutionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTransportOrderStore _store = new();
    private readonly InMemoryLineProvider _lines = new();
    private readonly CapturingPrinterGateway _gateway = new();
    private readonly TestClock _clock = new(T0);
    private readonly InMemoryXRefStore _xref = new();
    private readonly CartonAdviceService _advice;
    private readonly InductService _induct;

    public XRefInductResolutionTests()
    {
        _advice = new CartonAdviceService(_store, _clock, new InMemorySettingsProvider(), new XRefService(_xref));
        _induct = new InductService(
            _store, _lines, new PrinterSelectionService(), _gateway, _clock, new InMemorySettingsProvider(),
            xref: _xref);
    }

    private static PrinterConfig Printer(string id) =>
        new(id, "10.0.0.1", 9100, ["Shipping"], ApplyOrientation.Side, 0);

    private static PandaLabelSet Labels() =>
        new([new Label("Shipping", "LPN-1", "^XAShip^XZ")]);

    [Fact]
    public async Task Induct_ByOlpnBarcode_ResolvesViaXref_AndPrints()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1")]));
        await _advice.AdviseAsync(new AdviceMessage(
            "L1", "BLIND1", Labels(),
            Barcodes: [new AdviceBarcode("OLPN9", BarcodeType.oLPN)]));

        var result = await _induct.InductAsync(new InductScan("L1", "OLPN9", Length: 400));

        Assert.Equal(InductStatus.Printed, result.Status);
        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(TransportOrderStatus.Printed, stored!.Status);
    }

    [Fact]
    public async Task Induct_UnknownBarcode_WithXref_ReturnsNoActiveOrder()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1")]));
        await _advice.AdviseAsync(new AdviceMessage(
            "L1", "BLIND1", Labels(),
            Barcodes: [new AdviceBarcode("OLPN9", BarcodeType.oLPN)]));

        var result = await _induct.InductAsync(new InductScan("L1", "NOTMAPPED", Length: 400));

        Assert.Equal(InductStatus.NoActiveOrder, result.Status);
        Assert.Empty(_gateway.Jobs);
    }

    [Fact]
    public async Task Induct_SharedBarcode_ElectsLeastPrintedCarton()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1")]));
        // Two cartons in different waves share the same UPC.
        await _advice.AdviseAsync(new AdviceMessage(
            "L1", "BL-OLD", Labels(), Barcodes: [new AdviceBarcode("UPC5", BarcodeType.UPC)]));
        await _advice.AdviseAsync(new AdviceMessage(
            "L1", "BL-NEW", Labels(), Barcodes: [new AdviceBarcode("UPC5", BarcodeType.UPC)]));

        // Print the old carton once so its PrintCount = 1.
        await _induct.InductAsync("L1", "BL-OLD");

        // A UPC scan must elect the least-printed carton (BL-NEW, PrintCount 0).
        var result = await _induct.InductAsync(new InductScan("L1", "UPC5", Length: 400));

        Assert.Equal(InductStatus.Printed, result.Status);
        var newCarton = await _store.FindActiveByTuIdAsync("BL-NEW");
        Assert.Equal(TransportOrderStatus.Printed, newCarton!.Status);
        Assert.Equal(1, newCarton.PrintCount);
    }
}
