using PandA.Core;
using PandA.Core.Settings;
using PandA.Core.Verification;
using PandA.Sim;
using PandA.Sim.Line;
using PandA.Sim.Messaging;
using Xunit;

namespace PandA.Tests;

/// <summary>Regression guards for the PandA.Sim code-review findings (label attach, snapshots, verify, apply, bounds, framing).</summary>
public sealed class LineSimulationReviewFindingsTests
{
    private sealed record Rig(LineSimulation Sim, CapturingPrinterGateway Gateway, InMemoryTransportOrderStore Orders);

    private static Rig Build(
        IReadOnlyList<(string PrinterId, string LabelType, int PrintPulses)> printers,
        LineSimulationSettings settings)
    {
        var orders = new InMemoryTransportOrderStore();
        var gateway = new CapturingPrinterGateway();
        var clock = new SimClock(DateTimeOffset.UtcNow);

        var profile = new FirePointProfile("Active",
            [.. printers.Select(p => ((p.PrinterId, p.LabelType), new FirePoint(1, p.PrintPulses, 2, ApplyPoint.Parse("1T"))))]);
        var config = new LineConfig("L1",
            [.. printers.Select((p, i) => new PrinterConfig(p.PrinterId, $"10.0.0.{i + 1}", 9100, [p.LabelType], ApplyOrientation.Side, i))],
            activeProfile: profile);
        var lines = new InMemoryLineProvider().Add(config,
            [.. printers.Select(p => new PrinterState(p.PrinterId, plcOnline: true, engineOnline: true))]);

        var induct = TestServices.Induct(orders, lines, gateway, clock, new InMemorySettingsProvider());
        var verify = TestServices.Verify(orders, TestServices.Threshold(), clock);

        var sim = new LineSimulation(
            "L1", "Line 1", induct, verify, orders, gateway, clock, verifyFailThreshold: 2, settings,
            [.. printers.Select(p => new SimPrinterStation(p.PrinterId, "Side", PrintTrackingDevice: 1, ApplyTrackingDevice: 2, PrintFirePointPulses: p.PrintPulses))]);
        return new Rig(sim, gateway, orders);
    }

    [Fact]
    public async Task MultipleCartons_EachCarriesOnlyItsOwnLabels()
    {
        var rig = Build(
            [("Ship1", "Shipping", 40), ("Cont1", "Content", 40)],
            new LineSimulationSettings { ConveyorLengthInches = 400, BeltSpeedInchesPerSecond = 72 });

        await rig.Sim.StartAsync();
        await rig.Sim.SpawnCartonAsync();
        await rig.Sim.SpawnCartonAsync();
        await rig.Sim.SpawnCartonAsync();

        var labelsByCarton = new Dictionary<string, IReadOnlyList<LabelPlacementSnapshot>>(StringComparer.Ordinal);
        for (var i = 0; i < 600 && labelsByCarton.Count < 3; i++)
        {
            await Task.Delay(10);
            var snap = await rig.Sim.GetSnapshotAsync();
            foreach (var carton in snap.Cartons.Where(c => c.Labels.Count > 0))
            {
                labelsByCarton[carton.BlindLabel] = carton.Labels;
            }
        }

        Assert.Equal(3, labelsByCarton.Count);
        foreach (var (blind, labels) in labelsByCarton)
        {
            var seq = blind[^4..]; // blind = SIM-{lineId}-{seq:0000}
            Assert.Equal(2, labels.Count);
            Assert.Equal(
                new[] { $"CONT-SIM-{seq}", $"SHIP-SIM-{seq}" },
                labels.Select(l => l.Lpn).Order(StringComparer.Ordinal).ToArray());
        }
    }

