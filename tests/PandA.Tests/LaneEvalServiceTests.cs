using PandA.Core;
using Xunit;

namespace PandA.Tests;

/// <summary>
/// Tests for <see cref="LaneEvalService"/> — the ported <c>sdisp_PA_LaneEval</c> spare/slow/shut loop
/// (architecture-log 012). Covers per-orientation balancing, spare promotion/demotion, the generalized
/// degraded (slow-line) policy including the legacy 2-printer case, hard shutdown, zone shutdown, and
/// orientation independence.
/// </summary>
public sealed class LaneEvalServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly LaneEvalService _sut = new();

    private static PrinterConfig P(string id, ApplyOrientation type = ApplyOrientation.Side) =>
        new(id, "10.0.0.1", 9100, ["Shipping"], type);

    private static Dictionary<string, PrinterState> States(params PrinterState[] s) =>
        s.ToDictionary(x => x.PrinterId, StringComparer.OrdinalIgnoreCase);

    private static LineConfig Line(
        IEnumerable<PrinterConfig> printers,
        (ApplyOrientation Orient, PrinterGroupPolicy Policy)[] policies) =>
        new(
            "panda_01",
            printers,
            printerPolicies: policies.ToDictionary(p => p.Orient, p => p.Policy));

    private static PrinterGroupPolicy Policy(int min, int count, int floor = 1, bool degraded = false) =>
        new(min, count, floor, degraded);

    private LaneEvalResult Eval(LineConfig line, Dictionary<string, PrinterState> states, bool zoneOnline = true) =>
        _sut.Evaluate(line, states, new ZoneState(zoneOnline), Now);

    // ---- Zone -----------------------------------------------------------------------------------

    [Fact]
    public void ZoneDown_ShutsZone_NoSpareMath()
    {
        var line = Line([P("A"), P("B")], [(ApplyOrientation.Side, Policy(2, 2))]);
        var states = States(new PrinterState("A"), new PrinterState("B"));

        var result = Eval(line, states, zoneOnline: false);

        Assert.Equal(LineControl.ShutZone, result.Control);
        Assert.Empty(result.Changes);
    }

    // ---- Balanced -------------------------------------------------------------------------------

    [Fact]
    public void ExactlyMinOnline_Balanced_NoChanges()
    {
        var line = Line([P("A"), P("B")], [(ApplyOrientation.Side, Policy(2, 2))]);
        var states = States(new PrinterState("A"), new PrinterState("B"));

        var result = Eval(line, states);

        Assert.Equal(LineControl.Balanced, result.Control);
        Assert.Empty(result.Changes);
        Assert.False(states["A"].IsSpare);
        Assert.False(states["B"].IsSpare);
    }

    // ---- Demote surplus -------------------------------------------------------------------------

    [Fact]
    public void SurplusOnline_DemotesNewestToSpare()
    {
        var line = Line([P("A"), P("B"), P("C")], [(ApplyOrientation.Side, Policy(2, 3))]);
        var states = States(
            new PrinterState("A", lastStatusUpdate: Now.AddHours(-3)),
            new PrinterState("B", lastStatusUpdate: Now.AddHours(-2)),
            new PrinterState("C", lastStatusUpdate: Now.AddHours(-1))); // newest → demoted

        var result = Eval(line, states);

        Assert.Equal(LineControl.Balanced, result.Control);
        var change = Assert.Single(result.Changes);
        Assert.Equal("C", change.PrinterId);
        Assert.Equal(SpareChange.DemotedToSpare, change.Change);
        Assert.True(states["C"].IsSpare);
        Assert.Equal(Now, states["C"].LastStatusUpdate);
    }

    [Fact]
    public void AlreadyParkedSpare_AtMin_NoFurtherDemote()
    {
        var line = Line([P("A"), P("B"), P("C")], [(ApplyOrientation.Side, Policy(2, 3))]);
        var states = States(
            new PrinterState("A"),
            new PrinterState("B"),
            new PrinterState("C", isSpare: true)); // usable = 2 = min already

        var result = Eval(line, states);

        Assert.Equal(LineControl.Balanced, result.Control);
        Assert.Empty(result.Changes);
    }

    // ---- Promote spare --------------------------------------------------------------------------

    [Fact]
    public void ActiveLost_PromotesNewestSpare_BackToBalanced()
    {
        var line = Line([P("A"), P("B"), P("C")], [(ApplyOrientation.Side, Policy(2, 3))]);
        var states = States(
            new PrinterState("A"),                                                 // online active
            new PrinterState("B", plcOnline: false),                               // active lost
            new PrinterState("C", isSpare: true, lastStatusUpdate: Now.AddDays(-1))); // spare → promote

        var result = Eval(line, states);

        Assert.Equal(LineControl.Balanced, result.Control);
        var change = Assert.Single(result.Changes);
        Assert.Equal("C", change.PrinterId);
        Assert.Equal(SpareChange.PromotedFromSpare, change.Change);
        Assert.False(states["C"].IsSpare);
    }

    [Fact]
    public void OfflinePrinterHoldingSpareFlag_IsClearedBeforeLogic()
    {
        var line = Line([P("A"), P("B")], [(ApplyOrientation.Side, Policy(1, 2))]);
        // B is offline but somehow flagged spare; it must be cleared (a down printer isn't a reserve).
        var states = States(
            new PrinterState("A"),
            new PrinterState("B", plcOnline: false, isSpare: true));

        var result = Eval(line, states);

        Assert.False(states["B"].IsSpare);
        // A alone satisfies min=1, usable=1, no surplus → balanced.
        Assert.Equal(LineControl.Balanced, result.Control);
    }

    // ---- Degraded / slow line -------------------------------------------------------------------

    [Fact]
    public void LegacyTwoPrinterRule_LoseOne_SlowsLine()
    {
        // count=2, min=2, degraded on, floor=1  == source "2 Printer Rule".
        var line = Line([P("A"), P("B")], [(ApplyOrientation.Side, Policy(2, 2, floor: 1, degraded: true))]);
        var states = States(new PrinterState("A"), new PrinterState("B", engineOnline: false));

        var result = Eval(line, states);

        Assert.Equal(LineControl.SlowLine, result.Control);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void DegradedDisabled_LoseOne_ShutsLine()
    {
        var line = Line([P("A"), P("B")], [(ApplyOrientation.Side, Policy(2, 2, degraded: false))]);
        var states = States(new PrinterState("A"), new PrinterState("B", engineOnline: false));

        var result = Eval(line, states);

        Assert.Equal(LineControl.ShutLine, result.Control);
    }

    [Fact]
    public void OverProvisioned_DegradedLimpsOnSurvivors()
    {
        // 4 installed, min 2, degraded floor 1 → lose 3, still run slow on the last one.
        var line = Line(
            [P("A"), P("B"), P("C"), P("D")],
            [(ApplyOrientation.Side, Policy(2, 4, floor: 1, degraded: true))]);
        var states = States(
            new PrinterState("A"),
            new PrinterState("B", plcOnline: false),
            new PrinterState("C", plcOnline: false),
            new PrinterState("D", plcOnline: false));

        var result = Eval(line, states);

        Assert.Equal(LineControl.SlowLine, result.Control);
    }

    [Fact]
    public void BelowSlowLineFloor_ShutsEvenWhenDegradedAllowed()
    {
        // floor=2 but only 1 usable → below floor → shut.
        var line = Line(
            [P("A"), P("B"), P("C")],
            [(ApplyOrientation.Side, Policy(2, 3, floor: 2, degraded: true))]);
        var states = States(
            new PrinterState("A"),
            new PrinterState("B", plcOnline: false),
            new PrinterState("C", plcOnline: false));

        var result = Eval(line, states);

        Assert.Equal(LineControl.ShutLine, result.Control);
    }

    [Fact]
    public void MultipleSpares_PromotedAcrossPasses_UntilBalanced()
    {
        // usable starts at min-2 with two online spares → the re-eval loop must promote twice.
        var line = Line(
            [P("A"), P("B"), P("C"), P("D"), P("E")],
            [(ApplyOrientation.Side, Policy(3, 5))]);
        var states = States(
            new PrinterState("A"),                                                   // active
            new PrinterState("B", plcOnline: false),                                 // offline
            new PrinterState("C", isSpare: true, lastStatusUpdate: Now.AddHours(-3)),
            new PrinterState("D", isSpare: true, lastStatusUpdate: Now.AddHours(-2)),
            new PrinterState("E", isSpare: true, lastStatusUpdate: Now.AddHours(-1)));

        var result = Eval(line, states);

        Assert.Equal(LineControl.Balanced, result.Control);
        Assert.Equal(2, result.Changes.Count);
        Assert.All(result.Changes, c => Assert.Equal(SpareChange.PromotedFromSpare, c.Change));
        // Newest two spares (E then D) promoted; C (oldest) stays spare so usable == min.
        Assert.False(states["E"].IsSpare);
        Assert.False(states["D"].IsSpare);
        Assert.True(states["C"].IsSpare);
    }

    [Fact]
    public void PromotesNewestSpare_ByLastStatusUpdate()
    {
        var line = Line([P("A"), P("B"), P("C")], [(ApplyOrientation.Side, Policy(2, 3))]);
        var states = States(
            new PrinterState("A"),                                                    // 1 usable
            new PrinterState("B", isSpare: true, lastStatusUpdate: Now.AddHours(-5)),  // older spare
            new PrinterState("C", isSpare: true, lastStatusUpdate: Now.AddHours(-1))); // newer spare → promote

        var result = Eval(line, states);

        Assert.Equal(LineControl.Balanced, result.Control);
        var change = Assert.Single(result.Changes);
        Assert.Equal("C", change.PrinterId);
        Assert.False(states["C"].IsSpare);
        Assert.True(states["B"].IsSpare);
    }

    [Fact]
    public void SurplusOfTwo_ShedsOnlyOneSparePerEvaluation()
    {
        // usable = min + 2; demote has no re-eval (source-faithful) → exactly one shed per signal.
        var line = Line([P("A"), P("B"), P("C"), P("D")], [(ApplyOrientation.Side, Policy(2, 4))]);
        var states = States(
            new PrinterState("A"),
            new PrinterState("B"),
            new PrinterState("C"),
            new PrinterState("D"));

        var result = Eval(line, states);

        var change = Assert.Single(result.Changes);
        Assert.Equal(SpareChange.DemotedToSpare, change.Change);
        Assert.Equal(1, states.Values.Count(s => s.IsSpare)); // still one over min after this call
    }

    [Fact]
    public void ZoneDown_ShortCircuits_LeavesSpareFlagsUntouched()
    {
        var line = Line([P("A"), P("B")], [(ApplyOrientation.Side, Policy(1, 2))]);
        // Offline printer holding a stale spare flag; zone-down returns before the offline-clear.
        var offlineSpare = new PrinterState("B", plcOnline: false, isSpare: true, lastStatusUpdate: Now.AddDays(-1));
        var states = States(new PrinterState("A"), offlineSpare);

        var result = Eval(line, states, zoneOnline: false);

        Assert.Equal(LineControl.ShutZone, result.Control);
        Assert.True(states["B"].IsSpare);                       // not cleared
        Assert.Equal(Now.AddDays(-1), states["B"].LastStatusUpdate); // not touched
    }

    // ---- Shut -----------------------------------------------------------------------------------

    [Fact]
    public void AllOffline_NoSpare_ShutsLine()
    {
        var line = Line([P("A"), P("B")], [(ApplyOrientation.Side, Policy(2, 2))]);
        var states = States(
            new PrinterState("A", plcOnline: false),
            new PrinterState("B", plcOnline: false));

        var result = Eval(line, states);

        Assert.Equal(LineControl.ShutLine, result.Control);
    }

    // ---- Orientation independence ---------------------------------------------------------------

    [Fact]
    public void OrientationsEvaluatedIndependently()
    {
        // Side loses one (shut), Top is fine → most severe wins (ShutLine), Top untouched.
        var line = Line(
            [P("S1"), P("S2"), P("T1", ApplyOrientation.Top)],
            [
                (ApplyOrientation.Side, Policy(2, 2)),
                (ApplyOrientation.Top, Policy(1, 1)),
            ]);
        var states = States(
            new PrinterState("S1"),
            new PrinterState("S2", plcOnline: false),
            new PrinterState("T1"));

        var result = Eval(line, states);

        Assert.Equal(LineControl.ShutLine, result.Control);
        Assert.False(states["T1"].IsSpare); // Top group unaffected by Side's failure
    }

    [Fact]
    public void SurplusInOneOrientationDoesNotCoverAnother()
    {
        // Side has a surplus (demote), Top is below min with no spare (shut). Independent outcomes.
        var line = Line(
            [P("S1"), P("S2"), P("S3"), P("T1", ApplyOrientation.Top)],
            [
                (ApplyOrientation.Side, Policy(2, 3)),
                (ApplyOrientation.Top, Policy(1, 1)),
            ]);
        var states = States(
            new PrinterState("S1"),
            new PrinterState("S2"),
            new PrinterState("S3"),
            new PrinterState("T1", plcOnline: false));

        var result = Eval(line, states);

        Assert.Equal(LineControl.ShutLine, result.Control);
        Assert.Contains(result.Changes, c => c.Change == SpareChange.DemotedToSpare); // Side still demoted
    }

    // ---- No policy configured -------------------------------------------------------------------

    [Fact]
    public void OrientationWithoutPolicy_IsUnconstrained()
    {
        var line = Line([P("A"), P("B")], []); // no policies at all
        var states = States(new PrinterState("A", plcOnline: false), new PrinterState("B", plcOnline: false));

        var result = Eval(line, states);

        Assert.Equal(LineControl.Balanced, result.Control);
        Assert.Empty(result.Changes);
    }

    // ---- Selection integration ------------------------------------------------------------------

    [Fact]
    public void DemotedSpare_IsExcludedFromSelection_PromotedReenters()
    {
        var line = Line([P("A"), P("B"), P("C")], [(ApplyOrientation.Side, Policy(2, 3))]);
        var states = States(
            new PrinterState("A", lastStatusUpdate: Now.AddHours(-3)),
            new PrinterState("B", lastStatusUpdate: Now.AddHours(-2)),
            new PrinterState("C", lastStatusUpdate: Now.AddHours(-1)));

        Eval(line, states); // demotes C to spare

        var selection = new PrinterSelectionService();
        var labels = new PandaLabelSet([new Label("Shipping", "L1", "^XA^XZ")]);
        var chosen = selection.Select(line, states, labels)
            .Assignments.Select(a => a.PrinterId).ToList();

        Assert.DoesNotContain("C", chosen); // spare excluded
    }
}
