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
    private readonly VerifyThresholdTracker _threshold = TestServices.Threshold();
    private readonly TestClock _clock = new(T0);
    private readonly CartonAdviceService _advice;
    private readonly InductService _induct;
    private readonly VerifyStationService _verify;

    public LifecyclePermutationTests()
    {
        _advice = TestServices.Advice(_store, _clock, new InMemorySettingsProvider());
        _induct = TestServices.Induct(_store, _lines, _gateway, _clock, new InMemorySettingsProvider());
        _verify = TestServices.Verify(_store, _threshold, _clock);
    }

    private static PrinterConfig P(string id, string[] map, int order) =>
        new(id, "10.0.0." + order, 9100, map, ApplyOrientation.Side, order);

    private static PandaLabelSet Labels(params (string Type, string Lpn)[] labels) =>
        new(labels.Select(l => new Label(l.Type, l.Lpn, $"^XA{l.Type}^XZ")));

    // The carton's own blind label is a required verify slot (source TOOL_PA_VerifyLabel; owner ruling 2026-10-21).
    private static ScannedLabel[] WithBlind(string blind, params (string Type, string Value)[] labels) =>
        [new ScannedLabel("BlindLabel", blind), .. Scan(labels)];

    private static ScannedLabel[] Scan(params (string Type, string Value)[] labels) =>
        [.. labels.Select(l => new ScannedLabel(l.Type, l.Value))];

    // ---- Full advise→induct→verify permutations -------------------------------------------------

    public static IEnumerable<object[]> VerifyOutcomeCases()
    {
        // because, scannedValue, expectedStation
        yield return ["correct scan verifies", "SHIP1", VerifyStationStatus.Verified];
        // 'Reprint Labels' defaults on → every verify fail re-arms the carton (owner ruling 2026-10-21, R7).
        yield return ["wrong scan re-arms", "WRONG", VerifyStationStatus.ReArmed];
        yield return ["no-read re-arms", "SH?P", VerifyStationStatus.ReArmed];
        yield return ["no-data re-arms", "0", VerifyStationStatus.ReArmed];
        yield return ["conflict re-arms", "SH#P", VerifyStationStatus.ReArmed];
    }

    [Theory]
    [MemberData(nameof(VerifyOutcomeCases))]
    public async Task Advise_Induct_Verify_EndsInExpectedStatus(
        string because, string scanned, VerifyStationStatus expected)
    {
        _lines.AddCommissioned(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels(("Shipping", "SHIP1")));
        var induct = await _induct.InductAsync("L1", "BLIND1");
        Assert.Equal(InductStatus.Printed, induct.Status);

        var verify = await _verify.VerifyAsync(
            "BLIND1", WithBlind("BLIND1", ("Shipping", scanned)), new VerifyOptions(), failThreshold: 5);

        Assert.True(expected == verify.Status, $"{because}: expected {expected} but was {verify.Status}");

        var stored = await _store.FindActiveByTuIdAsync("BLIND1");
        var expectedOrder = expected == VerifyStationStatus.Verified
            ? TransportOrderStatus.Verified
            : TransportOrderStatus.Advised;
        Assert.Equal(expectedOrder, stored!.Status);
        Assert.Equal(1, stored.PrintCount); // one full run regardless of verify outcome
    }

    [Fact]
    public async Task FullRecoveryLoop_Fail_Authorize_Reprint_Pass()
    {
        _lines.AddCommissioned(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels(("Shipping", "SHIP1")));
        await _induct.InductAsync("L1", "BLIND1");

        // Fail → held ('Reprint Labels' off at the verify station: no re-arm, R7).
        var verifyReprintOff = TestServices.Verify(_store, _threshold, _clock, settings: TestSettings.ReprintOff());
        var failed = await verifyReprintOff.VerifyAsync(
            "BLIND1", WithBlind("BLIND1", ("Shipping", "WRONG")), new VerifyOptions(), failThreshold: 5);
        Assert.Equal(VerifyStationStatus.HeldForIntervention, failed.Status);

        // Operator authorizes → reprint (count 1→2).
        var held = await _store.FindActiveByTuIdAsync("BLIND1");
        held!.AuthorizeReprint("operator relabel");
        await _store.SaveAsync(held);
        var reprint = await _induct.InductAsync("L1", "BLIND1");
        Assert.Equal(InductStatus.Printed, reprint.Status);

        // Re-verify correctly → verified, streak cleared.
        var passed = await _verify.VerifyAsync(
            "BLIND1", WithBlind("BLIND1", ("Shipping", "SHIP1")), new VerifyOptions(), failThreshold: 5);
        Assert.Equal(VerifyStationStatus.Verified, passed.Status);
        Assert.Equal(0, passed.ConsecutiveFailures);

        var final = await _store.FindActiveByTuIdAsync("BLIND1");
        Assert.Equal(2, final!.PrintCount);
        Assert.Equal(TransportOrderStatus.Verified, final.Status);
    }

    [Fact]
    public async Task DuplicateAdvice_BeforePrint_StartsNewGeneration()
    {
        _lines.AddCommissioned(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
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
            "BLIND1", WithBlind("BLIND1", ("Shipping", "NEW")), new VerifyOptions(), failThreshold: 5);
        Assert.Equal(VerifyStationStatus.Verified, verify.Status);
    }

    // ---- Station-level threshold semantics (need non-zero starting streak) ----------------------

    [Fact]
    public async Task Station_ConsecutiveFailsAcrossCartons_PauseAtThreshold_ThenPassRecovers()
    {
        _lines.AddCommissioned(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));

        async Task<VerifyStationResult> RunCarton(string blind, string scan)
        {
            await _advice.AdviseAsync("L1", blind, Labels(("Shipping", "SHIP")));
            await _induct.InductAsync("L1", blind);
            return await _verify.VerifyAsync(
                blind, WithBlind(blind, ("Shipping", scan)), new VerifyOptions(), failThreshold: 2);
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
        _lines.AddCommissioned(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "B1", Labels(("Shipping", "SHIP")));
        await _induct.InductAsync("L1", "B1");
        var failed = await _verify.VerifyAsync(
            "B1", WithBlind("B1", ("Shipping", "WRONG")), new VerifyOptions(), failThreshold: 5);
        Assert.Equal(1, failed.ConsecutiveFailures);

        // A scan for an unknown blind returns early without touching the tracker.
        var unknown = await _verify.VerifyAsync(
            "NOPE", WithBlind("NOPE", ("Shipping", "SHIP")), new VerifyOptions(), failThreshold: 5);
        Assert.Equal(VerifyStationStatus.NoActiveOrder, unknown.Status);
        Assert.Equal(0, unknown.ConsecutiveFailures); // result reports 0, but...

        Assert.Equal(1, (await _threshold.CurrentCountAsync("Ship1"))); // ...the real (per-printer) streak is untouched
    }

    [Fact]
    public async Task Station_Bypass_Verifies_WithoutFailingThreshold()
    {
        _lines.AddCommissioned(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "B1", Labels(("Shipping", "SHIP")));
        await _induct.InductAsync("L1", "B1");

        var verify = await _verify.VerifyAsync(
            "B1", WithBlind("B1", ("Shipping", "ANYTHING")),
            new VerifyOptions(VerifyEnabled: false, Bypass: true), failThreshold: 2);

        // Owner ruling 2026-10-21 (R4): the carton id was read → a bypass carton PASSES (was Ignore/code 3).
        Assert.Equal(VerifyStationStatus.Verified, verify.Status);
        Assert.Equal(VerifyOutcome.Pass, verify.Verify!.Outcome);
        Assert.Equal("Pass", verify.Routing!.Value);
        Assert.Equal(0, verify.ConsecutiveFailures);
    }

    [Fact]
    public async Task Station_Bypass_CartonIdRead_OtherLabelNoRead_StillPasses()
    {
        // Owner ruling 2026-10-21 (R4) — overrules the former VF-3 "bypass no-read holds": the carton id was read,
        // so the glitched read of a label that was never printed does not fail the bypass carton.
        _lines.AddCommissioned(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "B1", Labels(("Shipping", "SHIP")));
        await _induct.InductAsync("L1", "B1");

        var verify = await _verify.VerifyAsync(
            "B1", WithBlind("B1", ("Shipping", "?")),
            new VerifyOptions(VerifyEnabled: false, Bypass: true), failThreshold: 2);

        Assert.Equal(VerifyStationStatus.Verified, verify.Status);
        Assert.Equal(VerifyOutcome.Pass, verify.Verify!.Outcome);
        Assert.Equal(0, verify.ConsecutiveFailures);
    }

    [Fact]
    public async Task Station_Bypass_CartonIdNoRead_FailsAndCountsTowardThreshold()
    {
        _lines.AddCommissioned(new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "B1", Labels(("Shipping", "SHIP")));
        await _induct.InductAsync("L1", "B1");

        var verify = await _verify.VerifyAsync(
            "B1", [new ScannedLabel("BlindLabel", "?")],
            new VerifyOptions(VerifyEnabled: false, Bypass: true), failThreshold: 2);

        Assert.Equal(VerifyStationStatus.ReArmed, verify.Status);
        Assert.Equal(VerifyOutcome.NoRead, verify.Verify!.Outcome);
        Assert.Equal(VerifyReasonCode.BlindLabelNotDetected, verify.Routing!.ReasonCode);
        Assert.Equal(1, verify.ConsecutiveFailures);
    }
}
