using PandA.Core;
using PandA.Core.Settings;
using PandA.Core.Verification;
using PandA.Sim;
using PandA.Sim.Line;
using Xunit;

namespace PandA.Tests;

/// <summary>
/// Guards the sim's end-to-end print/apply path: a carton spawned onto a configured line must actually
/// print at its print eye and get its label applied at its apply eye. Regression for the bug where the
/// simulator passed a bogus profile name ("Line Simulation") into SetAdviceMetadata, so PROFSW gating
/// returned NoProfile and nothing ever printed.
/// </summary>
public sealed class LineSimulationPrintFlowTests
{
    [Fact]
    public async Task SpawnedCarton_PrintsThenApplies_LabelOnLine()
    {
        var orders = new InMemoryTransportOrderStore();
        var gateway = new CapturingPrinterGateway();
        var clock = new SimClock(DateTimeOffset.UtcNow);

        var profile = new FirePointProfile("Active",
            [(("Ship1", "Shipping"), new FirePoint(1, 40, 2, ApplyPoint.Parse("1T")))]);
        var config = new LineConfig("L1", [new PrinterConfig("Ship1", "10.0.0.1", 9100, ["Shipping"], ApplyOrientation.Side, 0)], activeProfile: profile);
        var lines = new InMemoryLineProvider().Add(config, [new PrinterState("Ship1", plcOnline: true, engineOnline: true)]);

        var induct = new InductService(orders, lines, new PrinterSelectionService(), gateway, clock, new InMemorySettingsProvider());
        var verify = new VerifyStationService(orders, new VerificationService(), new VerifyThresholdTracker(), clock);

        var sim = new LineSimulation(
            "L1", "Line 1", induct, verify, orders, gateway, clock, verifyFailThreshold: 2,
            new LineSimulationSettings
            {
                ConveyorLengthInches = 120,
                BeltSpeedInchesPerSecond = 200,
            },
            [new SimPrinterStation("Ship1", "Side", PrintTrackingDevice: 1, ApplyTrackingDevice: 2, PrintFirePointPulses: 40)]);

        await sim.StartAsync();
        await sim.SpawnCartonAsync();

        var sawOnTamp = false;
        var sawApplied = false;
        for (var i = 0; i < 200 && !sawApplied; i++)
        {
            await Task.Delay(10);
            var snap = await sim.GetSnapshotAsync();
            foreach (var label in snap.Cartons.SelectMany(c => c.Labels))
            {
                sawOnTamp |= label.OnTamp;
                sawApplied |= label.Applied;
            }
        }

        Assert.True(gateway.Jobs.Count > 0, "carton should have printed at least one label");
        Assert.True(sawOnTamp, "label should ride the tamp head (OnTamp) after printing");
        Assert.True(sawApplied, "label should be applied to the carton at its apply eye");
    }

    [Fact]
    public async Task ThreeCartonsSpawnedTogether_RoundRobinAcrossTwoSidePrinters()
    {
        var orders = new InMemoryTransportOrderStore();
        var gateway = new CapturingPrinterGateway();
        var clock = new SimClock(DateTimeOffset.UtcNow);

        // Two side printers eligible for Shipping; load balancing on so selection rotates by LastPrinted.
        var profile = new FirePointProfile("Active",
        [
            (("Ship1", "Shipping"), new FirePoint(1, 40, 2, ApplyPoint.Parse("1T"))),
            (("Ship2", "Shipping"), new FirePoint(1, 40, 2, ApplyPoint.Parse("1T"))),
        ]);
        var config = new LineConfig("L1",
        [
            new PrinterConfig("Ship1", "10.0.0.1", 9100, ["Shipping"], ApplyOrientation.Side, 0),
            new PrinterConfig("Ship2", "10.0.0.2", 9100, ["Shipping"], ApplyOrientation.Side, 1),
        ],
        loadBalance: true,
        activeProfile: profile);
        var lines = new InMemoryLineProvider().Add(config,
        [
            new PrinterState("Ship1", plcOnline: true, engineOnline: true, lastPrinted: clock.UtcNow),
            new PrinterState("Ship2", plcOnline: true, engineOnline: true, lastPrinted: clock.UtcNow.AddMinutes(-1)),
        ]);

        var induct = new InductService(orders, lines, new PrinterSelectionService(), gateway, clock, new InMemorySettingsProvider());
        var verify = new VerifyStationService(orders, new VerificationService(), new VerifyThresholdTracker(), clock);

        var sim = new LineSimulation(
            "L1", "Line 1", induct, verify, orders, gateway, clock, verifyFailThreshold: 2,
            new LineSimulationSettings { ConveyorLengthInches = 160, BeltSpeedInchesPerSecond = 200 },
            [
                new SimPrinterStation("Ship1", "Side", 1, 2, 40),
                new SimPrinterStation("Ship2", "Side", 1, 2, 40),
            ]);

        await sim.StartAsync();
        // Spawn three cartons in quick succession, exactly as an operator clicking "Spawn carton" 3x.
        await sim.SpawnCartonAsync();
        await sim.SpawnCartonAsync();
        await sim.SpawnCartonAsync();

        var printerByCarton = new Dictionary<string, string>();
        for (var i = 0; i < 400 && printerByCarton.Count < 3; i++)
        {
            await Task.Delay(10);
            var snap = await sim.GetSnapshotAsync();
            foreach (var carton in snap.Cartons)
            {
                foreach (var label in carton.Labels)
                {
                    printerByCarton[carton.BlindLabel] = label.PrinterId;
                }
            }
        }

        Assert.Equal(3, printerByCarton.Count);
        // Round-robin must spread the three cartons across both printers, not collapse onto one.
        var distinct = printerByCarton.Values.Distinct().Count();
        Assert.True(distinct == 2, $"expected cartons spread across both side printers, got: {string.Join(", ", printerByCarton.Select(kv => $"{kv.Key}->{kv.Value}"))}");
    }
}
