using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using PandA.Core;
using PandA.Core.Settings;
using PandA.Core.Verification;
using PandA.Sim;
using PandA.Sim.Line;
using PandA.UI.Contracts.Config;

namespace PandA.UI.DemoHost.Sim;

internal static class LineSimulationFactory
{
    public static IReadOnlyList<(string Id, string Name)> Lines(DemoDataStore store) =>
        store.Lines.Values
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Select(l => (l.LineId!, l.Name))
            .ToList();

    public static LineSimulation Create(DemoDataStore store) => Create(store, null);

    public static LineSimulation Create(DemoDataStore store, string? lineId)
    {
        var line = lineId is null
            ? store.Lines.Values.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).First()
            : store.Lines.Values.First(l => string.Equals(l.LineId, lineId, StringComparison.OrdinalIgnoreCase));
        var printers = store.Printers.Values
            .Where(p => string.Equals(p.LineId, line.LineId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.ConfigOrder)
            .ToList();

        var profile = BuildProfile(store, line);
        var config = new LineConfig(
            line.LineId!,
            printers.Select(ToPrinterConfig),
            loadBalance: store.Settings.LoadBalanceEnabled,
            bufferOrder: new LabelBufferOrder(line.BufferOrder.Select((labelType, index) => new LabelBufferPosition(index + 1, labelType))),
            activeProfile: profile,
            encoderResolution: (decimal)store.Settings.EncoderResolutionInchesPerPulse);

        var lineProvider = new InMemoryLineProvider().Add(config, printers.Select(p =>
        {
            store.PrinterRuntime.TryGetValue(p.PrinterId!, out var runtime);
            return new PrinterState(
                p.PrinterId!,
                plcOnline: runtime?.Online ?? true,
                engineOnline: runtime?.Online ?? true,
                isSpare: runtime?.IsSpare ?? false,
                lastPrinted: runtime?.LastPrintedUtc,
                verifyFailCount: runtime?.VerifyFailCount ?? 0);
        }));

        var orders = new InMemoryTransportOrderStore();
        var gateway = new CapturingPrinterGateway();
        var clock = new SimClock(DateTimeOffset.UtcNow);
        ISettingsProvider settings = new InMemorySettingsProvider();
        var induct = new InductService(
            orders,
            lineProvider,
            new PrinterSelectionService(),
            gateway,
            clock,
            settings,
            logger: NullLogger<InductService>.Instance);
        var verify = new VerifyStationService(
            orders,
            new VerificationService(),
            new VerifyThresholdTracker(),
            clock,
            NullLogger<VerifyStationService>.Instance);

        var stationDevices = printers.ToDictionary(
            p => p.PrinterId!,
            p => ResolveStationDevices(store, line, p),
            StringComparer.OrdinalIgnoreCase);

        return new LineSimulation(
            line.LineId!,
            line.Name,
            induct,
            verify,
            orders,
            gateway,
            clock,
            store.Settings.VerifyFailThreshold,
            new LineSimulationSettings
            {
                TrackingEyeCount = Math.Max(3, line.TrackingDevices.Count),
                EncoderResolutionInchesPerPulse = store.Settings.EncoderResolutionInchesPerPulse,
                BeltSpeedInchesPerSecond = printers.FirstOrDefault()?.BeltSpeedInchesPerSecond ?? 24,
                DefaultApplyDistanceInches = 1,
                PrinterCount = Math.Max(1, printers.Count),
            },
            printers.Select(p =>
            {
                var (printDevice, applyDevice, printPulses) = stationDevices[p.PrinterId!];
                return new SimPrinterStation(
                    p.PrinterId!,
                    string.Equals(p.Orientation, "Top", StringComparison.OrdinalIgnoreCase) ? "Top" : "Side",
                    printDevice,
                    applyDevice,
                    printPulses);
            }).ToList());
    }

    /// <summary>Reads the printer's fire point from the line's active map to recover its print/apply tracking devices.</summary>
    private static (int PrintDevice, int ApplyDevice, int PrintPulses) ResolveStationDevices(DemoDataStore store, LineDto line, PrinterDto printer)
    {
        var fp = line.ActiveMapId is not null && store.Maps.TryGetValue(line.ActiveMapId, out var map)
            ? map.FirePointIds
                .Select(id => store.FirePoints.TryGetValue(id, out var f) ? f : null)
                .FirstOrDefault(f => f is not null && string.Equals(f.PrinterId, printer.PrinterId, StringComparison.OrdinalIgnoreCase))
            : null;

        if (fp is null)
        {
            return (1, 2, 0);
        }

        return (ParseTrackingDevice(fp.PrintTrackingDevice), ParseTrackingDevice(fp.ApplyTrackingDevice), Math.Max(0, fp.PrintPoint));
    }

    private static FirePointProfile? BuildProfile(DemoDataStore store, LineDto line)
    {
        if (line.ActiveMapId is null || !store.Maps.TryGetValue(line.ActiveMapId, out var map))
        {
            return null;
        }

        var firePoints = map.FirePointIds
            .Select(id => store.FirePoints.TryGetValue(id, out var fp) ? fp : null)
            .Where(fp => fp is not null)
            .Select(fp => ((fp!.PrinterId, fp.LabelType), ToFirePoint(fp)))
            .ToList();

        return firePoints.Count == 0 ? null : new FirePointProfile(map.Name, firePoints);
    }

    private static FirePoint ToFirePoint(FirePointDto fp) =>
        new(ParseTrackingDevice(fp.PrintTrackingDevice), fp.PrintPoint, ParseTrackingDevice(fp.ApplyTrackingDevice), ApplyPoint.Parse(fp.ApplyPointNotation));

    private static int ParseTrackingDevice(string trackingDevice)
    {
        var digits = new string(trackingDevice.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : 1;
    }

    private static PrinterConfig ToPrinterConfig(PrinterDto printer) =>
        new(
            printer.PrinterId!,
            printer.Ip,
            printer.Port,
            printer.LabelTypes,
            string.Equals(printer.Orientation, "Top", StringComparison.OrdinalIgnoreCase) ? ApplyOrientation.Top : ApplyOrientation.Side,
            printer.ConfigOrder);

}
