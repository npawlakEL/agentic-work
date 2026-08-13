using PandA.Core;
using PandA.Core.Induct;
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
        _advice = new CartonAdviceService(_store, _clock, new InMemorySettingsProvider());
        _induct = new InductService(_store, _lines, new PrinterSelectionService(), _gateway, _clock, new InMemorySettingsProvider());
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

    [Fact]
    public async Task Induct_FullPrint_SetsPrintCountToOne_AndRecordsPerLabelState()
    {
        _lines.Add(new LineConfig("L1",
        [
            Printer("Ship1", ["Shipping"], 0),
            Printer("Cont1", ["Content"], 1),
        ]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping", "Content"));

        await _induct.InductAsync("L1", "BLIND1");

        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(1, stored!.PrintCount);
        Assert.Equal(TransportOrderStatus.Printed, stored.Status);
        Assert.True(stored.PrintStateFor("Shipping").Printed);
        Assert.Equal("Ship1", stored.PrintStateFor("Shipping").PrinterId);
        Assert.True(stored.PrintStateFor("Content").Printed);
    }

    [Fact]
    public async Task Induct_PartialPrint_DoesNotCount_NorMarkPrinted()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping", "Content")); // no Content printer

        var result = await _induct.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.PartiallyPrinted, result.Status);
        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(0, stored!.PrintCount); // partial run does not count
        Assert.Equal(TransportOrderStatus.Advised, stored.Status);
        Assert.True(stored.PrintStateFor("Shipping").Printed);
        Assert.False(stored.PrintStateFor("Content").Printed);
    }

    [Fact]
    public async Task Induct_AlreadyPrinted_WithoutAuthorization_IsBlockedAsNoReprint()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));
        await _induct.InductAsync("L1", "BLIND1"); // PrintCount → 1

        var second = await _induct.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.NoReprint, second.Status);
        Assert.Single(_gateway.Jobs); // nothing re-sent
        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(1, stored!.PrintCount);
    }

    [Fact]
    public async Task Induct_AfterOperatorAuthorization_ReprintsAndIncrementsToTwo()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));
        await _induct.InductAsync("L1", "BLIND1");

        var order = await _store.FindActiveByTuIdAsync("BLIND1");
        order!.AuthorizeReprint("relabel");
        await _store.UpsertAsync(order);

        var reprint = await _induct.InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.Printed, reprint.Status);
        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(2, stored!.PrintCount);
        Assert.Equal(2, _gateway.Jobs.Count);
    }

    [Fact]
    public async Task Induct_WithActiveProfile_ResolvesFirePointOntoPrintJob()
    {
        var profile = new FirePointProfile("Generic",
        [
            (("Ship1", "Shipping"), new FirePoint(2, 800, 3, ApplyPoint.Parse("1T"))),
        ]);
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)], activeProfile: profile));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));

        await _induct.InductAsync("L1", "BLIND1");

        var job = _gateway.Jobs.Single(j => j.LabelType == "Shipping");
        Assert.NotNull(job.FirePoint);
        Assert.Equal(2, job.FirePoint!.PrintTrackingDevice);
        Assert.Equal(800, job.FirePoint.PrintFirePoint);
        Assert.Equal(3, job.FirePoint.ApplyTrackingDevice);
        Assert.Equal(ApplyPoint.Parse("1T"), job.FirePoint.ApplyFirePoint);
    }

    [Fact]
    public async Task Induct_NoActiveProfile_LeavesFirePointNull()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));

        await _induct.InductAsync("L1", "BLIND1");

        Assert.Null(_gateway.Jobs.Single(j => j.LabelType == "Shipping").FirePoint);
    }

    [Fact]
    public async Task Induct_WithPrinterStatusSuffix_AppendsHsToZpl()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)], printerStatusSuffix: true));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));

        await _induct.InductAsync("L1", "BLIND1");

        Assert.Equal("^XAShipping^XZ~HS", _gateway.Jobs.Single(j => j.LabelType == "Shipping").Zpl);
    }

    [Fact]
    public async Task Induct_WithoutPrinterStatusSuffix_LeavesZplUnchanged()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)])); // default: no suffix
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));

        await _induct.InductAsync("L1", "BLIND1");

        Assert.Equal("^XAShipping^XZ", _gateway.Jobs.Single(j => j.LabelType == "Shipping").Zpl);
    }

    [Fact]
    public async Task Induct_FromInductScan_StampsPhysicalMeasurementsOnCarton()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));

        var scan = new InductScan("L1", "BLIND1",
            FrontGap: 120, Length: 480, Width: 300, Height: 220, Weight: 1500,
            SorterNumber: 2, SorterMode: 1, DeviceId: 7, SeqNum: 4242,
            ScannedLabels: ["BLIND1"]);

        var result = await _induct.InductAsync(scan);

        Assert.Equal(InductStatus.Printed, result.Status);
        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        var m = stored!.InductMeasurements;
        Assert.NotNull(m);
        Assert.Equal(120, m!.FrontGap);
        Assert.Equal(480, m.Length);
        Assert.Equal(220, m.Height);
        Assert.Equal(1500, m.Weight);
        Assert.Equal(7, m.DeviceId);
        Assert.Equal(4242, m.SeqNum);
        Assert.Equal(2, m.SorterNumber);
    }

    [Fact]
    public async Task Induct_BareBlindLabel_LeavesMeasurementsAtDefaults()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));

        await _induct.InductAsync("L1", "BLIND1"); // identity-only convenience path

        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.NotNull(stored!.InductMeasurements);
        Assert.Equal(0, stored.InductMeasurements!.Length); // no physical bundle plumbed
        Assert.Equal(int.MaxValue, stored.InductMeasurements.FrontGap); // gap always passes classification
    }

    [Fact]
    public async Task Induct_FromInductScan_UnknownBlindLabel_ReturnsNoActiveOrder()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));

        var result = await _induct.InductAsync(new InductScan("L1", "NOPE", Length: 400));

        Assert.Equal(InductStatus.NoActiveOrder, result.Status);
        Assert.Empty(_gateway.Jobs);
    }

    private sealed class FixedMinGap(int minGap) : IMinGapProvider
    {
        public int GetMinGap() => minGap;
    }

    [Fact]
    public async Task Induct_FrontGapBelowMinimum_ClassifiesGapError_ButStillPrints()
    {
        var induct = new InductService(
            _store, _lines, new PrinterSelectionService(), _gateway, _clock, new InMemorySettingsProvider(),
            new FixedMinGap(100));
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));

        var result = await induct.InductAsync(new InductScan("L1", "BLIND1", FrontGap: 50, Length: 400));

        Assert.Equal(InductStatus.Printed, result.Status); // F20 stamps; it does not gate the print on its own
        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(CartonStatus.GapError, stored!.StatusAtInduct);
    }

    [Fact]
    public async Task Induct_AdequateFrontGap_ClassifiesPrintReady()
    {
        var induct = new InductService(
            _store, _lines, new PrinterSelectionService(), _gateway, _clock, new InMemorySettingsProvider(),
            new FixedMinGap(100));
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));

        await induct.InductAsync(new InductScan("L1", "BLIND1", FrontGap: 250, Length: 400));

        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(CartonStatus.PrintReady, stored!.StatusAtInduct);
    }
}

