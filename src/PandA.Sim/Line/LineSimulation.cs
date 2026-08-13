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

            UpdateLabelStates(carton, eye);
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
            return;
        }

        // Intermediate eye: print the label onto the tamp head (its print eye) and/or fire the TAMP for
        // labels whose apply eye is this one. Each label transitions on its own printer's eyes.
        var appliedHere = UpdateLabelStates(carton, eye);
        if (appliedHere)
        {
            carton.State = SimCartonState.Applied;
            _lastEvent = $"{carton.BlindLabel} TAMP apply fired at {eye.Id}";
        }
    }

    /// <summary>
    /// Advances each label's own print/apply state as the carton crosses <paramref name="eye"/>: a label
    /// rides onto its printer's tamp head at its print eye, then is deposited on the carton at its apply
    /// eye. Returns true if any label was applied at this eye.
    /// </summary>
    private bool UpdateLabelStates(SimCarton carton, TrackingEye eye)
    {
        var appliedHere = false;
        carton.MutateLabels(label =>
        {
            var (printEye, applyEye) = PrinterEyeIds(label.PrinterId);
            var next = label;
            if (!next.Applied && !next.OnTamp && string.Equals(printEye, eye.Id, StringComparison.OrdinalIgnoreCase))
            {
                next = next with { OnTamp = true };
            }

            if (!next.Applied && string.Equals(applyEye, eye.Id, StringComparison.OrdinalIgnoreCase))
            {
                next = next with { Applied = true, OnTamp = false };
                appliedHere = true;
            }

            return next;
        });

        return appliedHere;
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
    /// Seats each configured printer at its apply tracking-eye. Printers that share an apply device
    /// cluster around that eye but are spread out side-by-side (never stacked), grouped by orientation.
    /// </summary>
    private IReadOnlyList<SimPrinterSnapshot> PrinterStations()
    {
        var topo = BuildTopology();
        var stations = _printers.Count > 0
            ? _printers
            : Enumerable.Range(0, Math.Max(1, _settings.PrinterCount))
                .Select(i => new SimPrinterStation($"printer-{i + 1}", "Side", ApplyTrackingDevice: 2))
                .ToList();

        return SeatPrinters(stations, topo.DeviceX);
    }

    /// <summary>
    /// Pure printer-seating: place each station at its apply-device X. When several printers share an
    /// apply device they are fanned out around that X on a fixed <paramref name="spacingInches"/> pitch,
    /// ordered so same-orientation printers stay contiguous — so a shared apply eye still renders as
    /// distinct, physically adjacent printers rather than one on top of another.
    /// </summary>
    public static IReadOnlyList<SimPrinterSnapshot> SeatPrinters(
        IReadOnlyList<SimPrinterStation> stations,
        IReadOnlyDictionary<int, double> applyDeviceX,
        double spacingInches = 26.0)
    {
        if (stations.Count == 0)
        {
            return [];
        }

        var fallbackX = applyDeviceX.Count > 0 ? applyDeviceX.Values.Average() : 0;
        var result = new List<SimPrinterSnapshot>(stations.Count);

        var clusters = stations
            .Select((p, i) => (Printer: p, Index: i))
            .GroupBy(t => Math.Max(1, t.Printer.ApplyTrackingDevice))
            .OrderBy(g => applyDeviceX.TryGetValue(g.Key, out var x) ? x : fallbackX);

        foreach (var cluster in clusters)
        {
            var baseX = applyDeviceX.TryGetValue(cluster.Key, out var x) ? x : fallbackX;
            var members = cluster
                .OrderBy(t => IsTop(t.Printer) ? 1 : 0)
                .ThenBy(t => t.Index)
                .Select(t => t.Printer)
                .ToList();

            var span = spacingInches * (members.Count - 1);
            var startX = baseX - span / 2.0;
            for (var i = 0; i < members.Count; i++)
            {
                result.Add(new SimPrinterSnapshot(
                    members[i].PrinterId,
                    startX + spacingInches * i,
                    IsTop(members[i]) ? "Top" : "Side"));
            }
        }

        return result.OrderBy(r => r.PositionInches).ToList();
    }

    private static bool IsTop(SimPrinterStation station) =>
        string.Equals(station.Orientation, "Top", StringComparison.OrdinalIgnoreCase);

    private double ResolvePrintPosition(FirePoint? firePoint)
    {
        if (firePoint is null)
        {
            return BuildEyes().First().PositionInches + 8;
        }

        var anchor = EyePosition(firePoint.PrintTrackingDevice);
        var pulseInches = Math.Max(0, firePoint.PrintFirePoint) * _settings.EncoderResolutionInchesPerPulse;
        return anchor + pulseInches + _settings.PrintFirePointOffsetInches;
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
        var topo = BuildTopology();
        if (topo.DeviceX.TryGetValue(Math.Max(1, trackingDevice), out var x))
        {
            return x;
        }

        return topo.Eyes.Count > 0 ? topo.Eyes[0].PositionInches : 0;
    }

    /// <summary>The (print-eye, apply-eye) ids a printer's labels are anchored to in the current topology.</summary>
    private (string PrintEyeId, string ApplyEyeId) PrinterEyeIds(string printerId)
    {
        var topo = BuildTopology();
        var station = _printers.FirstOrDefault(p => string.Equals(p.PrinterId, printerId, StringComparison.OrdinalIgnoreCase));
        var printDevice = Math.Max(1, station?.PrintTrackingDevice ?? 1);
        var applyDevice = Math.Max(1, station?.ApplyTrackingDevice ?? 2);
        var printEye = topo.DeviceEyeId.GetValueOrDefault(printDevice, "Inbound Scanner");
        var applyEye = topo.DeviceEyeId.GetValueOrDefault(applyDevice, printEye);
        return (printEye, applyEye);
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

    /// <summary>
    /// The line's tracking-eye topology, derived from the configured printers' fire-point devices:
    /// the inbound scanner (device 1), one eye per distinct print/apply tracking device the printers
    /// reference, then a verify scanner at the tail. Also yields device -> eye-id and device -> X maps
    /// so print/apply events and printer bodies can be anchored back to their eye.
    /// </summary>
    private Topology BuildTopology()
    {
        var applyDevices = _printers.Select(p => Math.Max(1, p.ApplyTrackingDevice)).ToHashSet();

        var devices = new SortedSet<int> { 1 };
        foreach (var p in _printers)
        {
            devices.Add(Math.Max(1, p.PrintTrackingDevice));
            devices.Add(Math.Max(1, p.ApplyTrackingDevice));
        }

        var ordered = devices.ToList();
        var first = Math.Min(48, ConveyorLengthInches * 0.18);
        var verifyX = Math.Max(first + 60, ConveyorLengthInches - 36);
        var zoneEnd = Math.Max(first + 24, verifyX - 40);
        var step = ordered.Count <= 1 ? 0 : (zoneEnd - first) / (ordered.Count - 1);

        var eyes = new List<TrackingEye>(ordered.Count + 1);
        var deviceX = new Dictionary<int, double>();
        var deviceEyeId = new Dictionary<int, string>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var device = ordered[i];
            var x = first + step * i;
            var id = device == 1
                ? "Inbound Scanner"
                : applyDevices.Contains(device) ? $"Apply Eye {device}" : $"Print Eye {device}";
            deviceX[device] = x;
            deviceEyeId[device] = id;
            eyes.Add(new TrackingEye(id, x));
        }

        eyes.Add(new TrackingEye("Verify Scanner", verifyX));
        return new Topology(eyes, deviceX, deviceEyeId, applyDevices);
    }

    private sealed record Topology(
        List<TrackingEye> Eyes,
        Dictionary<int, double> DeviceX,
        Dictionary<int, string> DeviceEyeId,
        HashSet<int> ApplyDevices);

    private List<TrackingEye> BuildEyes() => BuildTopology().Eyes;
}
