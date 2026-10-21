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

    /// <summary>
    /// Build a line simulation composed over <paramref name="database"/> — the simulated shared eController
    /// database (decision-010). Every line (and every simulated host) built over the same instance shares one
    /// order table, run-id sequence, slot sequence, lease table, printer-state table and verify-fail counters.
    /// Omitting it gives the simulation a private database.
    /// </summary>
    public static LineSimulation Create(DemoDataStore store, string? lineId, SimSharedDatabase? database = null)
    {
        var db = database ?? new SimSharedDatabase();
        var line = (lineId is null
            ? store.Lines.Values.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
            : store.Lines.Values.FirstOrDefault(l => string.Equals(l.LineId, lineId, StringComparison.OrdinalIgnoreCase)))
            ?? throw new KeyNotFoundException(lineId is null ? "No lines are configured." : $"Line '{lineId}' does not exist.");
        var printers = store.Printers.Values
            .Where(p => string.Equals(p.LineId, line.LineId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.PlcNumber)
            .ToList();

        var profile = BuildProfile(store, line);
        var config = new LineConfig(
            line.LineId!,
            printers.Select(p => ToPrinterConfig(store, line, p)),
            loadBalance: line.LoadBalanceEnabled,
            bufferOrder: new LabelBufferOrder(line.BufferOrder.Select((labelType, index) => new LabelBufferPosition(index + 1, labelType))),
            activeProfile: profile,
            encoderResolution: (decimal)line.EncoderResolutionInchesPerPulse,
            twoPrinterRule: line.TwoPrinterRuleEnabled,
            plcNumber: line.PlcNumber);

        var lineProvider = new InMemoryLineProvider(db.PrinterStates).Add(config, printers.Select(p =>
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

        var orders = db.Orders;
        var gateway = new CapturingPrinterGateway();
        // Reflect real sim prints on the dashboard: stamp the printer that actually printed.
        gateway.JobSent += job =>
        {
            if (store.PrinterRuntime.TryGetValue(job.PrinterId, out var rt))
            {
                rt.LastPrintedUtc = DateTimeOffset.UtcNow;
            }
        };
        var clock = new SimClock(DateTimeOffset.UtcNow);
        ISettingsProvider settings = new InMemorySettingsProvider();
        // Synchronous seed of an in-memory provider (completes inline).
        settings.SetAsync(KnownSettings.ReprintLabels, store.Settings.ReprintLabelsEnabled).AsTask().GetAwaiter().GetResult();
        var induct = new InductService(
            orders,
            lineProvider,
            new PrinterSelectionService(),
            gateway,
            clock,
            settings,
            db.PrinterStates,
            db.Locks,
            db.Slots,
            db.Runs,
            logger: NullLogger<InductService>.Instance);
        var verify = new VerifyStationService(
            orders,
            new VerificationService(),
            db.CreateThresholdTracker(),
            clock,
            db.Locks,
            settings,
            NullLogger<VerifyStationService>.Instance,
            new DemoRejectHistoryRepository(store, line.LineId!, profile?.Name ?? string.Empty));

        var stationDevices = printers.ToDictionary(
            p => p.PrinterId!,
            ResolveStationDevices,
            StringComparer.OrdinalIgnoreCase);

        // Tracking eyes are inferred from the printers' print + apply devices (plus the induct scanner),
        // never a hand-entered line field. Floor at 3 (print eye, apply eye, scanner).
        var trackingEyeCount = Math.Max(3, printers
            .SelectMany(p => new[] { ParseTrackingDevice(p.PrintDevice), ParseTrackingDevice(p.ApplyDevice) })
            .Distinct()
            .Count());

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
                TrackingEyeCount = trackingEyeCount,
                EncoderResolutionInchesPerPulse = line.EncoderResolutionInchesPerPulse,
                BeltSpeedInchesPerSecond = line.BeltSpeedInchesPerSecond,
                DefaultApplyDistanceInches = 1,
                PrinterCount = Math.Max(1, printers.Count),
            },
            printers.Select(p =>
            {
                var (printDevice, applyDevice, printPulses) = stationDevices[p.PrinterId!];
                return new SimPrinterStation(
                    p.PrinterId!,
                    MotionString(store, p),
                    printDevice,
                    applyDevice,
                    printPulses);
            }).ToList());
    }

    /// <summary>Reads the printer's own print/apply devices and static print point.</summary>
    private static (int PrintDevice, int ApplyDevice, int PrintPulses) ResolveStationDevices(PrinterDto printer) =>
        (ParseTrackingDevice(printer.PrintDevice), ParseTrackingDevice(printer.ApplyDevice), Math.Max(0, printer.PrintPoint));

    internal static FirePointProfile? BuildProfile(DemoDataStore store, LineDto line)
    {
        if (line.ActiveMapId is null || !store.Maps.TryGetValue(line.ActiveMapId, out var map))
        {
            return null;
        }

        var firePoints = new List<((string PrinterId, string LabelType) Key, FirePoint FirePoint)>();
        foreach (var assignment in map.Assignments)
        {
            if (!store.FirePoints.TryGetValue(assignment.FirePointId, out var fp))
            {
                continue;
            }

            var labelType = LabelTypeName(store, fp.LabelDefId);
            foreach (var printerId in assignment.PrinterIds)
            {
                if (!store.Printers.TryGetValue(printerId, out var printer))
                {
                    continue;
                }

                // A malformed fire point (e.g. stored before validation existed) is skipped rather than
                // throwing into the Blazor circuit that is building the simulation.
                if (TryToFirePoint(printer, fp) is { } firePoint)
                {
                    firePoints.Add(((printerId, labelType), firePoint));
                }
            }
        }

        return firePoints.Count == 0 ? null : new FirePointProfile(map.Name, firePoints);
    }

    private static FirePoint? TryToFirePoint(PrinterDto printer, FirePointDto fp)
    {
        try
        {
            return new(ParseTrackingDevice(printer.PrintDevice), printer.PrintPoint, ParseTrackingDevice(printer.ApplyDevice), ApplyPoint.Parse(fp.ApplyPointNotation));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    private static string LabelTypeName(DemoDataStore store, string labelDefId) =>
        store.LabelDefs.TryGetValue(labelDefId, out var def) ? def.Name : labelDefId;

    /// <summary>Sim applicator motion string for a printer, resolved from its orientation entity's motion kind.</summary>
    private static string MotionString(DemoDataStore store, PrinterDto printer)
    {
        var kind = store.Orientations.TryGetValue(printer.OrientationId, out var o) ? o.MotionKind : ApplyMotionKind.Side;
        return kind == ApplyMotionKind.Top ? "Top" : "Side"; // Front maps to the closest existing motion for now.
    }

    internal static int ParseTrackingDevice(string trackingDevice)
    {
        var digits = new string(trackingDevice.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : 1;
    }

    internal static PrinterConfig ToPrinterConfig(DemoDataStore store, LineDto line, PrinterDto printer)
    {
        var kind = store.Orientations.TryGetValue(printer.OrientationId, out var o) ? o.MotionKind : ApplyMotionKind.Side;
        var orientation = kind == ApplyMotionKind.Top ? ApplyOrientation.Top : ApplyOrientation.Side;
        return new(
            printer.PrinterId!,
            printer.Ip,
            printer.Port,
            LabelTypesForPrinter(store, line, printer.PrinterId!),
            orientation,
            printer.PlcNumber);
    }

    /// <summary>
    /// A printer's printable label types are derived from the line's active map: the label definition
    /// of every fire point assigned to that printer. Printers no longer own a label-type list.
    /// </summary>
    internal static IReadOnlyList<string> LabelTypesForPrinter(DemoDataStore store, LineDto line, string printerId)
    {
        if (line.ActiveMapId is null || !store.Maps.TryGetValue(line.ActiveMapId, out var map))
        {
            return [];
        }

        return map.Assignments
            .Where(a => a.PrinterIds.Contains(printerId, StringComparer.OrdinalIgnoreCase))
            .Select(a => store.FirePoints.TryGetValue(a.FirePointId, out var fp) ? LabelTypeName(store, fp.LabelDefId) : null)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
