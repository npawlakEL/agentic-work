using PandA.Core;
using PandA.Core.Domain;
using PandA.Core.Induct;
using PandA.Core.Labels;
using PandA.Core.Ports;
using PandA.Core.Settings;
using PandA.Core.Slots;
using PandA.Core.Verification;
using PandA.Sim;
using PandA.Sim.Stores;

namespace PandA.Tests;

/// <summary>
/// Regression tests for the product owner's SQL-fidelity rulings (2026-10-21): each test pins C# behavior to
/// the cited source proc behavior.
/// </summary>
public sealed class SqlFidelityRulingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTransportOrderStore _store = new();
    private readonly InMemoryLineProvider _lines = new();
    private readonly CapturingPrinterGateway _gateway = new();
    private readonly TestClock _clock = new(T0);

    private static PrinterConfig Printer(string id, string[] map, int order) =>
        new(id, "10.0.0." + (order + 1), 9100, map, ApplyOrientation.Side, order);

    private static PandaLabelSet Labels(params (string Type, string Lpn)[] labels) =>
        new(labels.Select(l => new Label(l.Type, l.Lpn, $"^XA^FD{l.Lpn}^FS^XZ")));

    private InductService Induct(
        ISettingsProvider? settings = null,
        IPrinterGateway? gateway = null,
        ISlotIndexProvider? slots = null,
        bool exceptions = false) =>
        TestServices.Induct(_store, _lines, gateway ?? _gateway, _clock,
            settings ?? new InMemorySettingsProvider(),
            exceptionLabels: exceptions ? new LocalTemplateSource(new InMemoryLabelTemplateRepository()) : null,
            slots: slots);

    private CartonAdviceService Advice(ISettingsProvider? settings = null) =>
        TestServices.Advice(_store, _clock, settings ?? new InMemorySettingsProvider());

    private static async Task<InMemorySettingsProvider> Settings<T>(SettingsDescriptor<T> descriptor, T value, bool seed = true)
    {
        var settings = new InMemorySettingsProvider(seed);
        await settings.SetAsync(descriptor, value);
        return settings;
    }

    // ---- 1. Gap error → no print, no exception label ---------------------------------------------

    [Fact]
    public async Task Item1_GapError_WithResolvedOrder_PrintsNothing_AndNoExceptionLabel()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)], printExceptionLabels: true));
        await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));
        var settings = await Settings(KnownSettings.PrintExceptionLabels, true, seed: false);

        var result = await Induct(settings, exceptions: true).InductAsync(new InductScan("L1", "BL1", FrontGap: 5));

        Assert.Equal(InductStatus.NotPrintReady, result.Status);
        Assert.Equal(CartonStatus.GapError, result.Classification);
        Assert.Empty(_gateway.Jobs);
        Assert.Equal(0, (await _store.FindActiveByTuIdAsync("BL1"))!.PrintCount);
    }

    [Fact]
    public void Item1_GapError_HasNoExceptionLabel()
    {
        Assert.Null(ExceptionLabelPolicy.Classify(CartonStatus.GapError));
    }

    [Fact]
    public async Task Item13b_MinGap_DefaultsToTheMinGapSetting()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));
        await Advice().AdviseAsync("L1", "BL2", Labels(("Shipping", "S2")));
        var induct = Induct(); // seeded MinGap = 20 (source Settings seed), no IMinGapProvider

        var tooClose = await induct.InductAsync(new InductScan("L1", "BL1", FrontGap: 19));
        var ok = await induct.InductAsync(new InductScan("L1", "BL2", FrontGap: 20));

        Assert.Equal(InductStatus.NotPrintReady, tooClose.Status);
        Assert.Equal(InductStatus.Printed, ok.Status);
    }

    // ---- 2. Bypass → no print --------------------------------------------------------------------

    [Fact]
    public async Task Item2_BypassOrder_PrintsNothing_AndNoExceptionLabel()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)], printExceptionLabels: true));
        var order = await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));
        order.SetAdviceMetadata(bypass: true);
        await _store.SaveAsync(order);
        var settings = await Settings(KnownSettings.PrintExceptionLabels, true, seed: false);

        var result = await Induct(settings, exceptions: true).InductAsync("L1", "BL1");

        Assert.Equal(InductStatus.Bypass, result.Status);
        Assert.Empty(_gateway.Jobs);
    }

    // ---- 4. Exception carton at verify → saved as a bad verify -----------------------------------

    [Fact]
    public async Task Item4_ExceptionCarton_IsPersisted_AndFailsVerify_WithCode0_RejectAndThreshold()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)], printExceptionLabels: true));
        var settings = await Settings(KnownSettings.PrintExceptionLabels, true, seed: false);
        var induct = await Induct(settings, exceptions: true).InductAsync(new InductScan("L1", "BL-UNADVISED", SeqNum: 7));
        Assert.Equal(InductStatus.ExceptionLabel, induct.Status);
        var cartonId = induct.ExceptionCartonId!;

        var exceptionOrder = await _store.FindActiveByTuIdAsync(cartonId);
        Assert.NotNull(exceptionOrder);
        Assert.True(exceptionOrder!.IsExceptionCarton);
        Assert.True(exceptionOrder.VerifyEnabled);
        Assert.Equal(TransportOrderStatus.Printed, exceptionOrder.Status);

        var threshold = TestServices.Threshold();
        var rejects = new InMemoryRejectHistoryRepository();
        var verify = TestServices.Verify(_store, threshold, _clock, rejects: rejects);

        var result = await verify.VerifyAsync(cartonId, [new ScannedLabel("Exception", cartonId)], new VerifyOptions(), failThreshold: 3);

        Assert.Equal(VerifyStationStatus.HeldForIntervention, result.Status);
        Assert.Equal("Fail", result.Routing!.Value);
        Assert.Equal(VerifyReasonCode.PandaLogicError, result.Routing.ReasonCode);
        var reject = Assert.Single(rejects.Records);
        Assert.Equal(VerifyReasonCode.PandaLogicError, reject.Code);
        Assert.Equal(1, (await threshold.CurrentCountAsync("Ship1")));
    }

    [Fact]
    public async Task Item4_UnknownCartonAtVerify_FailsWithCode0_AndWritesRejectHistory()
    {
        var rejects = new InMemoryRejectHistoryRepository();
        var verify = TestServices.Verify(_store, TestServices.Threshold(), _clock, rejects: rejects);

        var result = await verify.VerifyAsync("NOPE", [new ScannedLabel("Shipping", "X")], new VerifyOptions(), failThreshold: 3);

        Assert.Equal(VerifyStationStatus.NoActiveOrder, result.Status);
        Assert.Equal("Fail", result.Routing!.Value);
        var reject = Assert.Single(rejects.Records);
        Assert.Equal(VerifyReasonCode.PandaLogicError, reject.Code);
        Assert.Equal(0, reject.PandaDataId);
    }

    // ---- 5. Verify fail streak per printer ---------------------------------------------------------

    private async Task<VerifyStationService> TwoPrinterCartonAsync(
        string tuId, VerifyThresholdTracker threshold, IRejectHistoryRepository? rejects = null)
    {
        if (await _lines.GetLineAsync("L1") is null)
        {
            _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0), Printer("Cont1", ["Content"], 1)]));
        }

        await Advice().AdviseAsync("L1", tuId, Labels(("Shipping", "S-" + tuId), ("Content", "C-" + tuId)));
        Assert.Equal(InductStatus.Printed, (await Induct().InductAsync("L1", tuId)).Status);
        return TestServices.Verify(_store, threshold, _clock, rejects: rejects);
    }

    [Fact]
    public async Task Item5_FailStreak_IsCountedPerPrinterThatPrinted_AndPausesThosePrinters()
    {
        var threshold = TestServices.Threshold();

        var v1 = await TwoPrinterCartonAsync("B1", threshold);
        var first = await v1.VerifyAsync("B1", [new ScannedLabel("BlindLabel", "B1")], new VerifyOptions(), failThreshold: 2);
        Assert.False(first.PrinterPaused);
        Assert.Equal(1, (await threshold.CurrentCountAsync("Ship1")));
        Assert.Equal(1, (await threshold.CurrentCountAsync("Cont1")));
        Assert.Equal(0, (await threshold.CurrentCountAsync("L1"))); // no longer keyed by line

        var v2 = await TwoPrinterCartonAsync("B2", threshold);
        var second = await v2.VerifyAsync("B2", [new ScannedLabel("BlindLabel", "B2")], new VerifyOptions(), failThreshold: 2);

        Assert.True(second.PrinterPaused);
        Assert.Equal(["Cont1", "Ship1"], second.PrintersToPause!.Order(StringComparer.Ordinal));
        Assert.Equal(0, (await threshold.CurrentCountAsync("Ship1"))); // source _Refresh after ~PP
    }

    [Fact]
    public async Task Item5_Pass_ResetsEachPrintersStreak()
    {
        var threshold = TestServices.Threshold();
        var v1 = await TwoPrinterCartonAsync("B1", threshold);
        await v1.VerifyAsync("B1", [new ScannedLabel("BlindLabel", "B1")], new VerifyOptions(), failThreshold: 3);
        Assert.Equal(1, (await threshold.CurrentCountAsync("Ship1")));

        var v2 = await TwoPrinterCartonAsync("B2", threshold);
        var pass = await v2.VerifyAsync(
            "B2",
            [new ScannedLabel("BlindLabel", "B2"), new ScannedLabel("Shipping", "S-B2"), new ScannedLabel("Content", "C-B2")],
            new VerifyOptions(),
            failThreshold: 3);

        Assert.Equal(VerifyStationStatus.Verified, pass.Status);
        Assert.Equal(0, (await threshold.CurrentCountAsync("Ship1")));
        Assert.Equal(0, (await threshold.CurrentCountAsync("Cont1")));
    }

    // ---- 6+7. Reprint Labels setting governs re-arm ------------------------------------------------

    [Fact]
    public async Task Item6_ReprintLabelsUnset_DefaultsOn()
    {
        var settings = new InMemorySettingsProvider(seedKnownSettings: false);
        Assert.True(await settings.GetAsync(KnownSettings.ReprintLabels));
    }

    [Fact]
    public async Task Item6_ReprintLabelsOn_AlreadyPrintedCarton_PrintsAgain_WithoutAuthorization()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));
        var induct = Induct(await Settings(KnownSettings.ReprintLabels, true));
        await induct.InductAsync("L1", "BL1");

        var again = await induct.InductAsync("L1", "BL1");

        Assert.Equal(InductStatus.Printed, again.Status);
        Assert.Equal(2, _gateway.Jobs.Count);
        Assert.Equal(2, (await _store.FindActiveByTuIdAsync("BL1"))!.PrintCount);
    }

    [Fact]
    public async Task Item6_ReprintLabelsOff_AlreadyPrintedCarton_NeedsAuthorization()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));
        var induct = Induct(await Settings(KnownSettings.ReprintLabels, false));
        await induct.InductAsync("L1", "BL1");

        Assert.Equal(InductStatus.NoReprint, (await induct.InductAsync("L1", "BL1")).Status);
        Assert.Single(_gateway.Jobs);
    }

    [Fact]
    public async Task Item7_ReAdvice_ReprintOff_UpdatesData_ButDoesNotReArmPrintedCarton()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        var settings = await Settings(KnownSettings.ReprintLabels, false);
        await Advice(settings).AdviseAsync("L1", "BL1", Labels(("Shipping", "OLD")));
        await Induct(settings).InductAsync("L1", "BL1");

        var readvised = await Advice(settings).AdviseAsync("L1", "BL1", Labels(("Shipping", "NEW")));

        Assert.Equal("NEW", Assert.Single(readvised.Labels.Labels).Lpn);
        Assert.Equal(TransportOrderStatus.Printed, readvised.Status);
        Assert.Equal(1, readvised.PrintCount);
        Assert.Equal(InductStatus.NoReprint, (await Induct(settings).InductAsync("L1", "BL1")).Status);
    }

    [Fact]
    public async Task Item7_ReAdvice_ReprintOn_ReArmsPrintedCarton()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        var settings = await Settings(KnownSettings.ReprintLabels, true);
        await Advice(settings).AdviseAsync("L1", "BL1", Labels(("Shipping", "OLD")));
        await Induct(settings).InductAsync("L1", "BL1");

        var readvised = await Advice(settings).AdviseAsync("L1", "BL1", Labels(("Shipping", "NEW")));

        Assert.Equal(TransportOrderStatus.Advised, readvised.Status);
        Assert.Equal(0, readvised.PrintCount);
    }

    // ---- 8. Missing fire point / NeglectPrint → label skipped -------------------------------------

    [Fact]
    public async Task Item8_MissingFirePointRow_SkipsThatLabel_OthersPrint()
    {
        var profile = new FirePointProfile("P", [(("Ship1", "Shipping"), new FirePoint(2, 800, 3, ApplyPoint.Parse("1L")))]);
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0), Printer("Cont1", ["Content"], 1)], activeProfile: profile));
        await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "S1"), ("Content", "C1")));

        var result = await Induct().InductAsync("L1", "BL1");

        Assert.Equal("Shipping", Assert.Single(_gateway.Jobs).LabelType);
        Assert.Equal(LabelSelectionStatus.NoFirePoint, result.Assignments.Single(a => a.Label.LabelType == "Content").Status);
        Assert.NotEqual(InductStatus.Printed, result.Status);
    }

    [Fact]
    public async Task Item8_NeglectPrintFirePoint_SkipsLabel_AndStampsLastPrinted()
    {
        var profile = new FirePointProfile("P", [(("Ship1", "Shipping"), new FirePoint(2, 0, 3, ApplyPoint.Parse("1L")))]);
        var state = new PrinterState("Ship1");
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)], activeProfile: profile), [state]);
        await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));

        var result = await Induct().InductAsync("L1", "BL1");

        Assert.Empty(_gateway.Jobs);
        Assert.Equal(LabelSelectionStatus.NoFirePoint, Assert.Single(result.Assignments).Status);
        Assert.Equal(T0, _lines.PrinterStates.Get("Ship1")!.LastPrinted);
    }

    // ---- 9. LastPrinted stamped on every attempt ---------------------------------------------------

    private sealed class FailingGateway : IPrinterGateway
    {
        public ValueTask SendAsync(PrintJob job, CancellationToken cancellationToken = default) =>
            throw new IOException("printer down");
    }

    [Fact]
    public async Task Item9_DispatchFailure_StillStampsLastPrinted()
    {
        var state = new PrinterState("Ship1");
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]), [state]);
        await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));

        var result = await Induct(gateway: new FailingGateway()).InductAsync("L1", "BL1");

        Assert.Equal(LabelSelectionStatus.DispatchFailed, Assert.Single(result.Assignments).Status);
        Assert.Equal(T0, _lines.PrinterStates.Get("Ship1")!.LastPrinted);
    }

    // ---- 10. Slot assignment on every induct -------------------------------------------------------

    [Fact]
    public async Task Item10_SlotIsDrawnOnEveryInduct_IncludingNoPrint_AndRidesThePrintJob()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));
        var induct = Induct(slots: new InMemorySlotIndexProvider());

        var none = await induct.InductAsync("L1", "UNKNOWN");
        var printed = await induct.InductAsync("L1", "BL1");

        Assert.Equal(InductStatus.NoActiveOrder, none.Status);
        Assert.Equal(1, none.Slot);
        Assert.Equal(2, printed.Slot);
        Assert.Equal(2, Assert.Single(_gateway.Jobs).Slot);
    }

    [Fact]
    public async Task Item10_SlotCycles_1To300()
    {
        var slots = new InMemorySlotIndexProvider(initialSlot: 300);

        Assert.Equal(300, await slots.NextSlotAsync());
        Assert.Equal(1, await slots.NextSlotAsync());
    }

    // ---- 11 + 12. Blind label required; reason codes, reject history, destinations ----------------

    private async Task<(VerifyStationService Verify, InMemoryRejectHistoryRepository Rejects)> VerifyFixtureAsync(IXRefStore? xref = null)
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        var order = await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));
        order.SetAdviceMetadata(verifyPassDest: "Ship Lane", verifyFailDest: "Reject Lane");
        await _store.SaveAsync(order);
        await Induct().InductAsync("L1", "BL1");
        var rejects = new InMemoryRejectHistoryRepository();
        return (TestServices.Verify(_store, TestServices.Threshold(), _clock, rejects: rejects, xref: xref), rejects);
    }

    [Fact]
    public async Task Item11_MissingBlindLabelRead_Fails_WithCode14_AndRejectHistory()
    {
        var (verify, rejects) = await VerifyFixtureAsync();

        var result = await verify.VerifyAsync("BL1", [new ScannedLabel("Shipping", "S1")], new VerifyOptions(), failThreshold: 3);

        Assert.Equal(VerifyStationStatus.HeldForIntervention, result.Status);
        Assert.Equal(VerifyReasonCode.BlindLabelNotDetected, result.Routing!.ReasonCode);
        Assert.Equal(VerifyReasonCode.BlindLabelNotDetected, Assert.Single(rejects.Records).Code);
    }

    [Fact]
    public async Task Item11_BlindLabelXrefFromCartonXrefStore_IsAcceptable()
    {
        var xref = new InMemoryXRefStore();
        await xref.AddAsync("BL1", new XRef("UPC-123", BarcodeType.UPC));
        var (verify, rejects) = await VerifyFixtureAsync(xref);

        var result = await verify.VerifyAsync(
            "BL1", [new ScannedLabel("BlindLabel", "UPC-123"), new ScannedLabel("Shipping", "S1")], new VerifyOptions(), failThreshold: 3);

        Assert.Equal(VerifyStationStatus.Verified, result.Status);
        Assert.Empty(rejects.Records);
    }

    [Fact]
    public async Task Item12_WrongShippingLabel_Code21_RejectHistory_FailDestination()
    {
        var (verify, rejects) = await VerifyFixtureAsync();

        var result = await verify.VerifyAsync(
            "BL1", [new ScannedLabel("BlindLabel", "BL1"), new ScannedLabel("Shipping", "WRONG")], new VerifyOptions(), failThreshold: 3);

        Assert.Equal("Fail", result.Routing!.Value);
        Assert.Equal(VerifyReasonCode.IncorrectShippingLabelApplied, result.Routing.ReasonCode);
        Assert.Equal("Reject Lane", result.Routing.Destination);
        var reject = Assert.Single(rejects.Records);
        Assert.Equal(VerifyReasonCode.IncorrectShippingLabelApplied, reject.Code);
        Assert.Equal("BL1", reject.TuId);
    }

    [Fact]
    public async Task Item12_ShippingNoRead_Code20()
    {
        var (verify, _) = await VerifyFixtureAsync();

        var result = await verify.VerifyAsync(
            "BL1", [new ScannedLabel("BlindLabel", "BL1"), new ScannedLabel("Shipping", "?")], new VerifyOptions(), failThreshold: 3);

        Assert.Equal(VerifyReasonCode.ShippingLabelNoRead, result.Routing!.ReasonCode);
    }

    [Fact]
    public async Task Item12_Pass_RoutesToPassDestination_NoRejectHistory()
    {
        var (verify, rejects) = await VerifyFixtureAsync();

        var result = await verify.VerifyAsync(
            "BL1", [new ScannedLabel("BlindLabel", "BL1"), new ScannedLabel("Shipping", "S1")], new VerifyOptions(), failThreshold: 3);

        Assert.Equal("Pass", result.Routing!.Value);
        Assert.Equal(VerifyReasonCode.VerificationPassed, result.Routing.ReasonCode);
        Assert.Equal("Ship Lane", result.Routing.Destination);
        Assert.Empty(rejects.Records);
    }

    [Theory]
    [InlineData("Shipping", VerifyLabelReason.Missing, VerifyReasonCode.ShippingLabelNoRead)]
    [InlineData("Content", VerifyLabelReason.NoRead, VerifyReasonCode.ContentLabelNoRead)]
    [InlineData("Parcel", VerifyLabelReason.Missing, VerifyReasonCode.ParcelLabelNoRead)]
    [InlineData("BlindLabel", VerifyLabelReason.Missing, VerifyReasonCode.BlindLabelNotDetected)]
    [InlineData("Shipping", VerifyLabelReason.Mismatch, VerifyReasonCode.IncorrectShippingLabelApplied)]
    [InlineData("Content", VerifyLabelReason.Mismatch, VerifyReasonCode.IncorrectContentLabelApplied)]
    [InlineData("Parcel", VerifyLabelReason.Conflict, VerifyReasonCode.ParcelLabelConflict)]
    [InlineData("BlindLabel", VerifyLabelReason.Conflict, VerifyReasonCode.BlindLabelConflict)]
    [InlineData("Shipping", VerifyLabelReason.NoData, VerifyReasonCode.VerifyScanError)]
    public void Item12_ReasonCodeMapping_MatchesSource(string labelType, VerifyLabelReason reason, VerifyReasonCode expected)
    {
        var result = new VerifyResult(VerifyOutcome.Fail, [new VerifyLabelDetail(labelType, "E", "S", reason)]);

        Assert.Equal(expected, VerifyReasonCodes.For(result));
    }

    // ---- 13a. OverwriteLabelData -------------------------------------------------------------------

    [Fact]
    public async Task Item13a_OverwriteLabelDataOff_DuplicateUnprintedAdvice_OldestWins()
    {
        await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "FIRST")));
        await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "SECOND")));

        var order = await _store.FindActiveByTuIdAsync("BL1");
        Assert.Equal("FIRST", Assert.Single(order!.Labels.Labels).Lpn);
    }

    [Fact]
    public async Task Item13a_OverwriteLabelDataOn_DuplicateAdvice_Replaces()
    {
        var settings = await Settings(KnownSettings.OverwriteLabelData, true);
        await Advice(settings).AdviseAsync("L1", "BL1", Labels(("Shipping", "FIRST")));
        await Advice(settings).AdviseAsync("L1", "BL1", Labels(("Shipping", "SECOND")));

        var order = await _store.FindActiveByTuIdAsync("BL1");
        Assert.Equal("SECOND", Assert.Single(order!.Labels.Labels).Lpn);
    }

    // ---- 13c. 2 Printer Rule before spare promotion; per-line overrides global ---------------------

    private static LineConfig TwoPrinterLine(bool? rule) =>
        new("L1",
            [new PrinterConfig("A", "10.0.0.1", 9100, ["Shipping"], ApplyOrientation.Side), new PrinterConfig("B", "10.0.0.2", 9100, ["Shipping"], ApplyOrientation.Side)],
            printerPolicies: new Dictionary<ApplyOrientation, PrinterGroupPolicy> { [ApplyOrientation.Side] = new(1, 2, 1, false) },
            twoPrinterRule: rule);

    private static Dictionary<string, PrinterState> SpareOnlineOtherOffline() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = new PrinterState("A", isSpare: true),
        ["B"] = new PrinterState("B", plcOnline: false),
    };

    /// <summary>Lane-evaluate over a shared printer-state store seeded with <paramref name="states"/>.</summary>
    private static async Task<(LaneEvalResult Result, InMemoryPrinterStateStore Store)> EvalShared(
        LineConfig line,
        Dictionary<string, PrinterState> states,
        ISettingsProvider? settings = null)
    {
        var store = new InMemoryPrinterStateStore();
        foreach (var state in states.Values)
        {
            store.Put(state);
        }

        var result = await TestServices.LaneEval(store, settings).EvaluateAsync(line, new ZoneState(), T0);
        return (result, store);
    }

    [Fact]
    public async Task Item13c_TwoPrinterRuleOn_RunsSlow_BeforePromotingSpare()
    {
        var (result, store) = await EvalShared(TwoPrinterLine(true), SpareOnlineOtherOffline());

        Assert.Equal(LineControl.SlowLine, result.Control);
        Assert.Empty(result.Changes);
        Assert.True(store.Get("A")!.IsSpare);
    }

    [Fact]
    public async Task Item13c_TwoPrinterRuleOff_PromotesSpare()
    {
        var (result, store) = await EvalShared(TwoPrinterLine(false), SpareOnlineOtherOffline());

        Assert.Contains(result.Changes, c => c.PrinterId == "A" && c.Change == SpareChange.PromotedFromSpare);
        Assert.False(store.Get("A")!.IsSpare); // promotion persisted to the shared state
    }

    [Fact]
    public async Task Item13c_GlobalSetting_AppliesWhenLineUnset_PerLineOverrides()
    {
        var global = await Settings(KnownSettings.TwoPrinterRule, true);
        var (fromGlobal, _) = await EvalShared(TwoPrinterLine(null), SpareOnlineOtherOffline(), global);
        var (lineOff, _) = await EvalShared(TwoPrinterLine(false), SpareOnlineOtherOffline(), global);

        Assert.Equal(LineControl.SlowLine, fromGlobal.Control);
        Assert.NotEmpty(lineOff.Changes);
    }

    // ---- 14. Printer with no state row counts offline ---------------------------------------------

    [Fact]
    public void Item14_PrinterWithoutStateRow_IsNotSelected()
    {
        var line = new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]);

        var result = new PrinterSelectionService().Select(line, new Dictionary<string, PrinterState>(), Labels(("Shipping", "S1")));

        Assert.False(result.AllAssigned);
    }

    [Fact]
    public void Item14_LaneEval_StatelessPrinter_DoesNotCountOnline()
    {
        var line = new LineConfig("L1",
            [new PrinterConfig("A", "10.0.0.1", 9100, ["Shipping"], ApplyOrientation.Side), new PrinterConfig("B", "10.0.0.2", 9100, ["Shipping"], ApplyOrientation.Side)],
            printerPolicies: new Dictionary<ApplyOrientation, PrinterGroupPolicy> { [ApplyOrientation.Side] = new(2, 2, 1, true) });
        var states = new Dictionary<string, PrinterState>(StringComparer.OrdinalIgnoreCase) { ["A"] = new PrinterState("A") };

        var result = TestServices.LaneEval(new InMemoryPrinterStateStore()).Evaluate(line, states, new ZoneState(), T0);

        Assert.NotEqual(LineControl.Balanced, result.Control);
    }
}
