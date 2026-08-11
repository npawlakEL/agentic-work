using PandA.Core;
using Xunit;

namespace PandA.Tests;

/// <summary>
/// Driver tests for <see cref="PrinterSelectionService"/> — the B1 rows of architecture-log 008.
/// Each theory row is a named scenario asserting the chosen printer per label type (eligibility,
/// load-balance ordering, and collision fallback).
/// </summary>
public sealed class PrinterSelectionMatrixTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly PrinterSelectionService _sut = new();

    private static PrinterConfig P(string id, string[] map, int order, ApplyOrientation type = ApplyOrientation.Side) =>
        new(id, "10.0.0." + order, 9100, map, type, order);

    private static PandaLabelSet Labels(params (string Type, string Lpn)[] labels) =>
        new(labels.Select(l => new Label(l.Type, l.Lpn, $"^XA{l.Type}^XZ")));

    private static Dictionary<string, PrinterState> States(params PrinterState[] states) =>
        states.ToDictionary(s => s.PrinterId, StringComparer.OrdinalIgnoreCase);

    private string? Chosen(PrinterSelectionResult r, string labelType) =>
        r.Assignments.First(a => string.Equals(a.Label.LabelType, labelType, StringComparison.OrdinalIgnoreCase))
            .PrinterId;

    // ---- Eligibility predicate (A3 → B1) --------------------------------------------------------

    public static IEnumerable<object[]> EligibilityCases()
    {
        // plcOnline, engineOnline, isSpare, expectedAssigned
        yield return ["online available", true, true, false, true];
        yield return ["plc offline", false, true, false, false];
        yield return ["engine offline", true, false, false, false];
        yield return ["held as spare", true, true, true, false];
        yield return ["plc+engine offline", false, false, false, false];
    }

    [Theory]
    [MemberData(nameof(EligibilityCases))]
    public void Eligibility_SinglePrinter_AssignsOnlyWhenAvailable(
        string because, bool plc, bool engine, bool spare, bool expectAssigned)
    {
        var line = new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]);
        var states = States(new PrinterState("Ship1", plc, engine, spare));

        var result = _sut.Select(line, states, Labels(("Shipping", "S1")));

        var status = result.Assignments.Single().Status;
        Assert.True(
            (status == LabelSelectionStatus.Assigned) == expectAssigned,
            $"{because}: expected assigned={expectAssigned} but was {status}");
    }

    [Fact]
    public void Eligibility_WrongOrientation_IsNotSelected()
    {
        var line = new LineConfig("L1", [P("Top1", ["Shipping"], 0, ApplyOrientation.Top)]);
        var states = States(new PrinterState("Top1"));

        // Default request is Side; a Top printer is ineligible.
        var side = _sut.Select(line, states, Labels(("Shipping", "S1")));
        Assert.Equal(LabelSelectionStatus.NoPrinter, side.Assignments.Single().Status);

        // Direct Top request → eligible (the one place Top is exercised; excluded from Induct e2e).
        var top = _sut.Select(line, states, Labels(("Shipping", "S1")), ApplyOrientation.Top);
        Assert.Equal("Top1", Chosen(top, "Shipping"));
    }

    [Fact]
    public void Eligibility_NoPrintersOnLine_NoPrinterForType()
    {
        var line = new LineConfig("L1", []);
        var result = _sut.Select(line, States(), Labels(("Shipping", "S1")));
        Assert.Equal(LabelSelectionStatus.NoPrinter, result.Assignments.Single().Status);
    }

    [Fact]
    public void Eligibility_PrinterCannotEmitType_NoPrinter()
    {
        var line = new LineConfig("L1", [P("Cont1", ["Content"], 0)]);
        var result = _sut.Select(line, States(new PrinterState("Cont1")), Labels(("Shipping", "S1")));
        Assert.Equal(LabelSelectionStatus.NoPrinter, result.Assignments.Single().Status);
    }

    // ---- Load balancing (A1/A3 → B1) ------------------------------------------------------------

    [Fact]
    public void LoadBalance_PicksLeastRecentlyPrinted()
    {
        var line = new LineConfig("L1", [P("A", ["Shipping"], 0), P("B", ["Shipping"], 1)], loadBalance: true);
        // A printed recently, B never → B is oldest → chosen.
        var states = States(
            new PrinterState("A", lastPrinted: T0.AddMinutes(5)),
            new PrinterState("B"));

        var result = _sut.Select(line, states, Labels(("Shipping", "S1")));
        Assert.Equal("B", Chosen(result, "Shipping"));
    }

    [Fact]
    public void LoadBalance_TieOnLastPrinted_LowestConfigOrderWins()
    {
        var line = new LineConfig("L1", [P("A", ["Shipping"], 1), P("B", ["Shipping"], 0)], loadBalance: true);
        var states = States(
            new PrinterState("A", lastPrinted: T0),
            new PrinterState("B", lastPrinted: T0));

        var result = _sut.Select(line, states, Labels(("Shipping", "S1")));
        Assert.Equal("B", Chosen(result, "Shipping")); // ConfigOrder 0
    }

    [Fact]
    public void LoadBalanceOff_LowestConfigOrderWins_IgnoringLastPrinted()
    {
        var line = new LineConfig("L1", [P("A", ["Shipping"], 0), P("B", ["Shipping"], 1)], loadBalance: false);
        // A printed most recently, but load-balance is off → first configured (A) wins anyway.
        var states = States(
            new PrinterState("A", lastPrinted: T0.AddMinutes(10)),
            new PrinterState("B"));

        var result = _sut.Select(line, states, Labels(("Shipping", "S1")));
        Assert.Equal("A", Chosen(result, "Shipping"));
    }

    // ---- Collision fallback (B1, corrected by senior) -------------------------------------------

    [Fact]
    public void Collision_SamePrimary_SameBackup_BothMoveToBackup()
    {
        // Two printers both cover both types; identical ranking → both types share primary A, share backup B.
        // Senior: there is no second pass — every colliding type independently moves to its own backup ⇒ both B.
        var line = new LineConfig("L1",
        [
            P("A", ["Shipping", "Content"], 0),
            P("B", ["Shipping", "Content"], 1),
        ], loadBalance: true);
        var states = States(new PrinterState("A"), new PrinterState("B"));

        var result = _sut.Select(line, states, Labels(("Shipping", "S1"), ("Content", "C1")));

        Assert.Equal("B", Chosen(result, "Shipping"));
        Assert.Equal("B", Chosen(result, "Content"));
    }

    [Fact]
    public void Collision_SamePrimary_OnlyOneHasBackup_OtherStaysOnPrimary()
    {
        // A covers both types (primary for both). B covers only Shipping (backup for Shipping only).
        var line = new LineConfig("L1",
        [
            P("A", ["Shipping", "Content"], 0),
            P("B", ["Shipping"], 1),
        ], loadBalance: true);
        var states = States(new PrinterState("A"), new PrinterState("B"));

        var result = _sut.Select(line, states, Labels(("Shipping", "S1"), ("Content", "C1")));

        Assert.Equal("B", Chosen(result, "Shipping"));  // moved to backup
        Assert.Equal("A", Chosen(result, "Content"));   // no backup → stays on primary
    }

    [Fact]
    public void Collision_SamePrimary_NoBackup_BothStayOnPrimary()
    {
        var line = new LineConfig("L1", [P("A", ["Shipping", "Content"], 0)], loadBalance: true);
        var states = States(new PrinterState("A"));

        var result = _sut.Select(line, states, Labels(("Shipping", "S1"), ("Content", "C1")));

        Assert.Equal("A", Chosen(result, "Shipping"));
        Assert.Equal("A", Chosen(result, "Content"));
    }

    [Fact]
    public void NoCollision_DistinctPrimaries_EachTypeGetsItsOwn()
    {
        var line = new LineConfig("L1",
        [
            P("Ship1", ["Shipping"], 0),
            P("Cont1", ["Content"], 1),
        ], loadBalance: true);
        var states = States(new PrinterState("Ship1"), new PrinterState("Cont1"));

        var result = _sut.Select(line, states, Labels(("Shipping", "S1"), ("Content", "C1")));

        Assert.Equal("Ship1", Chosen(result, "Shipping"));
        Assert.Equal("Cont1", Chosen(result, "Content"));
    }

    // ---- Duplicate label types (senior B1 addition) ---------------------------------------------

    [Fact]
    public void DuplicateTypes_ShareAssignment_JobsEqualPhysicalLabelCount()
    {
        var line = new LineConfig("L1", [P("Ship1", ["Shipping"], 0)]);
        var states = States(new PrinterState("Ship1"));

        var result = _sut.Select(line, states, Labels(("Shipping", "S1"), ("Shipping", "S2")));

        Assert.Equal(2, result.Assignments.Count); // one per physical label
        Assert.All(result.Assignments, a => Assert.Equal("Ship1", a.PrinterId));
    }

    [Fact]
    public void DuplicateTypes_NoEligiblePrinter_BothNoPrinter()
    {
        var line = new LineConfig("L1", [P("Cont1", ["Content"], 0)]);
        var states = States(new PrinterState("Cont1"));

        var result = _sut.Select(line, states, Labels(("Shipping", "S1"), ("Shipping", "S2")));

        Assert.All(result.Assignments, a => Assert.Equal(LabelSelectionStatus.NoPrinter, a.Status));
    }
}
