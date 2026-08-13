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
    public void ForVerify_Ignore_AlsoMapsToPass()
    {
        Assert.Equal("Pass", RoutingCriterion.ForVerify(new VerifyResult(VerifyOutcome.Ignore, [])).Value);
    }

    [Theory]
    [InlineData(VerifyOutcome.Fail, "Fail")]
    [InlineData(VerifyOutcome.NoRead, "NoRead")]
    [InlineData(VerifyOutcome.NoData, "NoData")]
    [InlineData(VerifyOutcome.Conflict, "Conflict")]
    public void ForVerify_FailingOutcomes_MapToOutcomeValue(VerifyOutcome outcome, string expected)
    {
        var criterion = RoutingCriterion.ForVerify(new VerifyResult(outcome, []));

        Assert.Equal("PandaVerify", criterion.Type);
        Assert.Equal(expected, criterion.Value);
    }

    [Fact]
    public async Task VerifyStation_Pass_PopulatesPassRoutingCriterion()
    {
        _lines.Add(new LineConfig("L1", [Printer("Ship1", ["Shipping"], 0)]));
        await _advice.AdviseAsync("L1", "BLIND1", Labels(("Shipping", "SHIP1")));
        await _induct.InductAsync("L1", "BLIND1");

        var result = await _verify.VerifyAsync("BLIND1", Scan(("Shipping", "SHIP1")), new VerifyOptions(), failThreshold: 3);

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

        var result = await _verify.VerifyAsync("BLIND1", Scan(("Shipping", "WRONG")), new VerifyOptions(), failThreshold: 3);

        Assert.Equal(VerifyStationStatus.HeldForIntervention, result.Status);
        Assert.NotNull(result.Routing);
        Assert.Equal("Fail", result.Routing!.Value);
    }

    [Fact]
    public async Task VerifyStation_NoActiveOrder_LeavesRoutingNull()
    {
        var result = await _verify.VerifyAsync("NOPE", Scan(("Shipping", "SHIP1")), new VerifyOptions(), failThreshold: 3);

        Assert.Equal(VerifyStationStatus.NoActiveOrder, result.Status);
        Assert.Null(result.Routing);
    }
}
