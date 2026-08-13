using PandA.Sim.Line;

namespace PandA.Tests;

public class PrinterLayoutTests
{
    // Demo Line 1 topology: Ship1(Side), Ship2(Side), Cont1(Top).
    private static readonly IReadOnlyList<SimPrinterStation> Line1 =
    [
        new SimPrinterStation("Ship1", "Side"),
        new SimPrinterStation("Ship2", "Side"),
        new SimPrinterStation("Cont1", "Top"),
    ];

    [Fact]
    public void LayoutPrinters_GivesEveryPrinterADistinctPosition()
    {
        var stations = LineSimulation.LayoutPrinters(Line1, centerX: 200);

        var xs = stations.Select(s => s.PositionInches).ToList();
        Assert.Equal(xs.Count, xs.Distinct().Count());
        Assert.Equal(3, stations.Count);
    }

    [Fact]
    public void LayoutPrinters_KeepsSameOrientationPrintersAdjacent()
    {
        var stations = LineSimulation.LayoutPrinters(Line1, centerX: 200);

        var orientations = stations
            .OrderBy(s => s.PositionInches)
            .Select(s => s.Orientation)
            .ToList();

        // Both Side printers must sit next to each other (no Top wedged between them).
        Assert.Equal(new[] { "Side", "Side", "Top" }, orientations);
    }

    [Fact]
    public void LayoutPrinters_CentresTheBankOnTheZoneCentre()
    {
        var stations = LineSimulation.LayoutPrinters(Line1, centerX: 200, spacingInches: 30);

        var avg = stations.Average(s => s.PositionInches);
        Assert.Equal(200, avg, precision: 3);
    }

    [Fact]
    public void LayoutPrinters_Empty_ReturnsEmpty()
    {
        Assert.Empty(LineSimulation.LayoutPrinters([], centerX: 100));
    }
}
