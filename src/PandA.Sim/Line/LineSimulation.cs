using PandA.Core;
using PandA.Core.Verification;

namespace PandA.Sim.Line;

/// <summary>
/// C#-authoritative kinematic line simulation. It moves cartons, raises tracking-eye events into the
/// real Core services, and exposes immutable snapshots for JavaScript rendering.
/// </summary>
public sealed class LineSimulation
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IInductService _induct;
    private readonly IVerifyStationService _verify;
    private readonly ITransportOrderStore _orders;
    private readonly CapturingPrinterGateway _printerGateway;
    private readonly SimClock _clock;
    private readonly List<SimCarton> _cartons = [];
    private readonly IReadOnlyList<SimPrinterStation> _printers;
    private readonly Dictionary<string, int> _seenPrintJobsByCarton = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _lastStepUtc = DateTimeOffset.UtcNow;
    private LineSimulationSettings _settings;
    private int _cartonSequence;
    private string _lastEvent = "Line idle";

    public LineSimulation(
        string lineId,
        string lineName,
        IInductService induct,
        IVerifyStationService verify,
        ITransportOrderStore orders,
        CapturingPrinterGateway printerGateway,
        SimClock clock,
        int verifyFailThreshold,
        LineSimulationSettings? settings = null,
        IReadOnlyList<SimPrinterStation>? printers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(lineName);
        LineId = lineId;
        LineName = lineName;
        _induct = induct ?? throw new ArgumentNullException(nameof(induct));
        _verify = verify ?? throw new ArgumentNullException(nameof(verify));
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        _printerGateway = printerGateway ?? throw new ArgumentNullException(nameof(printerGateway));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        VerifyFailThreshold = Math.Max(1, verifyFailThreshold);
        _settings = (settings ?? new LineSimulationSettings()).Normalize();
        _printers = printers ?? [];
    }

    public string LineId { get; }

    public string LineName { get; }

    public double ConveyorLengthInches => _settings.ConveyorLengthInches;

    public double BeltSpeedInchesPerSecond => _settings.BeltSpeedInchesPerSecond;

    public bool Running { get; private set; }

    public int VerifyFailThreshold { get; }

    public async ValueTask StartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Running = true;
            _lastStepUtc = DateTimeOffset.UtcNow;
            _lastEvent = "Line started";
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Running = false;
            _lastEvent = "Line stopped";
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SetBeltSpeedAsync(double inchesPerSecond)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _settings = _settings with { BeltSpeedInchesPerSecond = inchesPerSecond };
            _settings = _settings.Normalize();
            _lastEvent = $"Belt speed set to {BeltSpeedInchesPerSecond:0.#} in/s";
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<LineSimulationSettings> GetSettingsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return _settings;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<LineSimulationSnapshot> UpdateSettingsAsync(LineSimulationSettings settings)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _settings = settings.Normalize();
            _lastEvent = $"Sim settings applied: {_settings.TrackingEyeCount} eyes, encoder {_settings.EncoderResolutionInchesPerPulse:0.###} in/pulse";
            return BuildSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<LineSimulationSnapshot> SpawnCartonAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var sequence = ++_cartonSequence;
            var blind = $"SIM{sequence:0000}";
            var order = new TransportOrder(
                blind,
                LineId,
                new PandaLabelSet(
                [
                    new Label("Shipping", $"SHIP-SIM-{sequence:0000}", $"^XA^FO50,50^FDShipping {sequence}^FS^XZ"),
                    new Label("Content", $"CONT-SIM-{sequence:0000}", $"^XA^FO50,120^FDContent {sequence}^FS^XZ"),
                ]),
                _clock.UtcNow);
            order.SetAdviceMetadata($"SIM-WAVE-{sequence % 3 + 1}", "Line Simulation", verifyEnabled: true, verifyPassDest: "Ship Lane", verifyFailDest: "Reject Lane");
            await _orders.UpsertAsync(order).ConfigureAwait(false);

            _cartons.Add(new SimCarton($"Carton {sequence:000}", blind, order)
            {
                PositionInches = -_settings.CartonSpacingInches,
                PreviousPositionInches = -_settings.CartonSpacingInches,
                LengthInches = _settings.CartonLengthInches,
                WidthInches = _settings.CartonWidthInches,
                HeightInches = _settings.CartonHeightInches,
            });
            _lastEvent = $"Spawned {blind}";
            return BuildSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<LineSimulationSnapshot> ClearCartonsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _cartons.Clear();
            _seenPrintJobsByCarton.Clear();
            _lastEvent = "Line cleared";
            return BuildSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<LineSimulationSnapshot> GetSnapshotAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StepToNowAsync().ConfigureAwait(false);
            return BuildSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask StepToNowAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var dt = Math.Clamp((now - _lastStepUtc).TotalSeconds, 0, 0.12);
        _lastStepUtc = now;

        if (!Running || dt <= 0)
        {
            return;
        }

        _clock.Advance(TimeSpan.FromSeconds(dt));
        foreach (var carton in _cartons.ToList())
        {
            carton.PreviousPositionInches = carton.PositionInches;
            carton.PositionInches += BeltSpeedInchesPerSecond * dt;

            foreach (var eye in BuildEyes())
            {
                if (carton.HasCrossed(eye.Id) || !Crossed(carton, eye))
                {
                    continue;
                }

                carton.MarkCrossed(eye.Id);
                await OnTrackingEyeAsync(carton, eye).ConfigureAwait(false);
            }
        }

        _cartons.RemoveAll(c => c.PositionInches > ConveyorLengthInches + 40);
    }

    private static bool Crossed(SimCarton carton, TrackingEye eye) =>
        carton.PreviousPositionInches < eye.PositionInches && carton.PositionInches >= eye.PositionInches;

    private async ValueTask OnTrackingEyeAsync(SimCarton carton, TrackingEye eye)
    {
        var eyes = BuildEyes();
        var eyeIndex = eyes.FindIndex(e => string.Equals(e.Id, eye.Id, StringComparison.OrdinalIgnoreCase));
        if (eyeIndex == 0)
        {
            carton.State = SimCartonState.Scanned;
            _lastEvent = $"{carton.BlindLabel} crossed {eye.Id}; induct scan sent to Core";
            var result = await _induct.InductAsync(LineId, carton.BlindLabel).ConfigureAwait(false);
            if (result.Status is InductStatus.Printed or InductStatus.PartiallyPrinted)
            {
                carton.State = SimCartonState.Printed;
                AttachNewPrintJobs(carton);
                _lastEvent = $"{carton.BlindLabel} print decision: {result.Status}";
            }

            return;
        }

        if (eyeIndex > 0 && eyeIndex < eyes.Count - 1 && carton.State == SimCartonState.Printed)
        {
            carton.State = SimCartonState.Applied;
            carton.MarkApplied();
            _lastEvent = $"{carton.BlindLabel} TAMP apply fired at {eye.Id}";
            return;
        }

        if (eyeIndex == eyes.Count - 1 && carton.State is (SimCartonState.Printed or SimCartonState.Applied))
        {
            var scanned = carton.Order.Labels.Labels
                .Select(l => new ScannedLabel(l.LabelType, l.Lpn))
                .ToList();
            var result = await _verify.VerifyAsync(
                carton.BlindLabel,
                scanned,
                new VerifyOptions(VerifyEnabled: true, Bypass: false, VerifyContentLabel: true),
                VerifyFailThreshold).ConfigureAwait(false);
            carton.State = result.Status == VerifyStationStatus.Verified ? SimCartonState.Verified : SimCartonState.Rejected;
            _lastEvent = $"{carton.BlindLabel} verify decision: {result.Status}";
        }
    }

    private void AttachNewPrintJobs(SimCarton carton)
    {
        var jobs = _printerGateway.Jobs;
        var start = _seenPrintJobsByCarton.GetValueOrDefault(carton.BlindLabel);
        _seenPrintJobsByCarton[carton.BlindLabel] = jobs.Count;
        carton.ReplaceLabels(jobs.Skip(start).Select(job => ToPlacement(carton, job)));
    }

    private LabelPlacementSnapshot ToPlacement(SimCarton carton, PrintJob job)
    {
        var printX = ResolvePrintPosition(job.FirePoint);
        var (applyX, cartonOffset) = ResolveApplyPosition(carton, job.FirePoint);
        var orientation = OrientationFor(job.PrinterId);
        var top = string.Equals(orientation, "Top", StringComparison.OrdinalIgnoreCase);
        var y = top ? carton.HeightInches + 0.08 : carton.WidthInches / 2 + 0.08;

        return new LabelPlacementSnapshot(
            job.LabelType,
            job.PrinterId,
            job.Lpn,
            new SimPoint(printX, y, 0),
            new SimPoint(applyX, y, 0),
            cartonOffset,
            job.FirePoint?.ApplyFirePoint.ToString() ?? "n/a",
            top ? "Top" : "Side",
            Applied: false);
    }

    private string OrientationFor(string printerId) =>
        _printers.FirstOrDefault(p => string.Equals(p.PrinterId, printerId, StringComparison.OrdinalIgnoreCase))?.Orientation
        ?? "Side";

    /// <summary>
    /// Lays the configured printers out along the belt, centred on the printer-eye zone. Printers are
    /// grouped so same-orientation stations (all Side, all Top, ...) sit next to each other, and every
    /// printer gets a distinct X so none overlap in the 3D view.
    /// </summary>
    private IReadOnlyList<SimPrinterSnapshot> PrinterStations()
    {
        var eyes = BuildEyes();
        var zoneEyes = eyes.Skip(1).Take(Math.Max(0, eyes.Count - 2)).ToList();
        var centerX = zoneEyes.Count > 0
            ? zoneEyes.Average(e => e.PositionInches)
            : eyes[Math.Min(1, eyes.Count - 1)].PositionInches;

        var stations = _printers.Count > 0
            ? _printers
            : Enumerable.Range(0, Math.Max(1, _settings.PrinterCount))
                .Select(i => new SimPrinterStation($"printer-{i + 1}", "Side"))
                .ToList();

        return LayoutPrinters(stations, centerX);
    }

    /// <summary>
    /// Pure printer-bank layout: orders <paramref name="stations"/> so same-orientation printers are
    /// contiguous, then spreads them along X (centred on <paramref name="centerX"/>) with a fixed
    /// centre-to-centre <paramref name="spacingInches"/> so every station gets a distinct position.
    /// </summary>
    public static IReadOnlyList<SimPrinterSnapshot> LayoutPrinters(
        IReadOnlyList<SimPrinterStation> stations,
        double centerX,
        double spacingInches = 30.0)
    {
        if (stations.Count == 0)
        {
            return [];
        }

        var ordered = stations
            .Select((p, i) => (Printer: p, Index: i))
            .OrderBy(t => string.Equals(t.Printer.Orientation, "Top", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(t => t.Index)
            .Select(t => t.Printer)
            .ToList();

        var span = spacingInches * (ordered.Count - 1);
        var startX = centerX - span / 2.0;

        return ordered
            .Select((p, i) => new SimPrinterSnapshot(
                p.PrinterId,
                startX + spacingInches * i,
                string.Equals(p.Orientation, "Top", StringComparison.OrdinalIgnoreCase) ? "Top" : "Side"))
            .ToList();
    }

    private double ResolvePrintPosition(FirePoint? firePoint)
    {
        if (firePoint is null)
        {
            return BuildEyes().First().PositionInches + 8;
        }

        var anchor = EyePosition(firePoint.PrintTrackingDevice);
        return anchor + Math.Max(0, firePoint.PrintFirePoint) + _settings.PrintFirePointOffsetInches;
    }

    private (double PositionInches, double CartonOffsetInches) ResolveApplyPosition(SimCarton carton, FirePoint? firePoint)
    {
        if (firePoint is null)
        {
            var eyes = BuildEyes();
            return (eyes[Math.Min(1, eyes.Count - 1)].PositionInches + _settings.DefaultApplyDistanceInches, carton.LengthInches / 2);
        }

        var anchor = EyePosition(firePoint.ApplyTrackingDevice);
        var applyPoint = GetApplyPoint(firePoint);
        // Fire-point reference expressed as distance from the TRAILING (upstream) edge, matching the
        // source notation: nL = n from the leading edge, nT = n from the trailing edge, nM = n from centre
        // (positive toward leading). The renderer measures the ruler and label from this reference.
        var offset = applyPoint.Edge switch
        {
            Core.Edge.Leading => carton.LengthInches - ResolveDynamicApplyDistance(applyPoint),
            Core.Edge.Trailing => ResolveDynamicApplyDistance(applyPoint),
            _ => carton.LengthInches / 2 + ResolveDynamicApplyDistance(applyPoint),
        };

        offset += _settings.ApplyFirePointOffsetInches;
        return (anchor + offset, offset);
    }

    private ApplyPoint GetApplyPoint(FirePoint firePoint) => firePoint.ApplyFirePoint;

    private double ResolveDynamicApplyDistance(ApplyPoint applyPoint) =>
        (double)applyPoint.Inches;

    private double EyePosition(int trackingDevice)
    {
        var eyes = BuildEyes();
        var index = Math.Clamp(trackingDevice - 1, 0, eyes.Count - 1);
        return eyes[index].PositionInches;
    }

    private LineSimulationSnapshot BuildSnapshot()
    {
        var eyes = BuildEyes()
            .Select(eye => new TrackingEyeSnapshot(
                eye.Id,
                eye.PositionInches,
                _cartons.Any(c => Math.Abs(c.PositionInches - eye.PositionInches) < 3)))
            .ToList();

        var cartons = _cartons
            .Select(c => new SimCartonSnapshot(
                c.CartonId,
                c.BlindLabel,
                c.PositionInches,
                c.LengthInches,
                c.WidthInches,
                c.HeightInches,
                c.State.ToString(),
                c.Labels))
            .ToList();

        return new LineSimulationSnapshot(Running, BeltSpeedInchesPerSecond, ConveyorLengthInches, LineId, LineName, _settings, eyes, PrinterStations(), cartons, _lastEvent);
    }

    private List<TrackingEye> BuildEyes()
    {
        var count = _settings.TrackingEyeCount;
        var first = Math.Min(48, _settings.ConveyorLengthInches * 0.2);
        var last = Math.Max(first + 48, _settings.ConveyorLengthInches - 48);
        var step = count == 1 ? 0 : (last - first) / (count - 1);
        return Enumerable.Range(0, count)
            .Select(i => new TrackingEye(i == 0 ? "Inbound Scanner" : i == count - 1 ? "Verify Scanner" : $"Printer Eye {i}", first + step * i))
            .ToList();
    }
}
