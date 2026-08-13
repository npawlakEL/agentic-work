using PandA.Sim.Line;

namespace PandA.Tests;

public class PrinterLayoutTests
{
    // Demo Line 1: Ship1 + Ship2 share apply device 2 (Side); Cont1 applies at device 3 (Top).
    private static readonly IReadOnlyList<SimPrinterStation> Line1 =
    [
        new SimPrinterStation("Ship1", "Side", PrintTrackingDevice: 1, ApplyTrackingDevice: 2),
        new SimPrinterStation("Ship2", "Side", PrintTrackingDevice: 1, ApplyTrackingDevice: 2),
        new SimPrinterStation("Cont1", "Top", PrintTrackingDevice: 1, ApplyTrackingDevice: 3),
    ];

    private static readonly IReadOnlyDictionary<int, double> DeviceX =
        new Dictionary<int, double> { [1] = 40, [2] = 120, [3] = 180 };

    [Fact]
    public void SeatPrinters_SeatsEachPrinterAtItsApplyEye()
    {
        var stations = LineSimulation.SeatPrinters(Line1, DeviceX);

        // Cont1 is the only printer on device 3 -> sits exactly on that eye.
        var cont1 = stations.Single(s => s.PrinterId == "Cont1");
        Assert.Equal(180, cont1.PositionInches, precision: 3);
    }

    [Fact]
    public void SeatPrinters_SharedApplyDevice_PlacesPrintersAdjacentNotStacked()
    {
        var stations = LineSimulation.SeatPrinters(Line1, DeviceX, spacingInches: 26);

        var ship1 = stations.Single(s => s.PrinterId == "Ship1").PositionInches;
        var ship2 = stations.Single(s => s.PrinterId == "Ship2").PositionInches;

        // Two side printers sharing apply eye 2 must be distinct and one spacing apart, centred on the eye.
        Assert.NotEqual(ship1, ship2);
        Assert.Equal(26, Math.Abs(ship1 - ship2), precision: 3);
        Assert.Equal(120, (ship1 + ship2) / 2, precision: 3);
    }

    [Fact]
    public void SeatPrinters_GivesEveryPrinterADistinctPosition()
    {
        var xs = LineSimulation.SeatPrinters(Line1, DeviceX).Select(s => s.PositionInches).ToList();

        Assert.Equal(xs.Count, xs.Distinct().Count());
        Assert.Equal(3, xs.Count);
    }

    [Fact]
    public void SeatPrinters_KeepsSameOrientationPrintersContiguous()
    {
        var orientations = LineSimulation.SeatPrinters(Line1, DeviceX)
            .OrderBy(s => s.PositionInches)
            .Select(s => s.Orientation)
            .ToList();

        Assert.Equal(new[] { "Side", "Side", "Top" }, orientations);
    }

    [Fact]
    public void SeatPrinters_Empty_ReturnsEmpty()
    {
        Assert.Empty(LineSimulation.SeatPrinters([], DeviceX));
    }
}
