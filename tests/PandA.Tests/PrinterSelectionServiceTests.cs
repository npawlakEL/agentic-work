using PandA.Core;

namespace PandA.Tests;

/// <summary>
/// Tests for the authoritative printer-selection algorithm (architecture-log 005).
/// </summary>
public sealed class PrinterSelectionServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IPrinterSelectionService _sut = new PrinterSelectionService();

    // ---- helpers -------------------------------------------------------------------------------

    private static PrinterConfig Printer(
        string id,
        string[] labelMap,
        int order,
        ApplyOrientation type = ApplyOrientation.Side) =>
        new(id, "10.0.0.1", 9100, labelMap, type, order);

    private static PandaLabelSet Labels(params string[] types) =>
        new(types.Select(t => new Label(t, $"LPN-{t}", $"^XA{t}^XZ")));

    private static Dictionary<string, PrinterState> States(params PrinterState[] states) =>
        states.ToDictionary(s => s.PrinterId, StringComparer.OrdinalIgnoreCase);

    private string? Assigned(PrinterSelectionResult r, string labelType) =>
        r.Assignments.Single(a => a.Label.LabelType == labelType).PrinterId;

    // ---- eligibility ---------------------------------------------------------------------------

    [Fact]
    public void SingleLabel_SingleEligiblePrinter_IsAssigned()
    {
        var line = new LineConfig("L1", [Printer("P1", ["Shipping"], 0)]);
        var result = _sut.Select(line, States(), Labels("Shipping"));

        Assert.True(result.AllAssigned);
        Assert.Equal("P1", Assigned(result, "Shipping"));
    }

    [Fact]
    public void NoPrinterServesType_YieldsNoPrinter()
    {
        var line = new LineConfig("L1", [Printer("P1", ["Content"], 0)]);
        var result = _sut.Select(line, States(), Labels("Shipping"));

        Assert.False(result.AllAssigned);
        Assert.Equal(LabelSelectionStatus.NoPrinter, result.Assignments.Single().Status);
    }

    [Theory]
    [InlineData(false, true, false)]  // PLC offline
    [InlineData(true, false, false)]  // engine offline
    [InlineData(true, true, true)]    // spare
    public void UnavailablePrinter_IsExcluded(bool plc, bool engine, bool spare)
    {
        var line = new LineConfig("L1",
        [
            Printer("P1", ["Shipping"], 0),
            Printer("P2", ["Shipping"], 1),
        ]);
        var states = States(
            new PrinterState("P1", plcOnline: plc, engineOnline: engine, isSpare: spare, lastPrinted: T0),
            new PrinterState("P2", lastPrinted: T0.AddHours(1)));

        var result = _sut.Select(line, states, Labels("Shipping"));

        // P1 is ineligible despite being least-recently-printed, so P2 wins.
        Assert.Equal("P2", Assigned(result, "Shipping"));
    }

    // ---- load balancing ------------------------------------------------------------------------

    [Fact]
    public void LoadBalance_PicksLeastRecentlyPrinted()
    {
        var line = new LineConfig("L1",
        [
            Printer("P1", ["Shipping"], 0),
            Printer("P2", ["Shipping"], 1),
        ]);
        var states = States(
            new PrinterState("P1", lastPrinted: T0.AddHours(5)),
            new PrinterState("P2", lastPrinted: T0.AddHours(1))); // older → picked

        Assert.Equal("P2", Assigned(_sut.Select(line, states, Labels("Shipping")), "Shipping"));
    }

    [Fact]
    public void EqualLastPrinted_TieBreaksByConfiguredOrder()
    {
        var line = new LineConfig("L1",
        [
            Printer("P2", ["Shipping"], 5),
            Printer("P1", ["Shipping"], 2), // lower ConfigOrder wins the tie
        ]);
        // both never printed (null == null tie / cold start)
        var result = _sut.Select(line, States(), Labels("Shipping"));

        Assert.Equal("P1", Assigned(result, "Shipping"));
    }

    [Fact]
    public void LoadBalanceOff_UsesFirstConfiguredEligiblePrinter_IgnoringLastPrinted()
    {
        var line = new LineConfig("L1",
        [
            Printer("P1", ["Shipping"], 0),
            Printer("P2", ["Shipping"], 1),
        ],
        loadBalance: false);
        var states = States(
            new PrinterState("P1", lastPrinted: T0.AddHours(5)),  // most recently printed
            new PrinterState("P2", lastPrinted: T0.AddHours(1)));

        // LoadBalance off → first configured (P1) regardless of LastPrinted.
        Assert.Equal("P1", Assigned(_sut.Select(line, states, Labels("Shipping")), "Shipping"));
    }

    // ---- same-carton collision → backup --------------------------------------------------------

    [Fact]
    public void Collision_WhenOnePrinterIsPrimaryForTwoTypes_SecondFallsBackToBackup()
    {
        // P1 serves both A and B and is least-recently-printed for both (primary for both → collision).
        // A's backup is P2, B's backup is P3 → labels spread across distinct printers.
        var line = new LineConfig("L1",
        [
            Printer("P1", ["A", "B"], 0),
            Printer("P2", ["A"], 1),
            Printer("P3", ["B"], 2),
        ]);
        var states = States(
            new PrinterState("P1", lastPrinted: T0),            // oldest → primary for A and B
            new PrinterState("P2", lastPrinted: T0.AddHours(1)),
            new PrinterState("P3", lastPrinted: T0.AddHours(1)));

        var result = _sut.Select(line, states, Labels("A", "B"));

        Assert.Equal("P2", Assigned(result, "A"));
        Assert.Equal("P3", Assigned(result, "B"));
    }

    [Fact]
    public void Collision_WithNoBackup_FallsBackToPrimary_SerializedOnSamePrinter()
    {
        // Only P1 serves both types; no backup exists (PID2 null) → both stay on P1 (ELSE PID1).
        var line = new LineConfig("L1", [Printer("P1", ["A", "B"], 0)]);
        var result = _sut.Select(line, States(), Labels("A", "B"));

        Assert.Equal("P1", Assigned(result, "A"));
        Assert.Equal("P1", Assigned(result, "B"));
    }

    [Fact]
    public void MultiType_NoCollision_KeepsEachPrimary()
    {
        var line = new LineConfig("L1",
        [
            Printer("P1", ["A"], 0),
            Printer("P2", ["B"], 1),
        ]);
        var result = _sut.Select(line, States(), Labels("A", "B"));

        Assert.Equal("P1", Assigned(result, "A"));
        Assert.Equal("P2", Assigned(result, "B"));
    }

    [Fact]
    public void MultipleLabelsOfSameType_ShareTheChosenPrinter()
    {
        var line = new LineConfig("L1", [Printer("P1", ["Shipping"], 0)]);
        var labels = new PandaLabelSet(
        [
            new Label("Shipping", "LPN-1", "^XA1^XZ"),
            new Label("Shipping", "LPN-2", "^XA2^XZ"),
        ]);

        var result = _sut.Select(line, States(), labels);

        Assert.All(result.Assignments, a => Assert.Equal("P1", a.PrinterId));
        Assert.Equal(2, result.Assignments.Count);
    }

    // ---- orientation (provisioned dimension) ---------------------------------------------------

    [Fact]
    public void Orientation_PrinterTypeMustMatchRequiredOrientation()
    {
        var line = new LineConfig("L1",
        [
            Printer("Top1", ["Shipping"], 0, ApplyOrientation.Top),
            Printer("Side1", ["Shipping"], 1, ApplyOrientation.Side),
        ]);

        var side = _sut.Select(line, States(), Labels("Shipping"), ApplyOrientation.Side);
        var top = _sut.Select(line, States(), Labels("Shipping"), ApplyOrientation.Top);

        Assert.Equal("Side1", Assigned(side, "Shipping"));
        Assert.Equal("Top1", Assigned(top, "Shipping"));
    }

    [Fact]
    public void Orientation_NoPrinterOfRequiredOrientation_YieldsNoPrinter()
    {
        var line = new LineConfig("L1", [Printer("Side1", ["Shipping"], 0, ApplyOrientation.Side)]);
        var result = _sut.Select(line, States(), Labels("Shipping"), ApplyOrientation.Top);

        Assert.Equal(LabelSelectionStatus.NoPrinter, result.Assignments.Single().Status);
    }
}