    [Fact]
    public async Task Snapshot_IsIsolatedFromLaterSteps()
    {
        var rig = Build([("Ship1", "Shipping", 40)], new LineSimulationSettings { ConveyorLengthInches = 160, BeltSpeedInchesPerSecond = 72 });
        await rig.Sim.StartAsync();
        await rig.Sim.SpawnCartonAsync();

        LineSimulationSnapshot? old = null;
        for (var i = 0; i < 400 && old is null; i++)
        {
            await Task.Delay(5);
            var snap = await rig.Sim.GetSnapshotAsync();
            if (snap.Cartons.FirstOrDefault()?.Labels.Any(l => !l.Applied) == true)
            {
                old = snap;
            }
        }

        Assert.NotNull(old);
        var oldLabels = old.Cartons[0].Labels;
        var frozen = oldLabels.ToList();

        var changed = false;
        for (var i = 0; i < 400 && !changed; i++)
        {
            await Task.Delay(5);
            var snap = await rig.Sim.GetSnapshotAsync();
            var current = snap.Cartons.FirstOrDefault(c => c.BlindLabel == old.Cartons[0].BlindLabel);
            changed = current is null || !current.Labels.SequenceEqual(frozen);
        }

        Assert.True(changed, "the carton's labels should progress (print/apply) after the snapshot was taken");
        Assert.NotSame(oldLabels, (await rig.Sim.GetSnapshotAsync()).Cartons.FirstOrDefault()?.Labels);
        Assert.Equal(frozen, oldLabels.ToList());
    }

    [Fact]
    public async Task PartiallyPrintedCarton_IsRejectedAtVerify_NotVerified()
    {
        // Only Shipping has a printer; the order's Content label is never printed or applied, so the
        // verify scanner must not read it.
        var rig = Build([("Ship1", "Shipping", 40)], new LineSimulationSettings { ConveyorLengthInches = 160, BeltSpeedInchesPerSecond = 72 });
        await rig.Sim.StartAsync();
        await rig.Sim.SpawnCartonAsync();

        string? finalState = null;
        for (var i = 0; i < 600 && finalState is null; i++)
        {
            await Task.Delay(10);
            var snap = await rig.Sim.GetSnapshotAsync();
            var state = snap.Cartons.FirstOrDefault()?.State;
            if (state is "Verified" or "Rejected")
            {
                finalState = state;
            }
        }

        Assert.Equal("Rejected", finalState);
    }

    [Fact]
    public async Task LabelPrintedAfterCartonPassedStation_IsStillApplied()
    {
        // Print point far downstream of the apply station: the label reaches the tamp head only after the
        // carton's centre has already passed under the printer.
        var settings = new LineSimulationSettings { ConveyorLengthInches = 260, BeltSpeedInchesPerSecond = 72 };
        var rig = Build([("Ship1", "Shipping", 600)], settings);
        await rig.Sim.StartAsync();
        await rig.Sim.SpawnCartonAsync();

        var stationX = 0.0;
        var printedPastStation = false;
        var applied = false;
        for (var i = 0; i < 800 && !applied; i++)
        {
            await Task.Delay(5);
            var snap = await rig.Sim.GetSnapshotAsync();
            stationX = snap.Printers.Single(p => p.PrinterId == "Ship1").PositionInches;
            var label = snap.Cartons.FirstOrDefault()?.Labels.FirstOrDefault();
            if (label is null)
            {
                continue;
            }

            printedPastStation |= label.PrintPoint.X - snap.Cartons[0].LengthInches / 2 > stationX;
            applied = label.Applied && !label.OnTamp;
        }

        Assert.True(printedPastStation, "test setup: the print point should be downstream of the apply station");
        Assert.True(applied, "a label printed after its carton passed the station must not stay on the tamp forever");
    }

    [Fact]
    public async Task OrdersForCartonsThatLeaveTheLine_AreRemovedFromTheStore()
    {
        var rig = Build([("Ship1", "Shipping", 40)], new LineSimulationSettings { ConveyorLengthInches = 160, BeltSpeedInchesPerSecond = 72 });
        await rig.Sim.StartAsync();
        await rig.Sim.SpawnCartonAsync();
        Assert.Single(rig.Orders.All);

        for (var i = 0; i < 800 && rig.Orders.All.Count > 0; i++)
        {
            await Task.Delay(10);
            await rig.Sim.GetSnapshotAsync();
        }

        Assert.Empty(rig.Orders.All);
        Assert.Empty((await rig.Sim.GetSnapshotAsync()).Cartons);
    }

