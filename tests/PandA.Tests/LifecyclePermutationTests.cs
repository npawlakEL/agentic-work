using PandA.Core;
using PandA.Core.Verification;
using PandA.Sim;
using Xunit;

namespace PandA.Tests;

/// <summary>
/// End-to-end permutation driver tests (section D "LifecyclePermutationTests" of architecture-log 008):
/// compose advise (MP1) → induct (MP2) → verify station (MP286) through the real Core services over the
/// in-memory Sim, asserting the final carton status, print counter, and pause signal for each combination.
/// Also pins the station-level threshold semantics that require a non-zero starting streak.
/// </summary>
public sealed class LifecyclePermutationTests
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

    public LifecyclePermutationTests()
    {
        _advice = new CartonAdviceService(_store, _clock, new InMemorySettingsProvider());
        _induct = new InductService(_store, _lines, new PrinterSelectionService(), _gateway, _clock, new InMemorySettingsProvider());
        _verify = new VerifyStationService(_store, new VerificationService(), _threshold, _clock);
    }

    private static PrinterConfig P(string id, string[] map, int order) =>
        new(id, "10.0.0." + order, 9100, map, ApplyOrientation.Side, order);

    private static PandaLabelSet Labels(params (string Type, string Lpn)[] labels) =>
        new(labels.Select(l => new Label(l.Type, l.Lpn, $"^XA{l.Type}^XZ")));

    private static ScannedLabel[] Scan(params (string Type, string Value)[] labels) =>
        [.. labels.Select(l => new ScannedLabel(l.Type, l.Value))];

    // ---- Full advise→induct→verify permutations -------------------------------------------------

    public static IEnumerable<object[]> VerifyOutcomeCases()
    {
        // because, scannedValue, expectedStation
        yield return ["correct scan verifies", "SHIP1", VerifyStationStatus.Verified];
        yield return ["wrong scan holds", "WRONG", VerifyStationStatus.HeldForIntervention];
        yield return ["no-read holds", "SH?P", VerifyStationStatus.HeldForIntervention];
        yield return ["no-data holds", "0", VerifyStationStatus.HeldForIntervention];
        yield return ["conflict holds", "SH#P", VerifyStationStatus.HeldForIntervention];
    }

    [Theory]
    [MemberData(nameof(VerifyOutcomeCases))]
    public async Task Advise_Induct_Verify_EndsInExpectedStatus(
        string because, string scanned, VerifyStationStatus expected)
    {
        _lines.Add(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels(("Shipping", "SHIP1")));
        var induct = await _induct.InductAsync("L1", "BLIND1");
        Assert.Equal(InductStatus.Printed, induct.Status);

        var verify = await _verify.VerifyAsync(
            "BLIND1", Scan(("Shipping", scanned)), new VerifyOptions(), failThreshold: 5);

        Assert.True(expected == verify.Status, $"{because}: expected {expected} but was {verify.Status}");

        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        var expectedOrder = expected == VerifyStationStatus.Verified
            ? TransportOrderStatus.Verified
            : TransportOrderStatus.HeldForIntervention;
        Assert.Equal(expectedOrder, stored!.Status);
        Assert.Equal(1, stored.PrintCount); // one full run regardless of verify outcome
    }

    [Fact]
    public async Task FullRecoveryLoop_Fail_Authorize_Reprint_Pass()
    {
        _lines.Add(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels(("Shipping", "SHIP1")));
        await _induct.InductAsync("L1", "BLIND1");

        // Fail → held.
        var failed = await _verify.VerifyAsync(
            "BLIND1", Scan(("Shipping", "WRONG")), new VerifyOptions(), failThreshold: 5);
        Assert.Equal(VerifyStationStatus.HeldForIntervention, failed.Status);

        // Operator authorizes → reprint (count 1→2).
        var held = await _store.FindActiveByTuIdAsync("BLIND1");
        held!.AuthorizeReprint("operator relabel");
        await _store.UpsertAsync(held);
        var reprint = await _induct.InductAsync("L1", "BLIND1");
        Assert.Equal(InductStatus.Printed, reprint.Status);

        // Re-verify correctly → verified, streak cleared.
        var passed = await _verify.VerifyAsync(
            "BLIND1", Scan(("Shipping", "SHIP1")), new VerifyOptions(), failThreshold: 5);
        Assert.Equal(VerifyStationStatus.Verified, passed.Status);
        Assert.Equal(0, passed.ConsecutiveFailures);

        var final = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(2, final!.PrintCount);
        Assert.Equal(TransportOrderStatus.Verified, final.Status);
    }

    [Fact]
    public async Task DuplicateAdvice_BeforePrint_StartsNewGeneration()
    {
        _lines.Add(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels(("Shipping", "OLD")));
        await _induct.InductAsync("L1", "BLIND1");

        // Re-advise the same blind with new data → generation reset → printable again from scratch.
        await _advice.AdviseAsync("L1", "BLIND1", Labels(("Shipping", "NEW")));
        var order = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(0, order!.PrintCount);
        Assert.True(order.CanPrint);

        var reinduct = await _induct.InductAsync("L1", "BLIND1");
        Assert.Equal(InductStatus.Printed, reinduct.Status);

        var verify = await _verify.VerifyAsync(
            "BLIND1", Scan(("Shipping", "NEW")), new VerifyOptions(), failThreshold: 5);
        Assert.Equal(VerifyStationStatus.Verified, verify.Status);
    }

    // ---- Station-level threshold semantics (need non-zero starting streak) ----------------------

    [Fact]
    public async Task Station_ConsecutiveFailsAcrossCartons_PauseAtThreshold_ThenPassRecovers()
    {
        _lines.Add(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));

        async Task<VerifyStationResult> RunCarton(string blind, string scan)
        {
            await _advice.AdviseAsync("L1", blind, Labels(("Shipping", "SHIP")));
            await _induct.InductAsync("L1", blind);
            return await _verify.VerifyAsync(
                blind, Scan(("Shipping", scan)), new VerifyOptions(), failThreshold: 2);
        }

        var first = await RunCarton("B1", "WRONG");
        Assert.False(first.PrinterPaused);

        var second = await RunCarton("B2", "WRONG"); // trips threshold 2
        Assert.True(second.PrinterPaused);
        Assert.Equal(2, second.ConsecutiveFailures);

        var third = await RunCarton("B3", "SHIP"); // pass resets streak
        Assert.Equal(VerifyStationStatus.Verified, third.Status);
        Assert.Equal(0, third.ConsecutiveFailures);
    }

    [Fact]
    public async Task Station_NoActiveOrder_DoesNotResetExistingStreak()
    {
        _lines.Add(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "B1", Labels(("Shipping", "SHIP")));
        await _induct.InductAsync("L1", "B1");
        var failed = await _verify.VerifyAsync(
            "B1", Scan(("Shipping", "WRONG")), new VerifyOptions(), failThreshold: 5);
        Assert.Equal(1, failed.ConsecutiveFailures);

        // A scan for an unknown blind returns early without touching the tracker.
        var unknown = await _verify.VerifyAsync(
            "NOPE", Scan(("Shipping", "SHIP")), new VerifyOptions(), failThreshold: 5);
        Assert.Equal(VerifyStationStatus.NoActiveOrder, unknown.Status);
        Assert.Equal(0, unknown.ConsecutiveFailures); // result reports 0, but...

        Assert.Equal(1, _threshold.CurrentCount("L1")); // ...the real streak is untouched
    }

    [Fact]
    public async Task Station_Bypass_Verifies_WithoutFailingThreshold()
    {
        _lines.Add(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "B1", Labels(("Shipping", "SHIP")));
        await _induct.InductAsync("L1", "B1");

        var verify = await _verify.VerifyAsync(
            "B1", Scan(("Shipping", "ANYTHING")),
            new VerifyOptions(VerifyEnabled: false, Bypass: true), failThreshold: 2);

        Assert.Equal(VerifyStationStatus.Verified, verify.Status);
        Assert.Equal(VerifyOutcome.Ignore, verify.Verify!.Outcome);
        Assert.Equal(0, verify.ConsecutiveFailures);
    }

    [Fact]
    public async Task Station_Bypass_NoRead_HoldsAndCountsTowardThreshold()
    {
        _lines.Add(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "B1", Labels(("Shipping", "SHIP")));
        await _induct.InductAsync("L1", "B1");

        var verify = await _verify.VerifyAsync(
            "B1", Scan(("Shipping", "?")),
            new VerifyOptions(VerifyEnabled: false, Bypass: true), failThreshold: 2);

        Assert.Equal(VerifyStationStatus.HeldForIntervention, verify.Status);
        Assert.Equal(VerifyOutcome.NoRead, verify.Verify!.Outcome);
        Assert.Equal(1, verify.ConsecutiveFailures);
    }
}

