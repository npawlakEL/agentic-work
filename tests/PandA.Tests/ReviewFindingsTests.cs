using PandA.Core;
using PandA.Core.Concurrency;
using PandA.Core.Induct;
using PandA.Core.Labels;
using PandA.Core.Verification;
using PandA.Sim;

namespace PandA.Tests;

/// <summary>Regression tests for the PandA.Core code-review findings (#1–#10).</summary>
public sealed class ReviewFindingsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTransportOrderStore _store = new();
    private readonly InMemoryLineProvider _lines = new();
    private readonly CapturingPrinterGateway _gateway = new();
    private readonly TestClock _clock = new(T0);
    private readonly CartonAdviceService _advice;

    public ReviewFindingsTests()
    {
        _advice = TestServices.Advice(_store, _clock, new InMemorySettingsProvider());
    }

    private static PrinterConfig Printer(
        string id,
        string[] map,
        int order,
        int defaultApplyDistance = 0,
        decimal labelWidthInches = 4m) =>
        new(id, "10.0.0." + order, 9100, map, ApplyOrientation.Side, order, defaultApplyDistance, labelWidthInches);

    private static PandaLabelSet Labels(params string[] types) =>
        new(types.Select(t => new Label(t, $"LPN-{t}", $"^XA^FD{t}^FS^XZ")));

    private InductService Induct(IPrinterGateway? gateway = null, ILockProvider? locks = null) =>
        TestServices.Induct(_store, _lines, gateway ?? _gateway, _clock, TestSettings.ReprintOff(),
            locks: locks);

    private InductService ExceptionInduct() =>
        TestServices.Induct(_store, _lines, _gateway, _clock,
            new InMemorySettingsProvider(seedKnownSettings: false),
            exceptionLabels: new LocalTemplateSource(new InMemoryLabelTemplateRepository()));

    private static FirePointProfile SideProfile(string printerId, string applyPoint) =>
        new("Generic", [((printerId, "Shipping"), new FirePoint(2, 800, 3, ApplyPoint.Parse(applyPoint)))]);

    // ---- #1 ZPL sanitize (F11 VetLabel) ----

    [Fact]
    public async Task Finding1_FilterLabelsOn_SanitizesNormalLabelZpl_BeforeHsSuffix()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)], printerStatusSuffix: true));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));

        await Induct().InductAsync("L1", "BLIND1");

        Assert.Equal("^XA^LH13,0^FDShipping^FS^XZ~HS", Assert.Single(_gateway.Jobs).Zpl);
    }

    [Fact]
    public async Task Finding1_FilterLabelsOff_LeavesZplUnsanitized()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)], filterLabels: false));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));

        await Induct().InductAsync("L1", "BLIND1");

        Assert.Equal("^XA^FDShipping^FS^XZ", Assert.Single(_gateway.Jobs).Zpl);
    }

    [Fact]
    public async Task Finding1_FilterLabelsOn_SanitizesExceptionLabelZpl()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)], printExceptionLabels: true));

        await ExceptionInduct().InductAsync(new InductScan("L1", "???", SeqNum: 1));

        Assert.StartsWith("^XA^LH13,0", Assert.Single(_gateway.Jobs).Zpl, StringComparison.Ordinal);
    }

    // ---- #2 per-carton Bypass / VerifyEnabled ----

    private VerifyStationService VerifyStation() =>
        TestServices.Verify(_store, TestServices.Threshold(), _clock);

    private async Task SeedOrderAsync(string tuId, bool bypass, bool verifyEnabled)
    {
        var order = new TransportOrder(tuId, "L1", new PandaLabelSet([new Label("Shipping", "SHIP1", "^XA^XZ")]), T0);
        order.SetAdviceMetadata(bypass: bypass, verifyEnabled: verifyEnabled);
        await _store.SaveAsync(order);
    }

    [Fact]
    public async Task Finding2_OrderVerifyDisabledAndBypassed_IsIgnored_EvenWhenStationVerifies()
    {
        await SeedOrderAsync("BL1", bypass: true, verifyEnabled: false);

        var result = await VerifyStation().VerifyAsync("BL1", [], new VerifyOptions(), failThreshold: 3);

        Assert.Equal(VerifyOutcome.Ignore, result.Verify!.Outcome);
        Assert.Equal(VerifyStationStatus.Verified, result.Status);
    }

    [Fact]
    public async Task Finding2_OrderVerifyDisabledWithoutBypass_DoesNotRunLabelComparison()
    {
        await SeedOrderAsync("BL1", bypass: false, verifyEnabled: false);
        var stationWithBypass = new VerifyOptions(VerifyEnabled: true, Bypass: false);

        var carton = await VerifyStation().VerifyAsync(
            "BL1", [new ScannedLabel("Shipping", "SHIP1")], stationWithBypass, failThreshold: 3);
        var baseline = new VerificationService().Verify(
            new PandaLabelSet([new Label("Shipping", "SHIP1", "^XA^XZ")]),
            [new ScannedLabel("Shipping", "SHIP1")],
            new VerifyOptions(VerifyEnabled: false, Bypass: false));

        // The carton's VerifyEnabled=false takes effect: same outcome as a station with verify disabled.
        Assert.Equal(baseline.Outcome, carton.Verify!.Outcome);
        Assert.NotEqual(VerifyOutcome.Pass, carton.Verify.Outcome);
    }

    // ---- #3 blind label in the expected verify set ----

    private static readonly PandaLabelSet ShipOnly = new([new Label("Shipping", "SHIP1", "^XA^XZ")]);

    [Fact]
    public void Finding3_BlindLabelRead_MatchingTuId_Passes()
    {
        var scanned = LabelBufferOrder.Default.Type(["BL123", "SHIP1"]);

        var result = new VerificationService().Verify(ShipOnly, scanned, new VerifyOptions(), blindLabel: "BL123");

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void Finding3_BlindLabelRead_ConflictingWithTuId_Fails()
    {
        var scanned = LabelBufferOrder.Default.Type(["BL999", "SHIP1"]);

        var result = new VerificationService().Verify(ShipOnly, scanned, new VerifyOptions(), blindLabel: "BL123");

        Assert.Equal(VerifyOutcome.Fail, result.Outcome);
    }

    [Fact]
    public void Finding3_MissingBlindLabelRead_Fails_WithBlindLabelReasonCode()
    {
        // Source TOOL_PA_VerifyLabel L415-427: a leftover expected blind label fails with code 14
        // (owner ruling 2026-10-21; previously treated as optional).
        var scanned = LabelBufferOrder.Default.Type(["", "SHIP1"]);

        var result = new VerificationService().Verify(ShipOnly, scanned, new VerifyOptions(), blindLabel: "BL123");

        Assert.NotEqual(VerifyOutcome.Pass, result.Outcome);
        Assert.Equal(VerifyReasonCode.BlindLabelNotDetected, VerifyReasonCodes.For(result));
    }

    [Fact]
    public void Finding3_BlindLabelXrefAlternate_Passes()
    {
        var scanned = LabelBufferOrder.Default.Type(["ALT-BL", "SHIP1"]);

        var result = new VerificationService().Verify(
            ShipOnly, scanned, new VerifyOptions(), [new LabelXref("BlindLabel", "ALT-BL")], blindLabel: "BL123");

        Assert.Equal(VerifyOutcome.Pass, result.Outcome);
    }

    [Fact]
    public async Task Finding3_VerifyStation_RealBufferWithBlindLabel_Verifies()
    {
        await SeedOrderAsync("BL123", bypass: false, verifyEnabled: true);

        var result = await VerifyStation().VerifyAsync(
            "BL123", LabelBufferOrder.Default.Type(["BL123", "SHIP1"]), new VerifyOptions(), failThreshold: 3);

        Assert.Equal(VerifyOutcome.Pass, result.Verify!.Outcome);
    }

    // ---- #4 exception printer availability ----

    [Fact]
    public async Task Finding4_ExceptionLabel_SkipsUnavailablePrinter()
    {
        _lines.Add(
            new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0), Printer("Ship2", ["Shipping"], 1)], printExceptionLabels: true),
            [new PrinterState("Ship1", plcOnline: false), new PrinterState("Ship2")]);

        var result = await ExceptionInduct().InductAsync(new InductScan("L1", "???", SeqNum: 1));

        Assert.Equal(InductStatus.ExceptionLabel, result.Status);
        Assert.Equal("Ship2", Assert.Single(_gateway.Jobs).PrinterId);
    }

    [Fact]
    public async Task Finding4_ExceptionLabel_NoAvailablePrinter_ReturnsNoPrinter()
    {
        _lines.Add(
            new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0), Printer("Ship2", ["Shipping"], 1)], printExceptionLabels: true),
            [new PrinterState("Ship1", isSpare: true), new PrinterState("Ship2", engineOnline: false)]);

        var result = await ExceptionInduct().InductAsync(new InductScan("L1", "???", SeqNum: 1));

        Assert.Equal(InductStatus.NoPrinter, result.Status);
        Assert.Empty(_gateway.Jobs);
    }

    [Fact]
    public async Task Finding4_ExceptionLabel_StampsLastPrinted()
    {
        var states = new[] { new PrinterState("Ship1") };
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)], printExceptionLabels: true), states);

        await ExceptionInduct().InductAsync(new InductScan("L1", "???", SeqNum: 1));

        Assert.Equal(T0, _lines.PrinterStates.Get("Ship1")!.LastPrinted); // shared printer-state store
    }

    // ---- #5 ApplyPoint.Parse ----

    [Theory]
    [InlineData("1,5L")]
    [InlineData("1,000T")]
    public void Finding5_Parse_RejectsThousandsSeparator(string text)
    {
        Assert.Throws<FormatException>(() => ApplyPoint.Parse(text));
    }

    [Fact]
    public void Finding5_Parse_StillAcceptsDecimalAndSign()
    {
        Assert.Equal(1.5m, ApplyPoint.Parse("1.5L").Inches);
        Assert.Equal(-0.5m, ApplyPoint.Parse("-0.5M").Inches);
    }

    // ---- #6 per-printer DefaultApplyDistance / label width / DynamicPrintPoint ----

    [Fact]
    public async Task Finding6_ApplyPulse_IncludesPrinterDefaultApplyDistance()
    {
        // "1L" at 0.2 in/pulse = 5 pulses, plus the printer's DefaultApplyDistance of 10.
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0, defaultApplyDistance: 10)],
            activeProfile: SideProfile("Ship1", "1L")));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));

        await Induct().InductAsync(new InductScan("L1", "BLIND1", Length: 100, Height: 12));

        Assert.Equal(15, Assert.Single(_gateway.Jobs).ApplyPulse);
    }

    [Theory]
    [InlineData(4, 80)]
    [InlineData(6, 70)]
    public async Task Finding6_ApplyPulse_UsesPrinterLabelWidth(int widthInches, int expectedPulse)
    {
        // Trailing "0T": length 100 - (0 + width) / 0.2.
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0, labelWidthInches: widthInches)],
            activeProfile: SideProfile("Ship1", "0T")));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));

        await Induct().InductAsync(new InductScan("L1", "BLIND1", Length: 100, Height: 12));

        Assert.Equal(expectedPulse, Assert.Single(_gateway.Jobs).ApplyPulse);
    }

    [Fact]
    public async Task Finding6_DynamicPrintPointOff_LeavesApplyPulseNull()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)],
            activeProfile: SideProfile("Ship1", "1L"), dynamicPrintPoint: false));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));

        await Induct().InductAsync(new InductScan("L1", "BLIND1", Length: 100, Height: 12));

        Assert.Null(Assert.Single(_gateway.Jobs).ApplyPulse);
    }

    [Fact]
    public void Finding6_PrinterConfig_RejectsNonPositiveLabelWidth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Printer("P", ["Shipping"], 0, labelWidthInches: 0m));
    }

    // ---- #7 unique exception carton ids ----

    [Fact]
    public async Task Finding7_TwoNoReadInducts_WithSameSeqNum_MintDistinctCartonIds()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)], printExceptionLabels: true));
        var induct = ExceptionInduct();

        var first = await induct.InductAsync(new InductScan("L1", "???"));
        var second = await induct.InductAsync(new InductScan("L1", "???"));

        Assert.Equal(InductStatus.ExceptionLabel, first.Status);
        Assert.Equal(InductStatus.ExceptionLabel, second.Status);
        Assert.NotEqual(first.ExceptionCartonId, second.ExceptionCartonId);
        Assert.StartsWith("NoRead-000000-", first.ExceptionCartonId, StringComparison.Ordinal);
    }

    // ---- #8 exception ZPL injection ----

    [Fact]
    public void Finding8_ExceptionLpn_StripsZplControlPrefixesAndControlChars()
    {
        var template = new LabelTemplate("DataError_Duplicate", "^XA^FD<CartonID>^FS^FD<LPN>^FS^XZ", true);

        var build = new ExceptionLabelBuilder().Build(
            ExceptionType.Duplicate, "Dup^1", "AB^XZ~JR\u0001C", template);

        Assert.Equal("^XA^FDDup1^FS^FDABXZJRC^FS^XZ", build.Zpl);
    }

    // ---- #9 partial dispatch failure ----

    private sealed class FailingGateway(string failPrinterId) : IPrinterGateway
    {
        public List<PrintJob> Sent { get; } = [];

        public ValueTask SendAsync(PrintJob job, CancellationToken cancellationToken = default)
        {
            if (string.Equals(job.PrinterId, failPrinterId, StringComparison.Ordinal))
            {
                throw new IOException("printer socket closed");
            }

            Sent.Add(job);
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Finding9_OneLabelDispatchThrows_OthersPrint_PartiallyPrinted_OrderSaved()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0), Printer("Cont1", ["Content"], 1)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping", "Content"));
        var gateway = new FailingGateway("Cont1");

        var result = await Induct(gateway).InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.PartiallyPrinted, result.Status);
        Assert.Single(gateway.Sent, j => j.LabelType == "Shipping");
        Assert.Single(result.Assignments, a => a.Status == LabelSelectionStatus.DispatchFailed);

        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.NotEqual(TransportOrderStatus.Printed, stored!.Status);
        Assert.True(stored.PrintStateFor("Shipping").Printed);
        Assert.False(stored.PrintStateFor("Content").Printed);
    }

    [Fact]
    public async Task Finding9_AllDispatchesThrow_ReturnsNoPrinter()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels("Shipping"));

        var result = await Induct(new FailingGateway("Ship1")).InductAsync("L1", "BLIND1");

        Assert.Equal(InductStatus.NoPrinter, result.Status);
        Assert.True((await _store.FindActiveByTuIdAsync("BLIND1"))!.CanPrint);
    }

    // ---- #10 induct / lane-eval serialization ----

    private sealed class GatedGateway : IPrinterGateway
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Count;

        public async ValueTask SendAsync(PrintJob job, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Finding10_ConcurrentDuplicateInduct_PrintsOnce()
    {
        var tuId = "DUP-" + Guid.NewGuid().ToString("N");
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", tuId, Labels("Shipping"));
        var gateway = new GatedGateway();
        var induct = Induct(gateway, TestServices.Locks());

        var first = induct.InductAsync("L1", tuId).AsTask();
        await gateway.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = induct.InductAsync("L1", tuId).AsTask();
        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        gateway.Release.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, gateway.Count);
        Assert.Single(results, r => r.Status == InductStatus.Printed);
        Assert.Single(results, r => r.Status == InductStatus.NoReprint);
    }

    [Fact]
    public async Task Finding10_LeaseLockProvider_SerializesSameKey_NotDifferentKeys()
    {
        var locks = TestServices.Locks();
        var held = await locks.AcquireAsync("K1");

        var sameKey = locks.AcquireAsync("K1").AsTask();
        await using (await locks.AcquireAsync("K2", TimeSpan.FromSeconds(1)))
        {
            Assert.False(sameKey.IsCompleted);
        }

        await held.DisposeAsync();
        await using var second = await sameKey.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<LockTimeoutException>(async () => await locks.AcquireAsync("K1", TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public async Task Finding10_LaneEvalAsync_WaitsForPerLineLock()
    {
        var locks = TestServices.Locks();
        var printerStates = new InMemoryPrinterStateStore().Put(new PrinterState("A"));
        var sut = TestServices.LaneEval(printerStates, locks: locks);
        var line = new LineConfig("LE-" + Guid.NewGuid().ToString("N"), [Printer("A", ["Shipping"], 0)]);

        var held = await locks.AcquireAsync(LockNames.PrinterState(line.LineId));
        var pending = sut.EvaluateAsync(line, new ZoneState(true), T0).AsTask();
        await Task.Delay(50);
        Assert.False(pending.IsCompleted);

        // Another line is not blocked.
        var other = new LineConfig("LE-" + Guid.NewGuid().ToString("N"), [Printer("A", ["Shipping"], 0)]);
        await sut.EvaluateAsync(other, new ZoneState(true), T0).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        await held.DisposeAsync();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(LineControl.ShutZone, (await sut.EvaluateAsync(line, new ZoneState(false), T0)).Control);
        Assert.NotNull(result);
    }
}
