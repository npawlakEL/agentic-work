using Microsoft.Extensions.Logging;
using PandA.Core;
using PandA.Core.Domain;
using PandA.Core.Induct;
using PandA.Core.Services;
using PandA.Core.Settings;
using PandA.Core.Slots;
using PandA.Core.Verification;
using PandA.Sim;
using PandA.Sim.Stores;

namespace PandA.Tests;

/// <summary>
/// Regression tests for the product owner's rulings of 2026-10-21 (R2 slots per controller, R4 bypass passes on a
/// carton-id read, R7 verify-fail re-arm, R8 new advice overwrites, R9 no profile → no print, R11 xref cleanup,
/// R12 dynamic apply-point logging).
/// </summary>
public sealed class OwnerRulings20261021Tests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTransportOrderStore _store = new();
    private readonly InMemoryLineProvider _lines = new();
    private readonly CapturingPrinterGateway _gateway = new();
    private readonly TestClock _clock = new(T0);

    private static PrinterConfig Printer(string id, string[] map, int order = 0) =>
        new(id, "10.0.0." + (order + 1), 9100, map, ApplyOrientation.Side, order);

    private static PandaLabelSet Labels(params (string Type, string Lpn)[] labels) =>
        new(labels.Select(l => new Label(l.Type, l.Lpn, $"^XA^FD{l.Lpn}^FS^XZ")));

    private static async Task<InMemorySettingsProvider> Settings<T>(SettingsDescriptor<T> descriptor, T value)
    {
        var settings = new InMemorySettingsProvider();
        await settings.SetAsync(descriptor, value);
        return settings;
    }

    private InductService Induct(ISettingsProvider? settings = null, ISlotIndexProvider? slots = null, ILogger<InductService>? logger = null) =>
        TestServices.Induct(_store, _lines, _gateway, _clock, settings ?? new InMemorySettingsProvider(), slots: slots, logger: logger);

    private CartonAdviceService Advice(ISettingsProvider? settings = null) =>
        TestServices.Advice(_store, _clock, settings ?? new InMemorySettingsProvider());

    private static LineConfig Line(string lineId, int? plcNumber = null) =>
        new(lineId, [Printer("Ship1-" + lineId, ["Shipping"])], plcNumber: plcNumber);

    // ---- R2 slots per controller ------------------------------------------------------------------

    [Fact]
    public async Task R2_SlotCounters_AreIndependentPerControllerKey()
    {
        var slots = new InMemorySlotIndexProvider();

        Assert.Equal(1, await slots.NextSlotAsync("PLC:1"));
        Assert.Equal(2, await slots.NextSlotAsync("PLC:1"));
        Assert.Equal(1, await slots.NextSlotAsync("PLC:2"));
        Assert.Equal(3, await slots.NextSlotAsync("plc:1"));
    }

    [Fact]
    public void R2_ControllerKey_IsThePlcNumber_FallingBackToTheLine()
    {
        Assert.Equal(SlotControllerKey.For(Line("L1", 5)), SlotControllerKey.For(Line("L2", 5)));
        Assert.NotEqual(SlotControllerKey.For(Line("L1", 5)), SlotControllerKey.For(Line("L2", 6)));
        Assert.Equal("LINE:L1", SlotControllerKey.For(Line("l1")));
        Assert.NotEqual(SlotControllerKey.For(Line("L1")), SlotControllerKey.For(Line("L2")));
    }

    [Fact]
    public async Task R2_Induct_LinesOnOneController_ShareACounter_OtherControllersAreIndependent()
    {
        _lines.AddCommissioned(Line("L1", plcNumber: 1));
        _lines.AddCommissioned(Line("L2", plcNumber: 1));
        _lines.AddCommissioned(Line("L3", plcNumber: 2));
        foreach (var (line, tuId) in new[] { ("L1", "A"), ("L2", "B"), ("L3", "C") })
        {
            await Advice().AdviseAsync(line, tuId, Labels(("Shipping", "S-" + tuId)));
        }

        var induct = Induct(slots: new InMemorySlotIndexProvider());
        var a = await induct.InductAsync("L1", "A");
        var b = await induct.InductAsync("L2", "B");
        var c = await induct.InductAsync("L3", "C");

        Assert.Equal(1, a.Slot);
        Assert.Equal(2, b.Slot);
        Assert.Equal(1, c.Slot);
    }

    // ---- R4 bypass ----------------------------------------------------------------------------------

    [Fact]
    public async Task R4_BypassCarton_ToteIdRead_PassesVerify_NoRejectNoStreak()
    {
        _lines.AddCommissioned(Line("L1"));
        var order = await Advice().AdviseAsync("L1", "C123", Labels(("Shipping", "S1")));
        order.SetAdviceMetadata(bypass: true);
        await _store.SaveAsync(order);
        Assert.Equal(InductStatus.Bypass, (await Induct().InductAsync("L1", "C123")).Status);
        var rejects = new InMemoryRejectHistoryRepository();
        var verify = TestServices.Verify(_store, TestServices.Threshold(), _clock, rejects: rejects);

        var result = await verify.VerifyAsync(
            "C123", [new ScannedLabel("BlindLabel", "C123")], new VerifyOptions(Bypass: true), failThreshold: 1);

        Assert.Equal(VerifyStationStatus.Verified, result.Status);
        Assert.Equal(VerifyOutcome.Pass, result.Verify!.Outcome);
        Assert.Equal("Pass", result.Routing!.Value);
        Assert.Equal(0, result.ConsecutiveFailures);
        Assert.False(result.PrinterPaused);
        Assert.Empty(rejects.Records);
        Assert.Empty(_gateway.Jobs);
    }

    [Fact]
    public async Task R4_BypassCarton_NoRead_StillFails()
    {
        _lines.AddCommissioned(Line("L1"));
        var order = await Advice().AdviseAsync("L1", "C123", Labels(("Shipping", "S1")));
        order.SetAdviceMetadata(bypass: true);
        await _store.SaveAsync(order);
        var rejects = new InMemoryRejectHistoryRepository();
        var verify = TestServices.Verify(_store, TestServices.Threshold(), _clock, rejects: rejects);

        var result = await verify.VerifyAsync(
            "C123", [new ScannedLabel("BlindLabel", "?")], new VerifyOptions(Bypass: true), failThreshold: 3);

        Assert.NotEqual(VerifyStationStatus.Verified, result.Status);
        Assert.Equal(VerifyOutcome.NoRead, result.Verify!.Outcome);
        Assert.Equal("Fail", result.Routing!.Value);
        Assert.Equal(VerifyReasonCode.BlindLabelNotDetected, Assert.Single(rejects.Records).Code);
    }

    // ---- R7 verify-fail re-arm --------------------------------------------------------------------

    private async Task<VerifyStationResult> FailVerifyAsync(ISettingsProvider settings) =>
        await TestServices.Verify(_store, TestServices.Threshold(), _clock, settings: settings).VerifyAsync(
            "BL1", [new ScannedLabel("BlindLabel", "BL1"), new ScannedLabel("Shipping", "WRONG")],
            new VerifyOptions(), failThreshold: 1000);

    [Fact]
    public async Task R7_ReprintOn_VerifyFail_ReArms_AndPrintsAgain_WithoutACap()
    {
        _lines.AddCommissioned(Line("L1"));
        var settings = await Settings(KnownSettings.ReprintLabels, true);
        await Advice(settings).AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));
        var induct = Induct(settings);

        for (var attempt = 1; attempt <= 6; attempt++)
        {
            Assert.Equal(InductStatus.Printed, (await induct.InductAsync("L1", "BL1")).Status);
            Assert.Equal(attempt, (await _store.FindActiveByTuIdAsync("BL1"))!.PrintCount);

            var verify = await FailVerifyAsync(settings);

            Assert.Equal(VerifyStationStatus.ReArmed, verify.Status);
            Assert.Equal("Fail", verify.Routing!.Value);
            var order = (await _store.FindActiveByTuIdAsync("BL1"))!;
            Assert.Equal(TransportOrderStatus.Advised, order.Status);
            Assert.True(order.VerifyFailRearmed);
            Assert.True(order.CanPrint);
        }

        Assert.Equal(6, _gateway.Jobs.Count);
    }

    [Fact]
    public async Task R7_ReArmedCarton_SettingTurnedOffBeforeInduct_DoesNotPrint()
    {
        // Owner follow-up 2026-10-21: Reprint Labels is evaluated at induct time, not at the verify fail.
        _lines.AddCommissioned(Line("L1"));
        var settings = await Settings(KnownSettings.ReprintLabels, true);
        await Advice(settings).AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));
        var induct = Induct(settings);
        await induct.InductAsync("L1", "BL1");
        Assert.Equal(VerifyStationStatus.ReArmed, (await FailVerifyAsync(settings)).Status);

        await settings.SetAsync(KnownSettings.ReprintLabels, false);
        var blocked = await induct.InductAsync("L1", "BL1");

        Assert.Equal(InductStatus.NoReprint, blocked.Status);
        Assert.Single(_gateway.Jobs);
        Assert.Equal(1, (await _store.FindActiveByTuIdAsync("BL1"))!.PrintCount);

        await settings.SetAsync(KnownSettings.ReprintLabels, true);
        var reprint = await induct.InductAsync("L1", "BL1");

        Assert.Equal(InductStatus.Printed, reprint.Status);
        Assert.Equal(2, _gateway.Jobs.Count);
        var order = (await _store.FindActiveByTuIdAsync("BL1"))!;
        Assert.Equal(2, order.PrintCount);
        Assert.False(order.VerifyFailRearmed);
    }

    [Fact]
    public async Task R7_ReArmedCarton_SettingOffAtInduct_OperatorAuthorizationStillPrints()
    {
        _lines.AddCommissioned(Line("L1"));
        var settings = await Settings(KnownSettings.ReprintLabels, true);
        await Advice(settings).AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));
        await Induct(settings).InductAsync("L1", "BL1");
        await FailVerifyAsync(settings);
        await settings.SetAsync(KnownSettings.ReprintLabels, false);

        var order = (await _store.FindActiveByTuIdAsync("BL1"))!;
        order.AuthorizeReprint("operator");
        await _store.SaveAsync(order);

        Assert.Equal(InductStatus.Printed, (await Induct(settings).InductAsync("L1", "BL1")).Status);
    }
    [Fact]
    public async Task R7_ReprintOff_VerifyFail_DoesNotReArm()
    {
        _lines.AddCommissioned(Line("L1"));
        var off = await Settings(KnownSettings.ReprintLabels, false);
        await Advice(off).AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));
        await Induct(off).InductAsync("L1", "BL1");

        var verify = await FailVerifyAsync(off);

        Assert.Equal(VerifyStationStatus.HeldForIntervention, verify.Status);
        Assert.False((await _store.FindActiveByTuIdAsync("BL1"))!.VerifyFailRearmed);
        Assert.Equal(InductStatus.NoReprint, (await Induct(off).InductAsync("L1", "BL1")).Status);
        Assert.Single(_gateway.Jobs);
    }

    // ---- R8 new advice overwrites -----------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task R8_NewAdvice_AlwaysOverwritesLabelData_RegardlessOfOverwriteSetting(bool overwriteSetting)
    {
        _lines.AddCommissioned(Line("L1"));
        var settings = await Settings(KnownSettings.OverwriteLabelData, overwriteSetting);
        await Advice(settings).AdviseAsync("L1", "BL1", Labels(("Shipping", "FIRST")));
        await Induct(settings).InductAsync("L1", "BL1");

        await Advice(settings).AdviseAsync("L1", "BL1", Labels(("Shipping", "SECOND")));

        var label = Assert.Single((await _store.FindActiveByTuIdAsync("BL1"))!.Labels.Labels);
        Assert.Equal("SECOND", label.Lpn);
        await Induct(settings).InductAsync("L1", "BL1");
        Assert.Contains("SECOND", _gateway.Jobs[^1].Zpl, StringComparison.Ordinal);
    }

    // ---- R9 no profile → no print -----------------------------------------------------------------

    [Fact]
    public async Task R9_LineWithoutProfile_PrintsNothing_AndLogsWarning()
    {
        _lines.Add(Line("L1"));
        await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));
        var logger = new ListLogger<InductService>();

        await Induct(logger: logger).InductAsync("L1", "BL1");

        Assert.Empty(_gateway.Jobs);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Has("TuId", "BL1"));
    }

    // ---- R11 xref cleanup -------------------------------------------------------------------------

    [Fact]
    public async Task R11_Disassociate_DeletesTheRow_AndLogsInformation()
    {
        var xref = new InMemoryXRefStore();
        var logger = new ListLogger<XRefService>();
        var service = new XRefService(xref, logger);
        await service.AssociateAsync("BL1", "OLPN-1", BarcodeType.oLPN);

        var removed = await service.DisassociateAsync("OLPN-1", BarcodeType.oLPN);

        Assert.Equal(1, removed);
        Assert.Empty(xref.All);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Has("Barcode", "OLPN-1"));
    }

    [Fact]
    public async Task R11_PurgeCarton_DeletesOrderAndItsXrefRows_AndLogsInformation()
    {
        var xref = new InMemoryXRefStore();
        await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));
        await Advice().AdviseAsync("L1", "BL2", Labels(("Shipping", "S2")));
        await xref.AddAsync("BL1", new XRef("OLPN-1", BarcodeType.oLPN));
        await xref.AddAsync("BL1", new XRef("UPC-1", BarcodeType.UPC));
        await xref.AddAsync("BL2", new XRef("OLPN-2", BarcodeType.oLPN));
        var logger = new ListLogger<CartonPurgeService>();

        var result = await new CartonPurgeService(_store, xref, TestServices.Locks(), logger).PurgeCartonAsync("BL1");

        Assert.Equal(new CartonPurgeResult(1, 2), result);
        Assert.Null(await _store.FindActiveByTuIdAsync("BL1"));
        Assert.Empty(await xref.FindTuIdsByBarcodeAsync("OLPN-1"));
        Assert.Equal("BL2", Assert.Single(xref.All).TuId);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Has("TuId", "BL1"));
    }

    [Fact]
    public async Task R11_PurgeWave_DeletesOnlyThatWavesCartonsAndXrefs()
    {
        var xref = new InMemoryXRefStore();
        foreach (var (tuId, wave) in new[] { ("BL1", "W1"), ("BL2", "W1"), ("BL3", "W2") })
        {
            var order = await Advice().AdviseAsync("L1", tuId, Labels(("Shipping", "S-" + tuId)));
            order.SetAdviceMetadata(waveId: wave);
            await _store.SaveAsync(order);
            await xref.AddAsync(tuId, new XRef("OLPN-" + tuId, BarcodeType.oLPN));
        }

        var logger = new ListLogger<CartonPurgeService>();

        var result = await new CartonPurgeService(_store, xref, TestServices.Locks(), logger).PurgeWaveAsync("W1");

        Assert.Equal(new CartonPurgeResult(2, 2), result);
        Assert.Equal("BL3", Assert.Single(_store.All).TuId);
        Assert.Equal("BL3", Assert.Single(xref.All).TuId);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Has("WaveId", "W1"));
    }

    // ---- R12 dynamic apply-point logging ----------------------------------------------------------

    private static FirePointProfile SideProfile(string printerId, string applyPoint) =>
        new("Generic", [((printerId, "Shipping"), new FirePoint(2, 800, 3, ApplyPoint.Parse(applyPoint)))]);

    [Fact]
    public async Task R12_CalculatedApplyPoint_IsLoggedAtDebug_WithCartonAndPrinter()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"])], activeProfile: SideProfile("Ship1", "0T")));
        await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));
        var logger = new ListLogger<InductService>();

        await Induct(logger: logger).InductAsync(new InductScan("L1", "BL1", Length: 100, Height: 12));

        Assert.Equal(80, Assert.Single(_gateway.Jobs).ApplyPulse);
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Debug && e.Has("TuId", "BL1") && e.Has("PrinterId", "Ship1") && e.Has("ApplyPulse", 80));
    }

    [Fact]
    public async Task R12_InvalidApplyInputs_AreLoggedAtWarning_AndThePrintStillGoesOut()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"])],
            activeProfile: SideProfile("Ship1", "0T"), encoderResolution: 0m));
        await Advice().AdviseAsync("L1", "BL1", Labels(("Shipping", "S1")));
        var logger = new ListLogger<InductService>();

        await Induct(logger: logger).InductAsync(new InductScan("L1", "BL1", Length: 100, Height: 12));

        Assert.Null(Assert.Single(_gateway.Jobs).ApplyPulse);
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Has("TuId", "BL1") && e.Has("PrinterId", "Ship1"));
    }

    [Theory]
    [InlineData(0.5, 3)]
    [InlineData(0.1, 1)]
    public void R12_RoundingStaysAwayFromZero(decimal inches, int expected)
    {
        // Owner kept Math.Round(AwayFromZero): 0.5" / 0.2 = 2.5 pulses → 3 and 0.5 pulse → 1 (banker's would give 2 / 0).
        var pulse = new ApplyPointResolver().Resolve(
            new ApplyPoint(inches, Edge.Leading), ApplyOrientation.Side, new CartonDimensions(100),
            new ApplyPointConfig(EncoderResolution: 0.2m));

        Assert.Equal(expected, pulse);
    }
}
/// <summary>Minimal capturing logger: records level, rendered message and structured state.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        var values = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
        lock (Entries)
        {
            Entries.Add(new Entry(logLevel, formatter(state, exception), values));
        }
    }

    internal sealed record Entry(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> Values)
    {
        public bool Has(string key, object expected) =>
            Values.Any(v => string.Equals(v.Key, key, StringComparison.Ordinal) && Equals(v.Value?.ToString(), expected.ToString()));
    }
}
