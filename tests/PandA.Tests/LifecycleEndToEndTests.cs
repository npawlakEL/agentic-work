using PandA.Core;
using PandA.Core.Verification;
using PandA.Sim;
using Xunit;

namespace PandA.Tests;

/// <summary>
/// Full-lifecycle tests: advise (MP1) → induct/print (MP2) → verify (MP286), composed through the
/// in-memory Sim with the real Core services. Exercises the whole slice end-to-end.
/// </summary>
public sealed class LifecycleEndToEndTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTransportOrderStore _store = new();
    private readonly InMemoryLineProvider _lines = new();
    private readonly CapturingPrinterGateway _gateway = new();
    private readonly VerifyThresholdTracker _threshold = new();
    private readonly TestClock _clock = new(T0);

    private readonly CartonAdviceService _advice;
    private readonly InductService _induct;
    private readonly VerifyStationService _verify;

    public LifecycleEndToEndTests()
    {
        _advice = new CartonAdviceService(_store, _clock);
        _induct = new InductService(_store, _lines, new PrinterSelectionService(), _gateway, _clock);
        _verify = new VerifyStationService(_store, new VerificationService(), _threshold, _clock);
    }

    private static PrinterConfig Printer(string id, string[] map, int order) =>
        new(id, "10.0.0." + order, 9100, map, ApplyOrientation.Side, order);

    private static PandaLabelSet Labels(params (string Type, string Lpn)[] labels) =>
        new(labels.Select(l => new Label(l.Type, l.Lpn, $"^XA{l.Type}^XZ")));

    private static ScannedLabel[] Scan(params (string Type, string Value)[] labels) =>
        [.. labels.Select(l => new ScannedLabel(l.Type, l.Value))];

    [Fact]
    public async Task HappyPath_Advise_Induct_Verify_Passes_AndCompletesCarton()
    {
        _lines.Add(new LineConfig("L1",
        [
            Printer("Ship1", ["Shipping"], 0),
            Printer("Cont1", ["Content"], 1),
        ]));

        await _advice.AdviseAsync("L1", "BLIND1", Labels(("Shipping", "SHIP1"), ("Content", "CON1")));

        var induct = await _induct.InductAsync("L1", "BLIND1");
        Assert.Equal(InductStatus.Printed, induct.Status);
        Assert.Equal(2, _gateway.Jobs.Count);

        var verify = await _verify.VerifyAsync(
            "BLIND1",
            Scan(("Shipping", "SHIP1"), ("Content", "CON1")),
            new VerifyOptions(),
            failThreshold: 3);

        Assert.Equal(VerifyStationStatus.Verified, verify.Status);
        Assert.False(verify.PrinterPaused);

        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(TransportOrderStatus.Verified, stored!.Status);
        Assert.Equal(T0, stored.VerifiedAt);
    }

    [Fact]
    public async Task Mismatch_ReArmsCarton_ThenReprintAndReverifyPasses()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels(("Shipping", "SHIP1")));
        await _induct.InductAsync("L1", "BLIND1");

        // Operator applied the wrong label → verify fails and the carton is re-armed.
        var firstVerify = await _verify.VerifyAsync(
            "BLIND1", Scan(("Shipping", "WRONG")), new VerifyOptions(), failThreshold: 3);

        Assert.Equal(VerifyStationStatus.ReArmed, firstVerify.Status);
        var reArmed = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(TransportOrderStatus.Advised, reArmed!.Status);
        Assert.Null(reArmed.PrintedAt);

        // Re-armed carton can be re-inducted (reprint) and re-verified correctly.
        var reprint = await _induct.InductAsync("L1", "BLIND1");
        Assert.Equal(InductStatus.Printed, reprint.Status);

        var secondVerify = await _verify.VerifyAsync(
            "BLIND1", Scan(("Shipping", "SHIP1")), new VerifyOptions(), failThreshold: 3);

        Assert.Equal(VerifyStationStatus.Verified, secondVerify.Status);
        Assert.Equal(0, secondVerify.ConsecutiveFailures); // pass cleared the streak
    }

    [Fact]
    public async Task ConsecutiveFailures_AcrossCartons_PauseThePrinterAtThreshold()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));

        var paused = false;
        for (var i = 1; i <= 2; i++)
        {
            var blind = $"BLIND{i}";
            await _advice.AdviseAsync("L1", blind, Labels(("Shipping", $"SHIP{i}")));
            await _induct.InductAsync("L1", blind);

            var verify = await _verify.VerifyAsync(
                blind, Scan(("Shipping", "WRONG")), new VerifyOptions(), failThreshold: 2);
            paused = verify.PrinterPaused;
        }

        Assert.True(paused); // second consecutive line failure trips the threshold
    }

    [Fact]
    public async Task Bypass_WhenVerifyDisabled_CompletesWithoutFailingThreshold()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels(("Shipping", "SHIP1")));
        await _induct.InductAsync("L1", "BLIND1");

        var verify = await _verify.VerifyAsync(
            "BLIND1",
            Scan(("Shipping", "ANYTHING")),
            new VerifyOptions(VerifyEnabled: false, Bypass: true),
            failThreshold: 2);

        Assert.Equal(VerifyStationStatus.Verified, verify.Status);
        Assert.Equal(VerifyOutcome.Ignore, verify.Verify!.Outcome);
        Assert.Equal(0, verify.ConsecutiveFailures);
    }

    [Fact]
    public async Task Verify_UnknownBlindLabel_ReturnsNoActiveOrder()
    {
        var verify = await _verify.VerifyAsync(
            "NOPE", Scan(("Shipping", "SHIP1")), new VerifyOptions(), failThreshold: 3);

        Assert.Equal(VerifyStationStatus.NoActiveOrder, verify.Status);
        Assert.Null(verify.Verify);
    }

    [Fact]
    public async Task MultiTypeCarton_LoadBalances_PrintsAllThenVerifiesAll()
    {
        _lines.Add(new LineConfig("L1",
        [
            Printer("Ship1", ["Shipping"], 0),
            Printer("Cont1", ["Content"], 1),
            Printer("Par1", ["Parcel"], 2),
        ]));

        await _advice.AdviseAsync("L1", "BLIND1",
            Labels(("Shipping", "S1"), ("Content", "C1"), ("Parcel", "P1")));

        var induct = await _induct.InductAsync("L1", "BLIND1");
        Assert.Equal(InductStatus.Printed, induct.Status);
        Assert.Equal(3, _gateway.Jobs.Count);

        var verify = await _verify.VerifyAsync(
            "BLIND1",
            Scan(("Shipping", "S1"), ("Content", "C1"), ("Parcel", "P1")),
            new VerifyOptions(),
            failThreshold: 3);

        Assert.Equal(VerifyStationStatus.Verified, verify.Status);
    }
}
