using PandA.Core;
using PandA.Core.Verification;
using PandA.Sim;

namespace PandA.Tests;

/// <summary>
/// F08 (decision-008) — PandA annotates the carton with a verify routing criterion for econtroller
/// CriteriaBasedSorting; it does NOT select lanes/places itself.
/// </summary>
public sealed class RoutingCriterionTests
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

    public RoutingCriterionTests()
    {
        _advice = new CartonAdviceService(_store, _clock, new InMemorySettingsProvider());
        _induct = new InductService(_store, _lines, new PrinterSelectionService(), _gateway, _clock, new InMemorySettingsProvider());
        _verify = new VerifyStationService(_store, new VerificationService(), _threshold, _clock);
    }

    private static PrinterConfig Printer(string id, string[] map, int order) =>
        new(id, "10.0.0." + order, 9100, map, ApplyOrientation.Side, order);

    private static PandaLabelSet Labels(params (string Type, string Lpn)[] labels) =>
        new(labels.Select(l => new Label(l.Type, l.Lpn, $"^XA{l.Type}^XZ")));

    // The carton's own blind label is a required verify slot (source TOOL_PA_VerifyLabel; owner ruling 2026-10-21).
    private static ScannedLabel[] WithBlind(string blind, params (string Type, string Value)[] labels) =>
        [new ScannedLabel("BlindLabel", blind), .. Scan(labels)];

    private static IReadOnlyList<ScannedLabel> Scan(params (string Type, string Value)[] reads) =>
        [.. reads.Select(r => new ScannedLabel(r.Type, r.Value))];

    [Fact]
    public void ForVerify_Pass_MapsToPandaVerifyPass()
    {
        var criterion = RoutingCriterion.ForVerify(new VerifyResult(VerifyOutcome.Pass, []));

        Assert.Equal("PandaVerify", criterion.Type);
        Assert.Equal("Pass", criterion.Value);
    }

    [Fact]
    public void ForVerify_Ignore_RoutesToFailDestination_WithPassThroughCode()
    {
        // Source TOOL_PA_VerifyLabel L470: FinalDestination = pass(1) ? PassDest : FailDest, so a bypass (3)
        // goes to the fail destination (owner ruling 2026-10-21).
        var criterion = RoutingCriterion.ForVerify(new VerifyResult(VerifyOutcome.Ignore, []));

        Assert.Equal("Fail", criterion.Value);
        Assert.Equal(VerifyReasonCode.PassThroughCarton, criterion.ReasonCode);
        Assert.Equal("Verify - FAIL", criterion.Destination);
    }

    [Theory]
    [InlineData(VerifyOutcome.Fail)]
    [InlineData(VerifyOutcome.NoRead)]
    [InlineData(VerifyOutcome.NoData)]
    [InlineData(VerifyOutcome.Conflict)]
    public void ForVerify_FailingOutcomes_MapToFail_WithReasonCode(VerifyOutcome outcome)
    {
        // decision-008 "Pass|Fail|<RejectReasonCode>": value is Pass/Fail; the reason code rides alongside.
        var criterion = RoutingCriterion.ForVerify(new VerifyResult(outcome, []), "Ship Lane", "Reject Lane");

        Assert.Equal("PandaVerify", criterion.Type);
        Assert.Equal("Fail", criterion.Value);
        Assert.Equal(VerifyReasonCode.PandaLogicError, criterion.ReasonCode); // no details → code 0
        Assert.Equal("Reject Lane", criterion.Destination);
    }

    [Fact]
    public async Task VerifyStation_Pass_PopulatesPassRoutingCriterion()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels(("Shipping", "SHIP1")));
        await _induct.InductAsync("L1", "BLIND1");

        var result = await _verify.VerifyAsync("BLIND1", WithBlind("BLIND1", ("Shipping", "SHIP1")), new VerifyOptions(), failThreshold: 3);

        Assert.Equal(VerifyStationStatus.Verified, result.Status);
        Assert.NotNull(result.Routing);
        Assert.Equal("PandaVerify", result.Routing!.Type);
        Assert.Equal("Pass", result.Routing.Value);
    }

    [Fact]
    public async Task VerifyStation_Fail_PopulatesFailRoutingCriterion()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels(("Shipping", "SHIP1")));
        await _induct.InductAsync("L1", "BLIND1");

        var result = await _verify.VerifyAsync("BLIND1", WithBlind("BLIND1", ("Shipping", "WRONG")), new VerifyOptions(), failThreshold: 3);

        Assert.Equal(VerifyStationStatus.HeldForIntervention, result.Status);
        Assert.NotNull(result.Routing);
        Assert.Equal("Fail", result.Routing!.Value);
    }

    [Fact]
    public async Task VerifyStation_NoActiveOrder_RoutesFail_WithPandaLogicErrorCode()
    {
        // Source VerifyLabel L134: PandaDataID=0 → VerifyPass 0 → fail destination (owner ruling 2026-10-21).
        var result = await _verify.VerifyAsync("NOPE", WithBlind("NOPE", ("Shipping", "SHIP1")), new VerifyOptions(), failThreshold: 3);

        Assert.Equal(VerifyStationStatus.NoActiveOrder, result.Status);
        Assert.NotNull(result.Routing);
        Assert.Equal("Fail", result.Routing!.Value);
        Assert.Equal(VerifyReasonCode.PandaLogicError, result.Routing.ReasonCode);
    }
}