    [Fact]
    public async Task ClearCartons_RemovesTheirOrders()
    {
        var rig = Build([("Ship1", "Shipping", 40)], new LineSimulationSettings());
        await rig.Sim.SpawnCartonAsync();
        await rig.Sim.SpawnCartonAsync();
        Assert.Equal(2, rig.Orders.All.Count);

        await rig.Sim.ClearCartonsAsync();

        Assert.Empty(rig.Orders.All);
    }

    [Fact]
    public async Task CapturingGateway_CapsHistory_AndRangeReadsUseAbsoluteIndex()
    {
        var gateway = new CapturingPrinterGateway(maxRetainedJobs: 3);
        for (var i = 0; i < 5; i++)
        {
            await gateway.SendAsync(new PrintJob("P1", "10.0.0.1", 9100, "Shipping", $"LPN{i}", "^XA^XZ"));
        }

        Assert.Equal(5, gateway.Count);
        Assert.Equal(new[] { "LPN2", "LPN3", "LPN4" }, gateway.Jobs.Select(j => j.Lpn));
        Assert.Equal(new[] { "LPN4" }, gateway.JobsSince(4).Select(j => j.Lpn));
        Assert.Equal(new[] { "LPN2", "LPN3", "LPN4" }, gateway.JobsSince(0).Select(j => j.Lpn));
        Assert.Empty(gateway.JobsSince(5));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Normalize_ReplacesNonFiniteValuesWithDefaults(double bad)
    {
        var defaults = new LineSimulationSettings();
        var normalized = new LineSimulationSettings
        {
            EncoderResolutionInchesPerPulse = bad,
            BeltSpeedInchesPerSecond = bad,
            ConveyorLengthInches = bad,
            CartonLengthInches = bad,
            CartonWidthInches = bad,
            CartonHeightInches = bad,
            DefaultApplyDistanceInches = bad,
            PrintFirePointOffsetInches = bad,
            ApplyFirePointOffsetInches = bad,
            CartonSpacingInches = bad,
        }.Normalize();

        Assert.Equal(defaults.Normalize(), normalized);
    }

    [Theory]
    [InlineData("BL,1")]
    [InlineData("BL<1")]
    [InlineData("BL>1")]
    [InlineData("BL|1")]
    public void InductScan_ToFrame_RejectsDelimitersInBlindLabel(string blind)
    {
        var scan = new InductScanMessage(1, 1, 1, 1, 1, 0, blind);
        Assert.Throws<ArgumentException>(() => scan.ToFrame());
    }

    [Theory]
    [InlineData("LPN,1")]
    [InlineData("LPN|1")]
    [InlineData("LPN>1")]
    public void VerifyScan_ToFrame_RejectsDelimitersInLabelValues(string lpn)
    {
        var scan = new VerifyScanMessage(1, 1, 1, 2, 1, ["OK", lpn]);
        Assert.Throws<ArgumentException>(() => scan.ToFrame());
    }

    [Fact]
    public void VerifyScan_RoundTripsPipeDelimitedBuffer()
    {
        var scan = new VerifyScanMessage(1, 1, 1, 2, 7, ["123456798", "0154006001"]);
        var parsed = VerifyScanMessage.FromFrame(PaFrame.Parse(scan.ToFrame().ToWire()));
        Assert.Equal(scan.LabelBuffer, parsed.LabelBuffer);
    }

    [Fact]
    public void PaFrame_ToWire_RejectsDelimiterInField()
    {
        var frame = new PaFrame(281, ["1", "a,b"]);
        Assert.Throws<ArgumentException>(() => frame.ToWire());
    }

    [Theory]
    [InlineData("<281>")]
    [InlineData("<281,1,1,1>")]
    [InlineData("<286,1,1>")]
    public void ShortFrames_ThrowFormatException(string wire)
    {
        var frame = PaFrame.Parse(wire);
        if (frame.Code == InductScanMessage.Code)
        {
            Assert.Throws<FormatException>(() => InductScanMessage.FromFrame(frame));
        }
        else
        {
            Assert.Throws<FormatException>(() => VerifyScanMessage.FromFrame(frame));
        }
    }
}
