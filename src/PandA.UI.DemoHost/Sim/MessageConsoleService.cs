using Microsoft.Extensions.Logging;
using PandA.Core;
using PandA.Core.Advice;
using PandA.Core.Settings;
using PandA.Core.Verification;
using PandA.Sim;
using PandA.Sim.Line;
using PandA.Sim.Messaging;
using PandA.UI.Contracts.Config;

namespace PandA.UI.DemoHost.Sim;

/// <summary>
/// Backs the non-visual Message Console page. Given a line + a seeded carton (barcode), it composes the
/// real Core services over fresh in-memory Sim adapters and drives the current end-to-end slice entirely
/// through the message layer — inbound 281 (induct scan) → print → inbound 286 (verify scan) → Verified —
/// while capturing every <see cref="ILogger"/> line the services emit so the operator can see what happened
/// under the hood without the 3D simulator.
/// </summary>
public sealed class MessageConsoleService
{
    private const int SourceMode = 1;
    private const int SorterNumber = 1;
    private const int SorterMode = 1; // normal
    private const int InductDeviceId = 1;
    private const int VerifyDeviceId = 2;

    private readonly DemoDataStore _store;

    public MessageConsoleService(DemoDataStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>Selectable lines, ordered by name.</summary>
    public IReadOnlyList<(string Id, string Name)> Lines() => LineSimulationFactory.Lines(_store);

    public string DefaultLineId => Lines().Count > 0 ? Lines()[0].Id : string.Empty;

    /// <summary>Barcodes (seeded cartons) available for a line. Falls back to all cartons when the line has none.</summary>
    public IReadOnlyList<ConsoleCartonSummary> CartonsForLine(string lineId)
    {
        var forLine = _store.Cartons.Values
            .Where(c => string.Equals(c.LineId, lineId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (forLine.Count == 0)
        {
            forLine = _store.Cartons.Values.ToList();
        }

        return forLine
            .OrderBy(c => c.CartonId, StringComparer.OrdinalIgnoreCase)
            .Select(c => new ConsoleCartonSummary(c.CartonId, c.BlindLabel, c.CartonStatus, c.Slots.Count))
            .ToList();
    }

    /// <summary>Full advised data the system holds for one barcode.</summary>
    public ConsoleCartonData? Describe(string cartonId)
    {
        if (cartonId is null || !_store.Cartons.TryGetValue(cartonId, out var c))
        {
            return null;
        }

        return new ConsoleCartonData(
            c.CartonId,
            c.BlindLabel,
            c.LineId,
            c.Upc,
            c.Gtin,
            c.Ean,
            c.ProfileName,
            c.WaveId,
            c.CartonStatus,
            c.PassFailDestination,
            c.Slots
                .OrderBy(s => s.Slot)
                .Select(s => new ConsoleLabelSlot(s.Slot, s.LabelType, s.LabelBarcode, s.Lpn, s.Zpl))
                .ToList());
    }

    /// <summary>
    /// Run the current end-to-end slice for one carton on the selected line, over a FRESH service set so
    /// every run is independent and reproducible. Returns the wire transcript, the per-step outcomes, and
    /// every captured log line.
    /// </summary>
    public async Task<MessageRunResult> RunAsync(string lineId, string cartonId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(cartonId);

        if (!_store.Cartons.TryGetValue(cartonId, out var carton))
        {
            return new MessageRunResult(cartonId, lineId, "Unknown carton", false, [], []);
        }

        var line = _store.Lines.Values.FirstOrDefault(l => string.Equals(l.LineId, lineId, StringComparison.OrdinalIgnoreCase));
        if (line is null)
        {
            return new MessageRunResult(cartonId, lineId, "Unknown line", false, [], []);
        }

        var blindLabel = carton.BlindLabel;
        var entries = new List<ConsoleEntry>();
        var sink = new ConsoleLogSink();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new SinkLoggerProvider(sink));
        });

        // Compose the real Core services over fresh in-memory adapters seeded from this line's config.
        var printers = _store.Printers.Values
            .Where(p => string.Equals(p.LineId, line.LineId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.ConfigOrder)
            .ToList();

        // The line's default fire-point profile (built from its active map). Register it by name so a
        // carton that advises a ProfileName resolves through the real named-profile lookup (PROFSW/F19)
        // instead of silently falling back — a carton requesting an unregistered name prints nothing.
        var activeProfile = LineSimulationFactory.BuildProfile(_store, line);
        var profileRegistry = activeProfile is { } prof
            ? new Dictionary<string, FirePointProfile>(StringComparer.OrdinalIgnoreCase) { [prof.Name] = prof }
            : null;

        var config = new LineConfig(
            line.LineId!,
            printers.Select(LineSimulationFactory.ToPrinterConfig),
            loadBalance: _store.Settings.LoadBalanceEnabled,
            bufferOrder: new LabelBufferOrder(line.BufferOrder.Select((labelType, index) => new LabelBufferPosition(index + 1, labelType))),
            activeProfile: activeProfile,
            encoderResolution: (decimal)_store.Settings.EncoderResolutionInchesPerPulse,
            profileRegistry: profileRegistry);

        var lineProvider = new InMemoryLineProvider().Add(config, printers.Select(p =>
        {
            _store.PrinterRuntime.TryGetValue(p.PrinterId!, out var runtime);
            // The console traces one message through a normally-staffed line: every configured printer is
            // treated as active (spares promoted) so each label type with a printer prints. Spare/lane-eval
            // behavior is exercised by the visual simulator, not this logic console.
            var online = runtime?.Online ?? true;
            return new PrinterState(
                p.PrinterId!,
                plcOnline: online,
                engineOnline: online,
                isSpare: false,
                lastPrinted: runtime?.LastPrintedUtc,
                verifyFailCount: runtime?.VerifyFailCount ?? 0);
        }));

        var orders = new InMemoryTransportOrderStore();
        var gateway = new CapturingPrinterGateway();
        var clock = new SimClock(DateTimeOffset.UtcNow);
        ISettingsProvider settings = new InMemorySettingsProvider();

        var advice = new CartonAdviceService(orders, clock, settings, logger: loggerFactory.CreateLogger<CartonAdviceService>());
        var induct = new InductService(
            orders,
            lineProvider,
            new PrinterSelectionService(),
            gateway,
            clock,
            settings,
            logger: loggerFactory.CreateLogger<InductService>());
        var verify = new VerifyStationService(
            orders,
            new VerificationService(),
            new VerifyThresholdTracker(),
            clock,
            loggerFactory.CreateLogger<VerifyStationService>());

        // 0) Advise — create the transport order from the barcode's advised label slots, carrying the
        // carton's real WaveId and ProfileName so the induct step resolves the named profile (not the
        // silent line default). Empty values stay null → induct falls back to the line's active profile.
        var labelSet = new PandaLabelSet(carton.Slots.Select(s => new Label(s.LabelType, s.Lpn, s.Zpl)));
        var waveId = string.IsNullOrWhiteSpace(carton.WaveId) ? null : carton.WaveId;
        var profileName = string.IsNullOrWhiteSpace(carton.ProfileName) ? null : carton.ProfileName;
        await advice.AdviseAsync(new AdviceMessage(line.LineId!, blindLabel, labelSet, WaveId: waveId, ProfileName: profileName));
        var profileLabel = profileName is null ? "line default" : profileName;
        entries.Add(new ConsoleEntry(ConsoleEntryKind.Info,
            $"Advised carton {blindLabel} on line {line.Name} with {labelSet.Labels.Count} label(s); wave {waveId ?? "(none)"}, profile {profileLabel}."));

        // 1) Inbound 281 induct scan → print.
        var scan = new InductScanMessage(SourceMode, SorterNumber, SorterMode, InductDeviceId, SeqNum: 1, LabelStatus: 0, BlindLabel: blindLabel);
        var scanWire = scan.ToFrame().ToWire();
        entries.Add(new ConsoleEntry(ConsoleEntryKind.InboundFrame, $"IN  281 PANDA_SCAN_INBOUND  {scanWire}"));

        var parsedScan = InductScanMessage.FromFrame(PaFrame.Parse(scanWire));
        var before = gateway.Jobs.Count;
        var inductResult = await induct.InductAsync(line.LineId!, parsedScan.BlindLabel);
        entries.Add(new ConsoleEntry(ConsoleEntryKind.StepResult, $"Induct → {inductResult.Status}"));

        var printedJobs = gateway.Jobs.Skip(before).ToList();
        foreach (var job in printedJobs)
        {
            var fire = job.FirePoint is { } f
                ? $"  fire[print dev{f.PrintTrackingDevice}@{f.PrintFirePoint}, apply dev{f.ApplyTrackingDevice}@{f.ApplyFirePoint}]"
                : string.Empty;
            var pulse = job.ApplyPulse is { } ap ? $"  applyPulse={ap}" : string.Empty;
            entries.Add(new ConsoleEntry(ConsoleEntryKind.OutboundPrint,
                $"OUT PRINT  {job.LabelType} lpn={job.Lpn} → printer {job.PrinterId} ({job.Ip}:{job.Port}){fire}{pulse}"));
        }

        if (inductResult.Status is not (InductStatus.Printed or InductStatus.PartiallyPrinted))
        {
            entries.Add(new ConsoleEntry(ConsoleEntryKind.Info, $"Stopped: nothing was printed ({inductResult.Status})."));
            var stopped = await orders.FindActiveByTuIdAsync(blindLabel);
            return new MessageRunResult(cartonId, line.LineId!, stopped?.Status.ToString() ?? inductResult.Status.ToString(), false, entries, sink.Lines);
        }

        if (inductResult.Status == InductStatus.PartiallyPrinted)
        {
            // A label had no eligible printer (e.g. its only printer is offline or held as a spare), so the
            // carton prints partially. The verify scan below reads only what physically printed, so the
            // missing label makes verify fail — the honest outcome.
            entries.Add(new ConsoleEntry(ConsoleEntryKind.Info, "Note: some labels had no eligible printer (offline/unavailable); the verify scan will read only what printed."));
        }

        // 2) Inbound 286 verify scan — the scanner reads only the labels that were physically printed.
        var rawBuffer = BuildScannedBuffer(printedJobs, config.BufferOrder);
        var verifyMsg = new VerifyScanMessage(SourceMode, SorterNumber, SorterMode, VerifyDeviceId, SeqNum: parsedScan.SeqNum, LabelBuffer: rawBuffer);
        var verifyWire = verifyMsg.ToFrame().ToWire();
        entries.Add(new ConsoleEntry(ConsoleEntryKind.InboundFrame, $"IN  286 PANDA_SCAN_VERIFY   {verifyWire}"));

        var parsedVerify = VerifyScanMessage.FromFrame(PaFrame.Parse(verifyWire));
        var typed = config.BufferOrder.Type(parsedVerify.LabelBuffer);
        var verifyResult = await verify.VerifyAsync(blindLabel, typed, new VerifyOptions(), failThreshold: _store.Settings.VerifyFailThreshold);
        entries.Add(new ConsoleEntry(ConsoleEntryKind.StepResult,
            $"Verify → {verifyResult.Verify!.Outcome} ⇒ station {verifyResult.Status}"));

        foreach (var detail in verifyResult.Verify!.Details)
        {
            var scanned = string.IsNullOrEmpty(detail.Scanned) ? "(no read)" : detail.Scanned;
            entries.Add(new ConsoleEntry(ConsoleEntryKind.Info,
                $"  {detail.LabelType}: {detail.Reason} (expected {detail.Expected ?? "-"}, scanned {scanned})"));
        }

        var final = await orders.FindActiveByTuIdAsync(blindLabel);
        var finalStatus = final!.Status.ToString();
        var success = verifyResult.Status == VerifyStationStatus.Verified;
        entries.Add(new ConsoleEntry(ConsoleEntryKind.Info, $"Carton {blindLabel}: {finalStatus}, printCount={final.PrintCount}"));

        return new MessageRunResult(cartonId, line.LineId!, finalStatus, success, entries, sink.Lines);
    }

    /// <summary>
    /// Build the raw verify-scanner buffer aligned to the line's <see cref="LabelBufferOrder"/> from the
    /// labels that were <em>physically printed</em>: each printed label's LPN is placed at its type's
    /// configured position; positions whose label never printed stay empty (a slot that read nothing). A
    /// partial print therefore yields a buffer missing that label, and verify fails — the honest outcome.
    /// </summary>
    private static string[] BuildScannedBuffer(IReadOnlyList<PrintJob> printed, LabelBufferOrder bufferOrder)
    {
        var buffer = new string[bufferOrder.MaxPosition];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = string.Empty;
        }

        foreach (var pos in bufferOrder.Positions)
        {
            var job = printed.FirstOrDefault(
                j => string.Equals(j.LabelType, pos.LabelType, StringComparison.OrdinalIgnoreCase));
            if (job is not null)
            {
                buffer[pos.Position - 1] = job.Lpn;
            }
        }

        return buffer;
    }
}

/// <summary>A barcode option in the console's carton dropdown.</summary>
public sealed record ConsoleCartonSummary(string CartonId, string BlindLabel, string Status, int LabelCount);

/// <summary>The advised data the system holds for a barcode, shown before running.</summary>
public sealed record ConsoleCartonData(
    string CartonId,
    string BlindLabel,
    string LineId,
    string Upc,
    string Gtin,
    string Ean,
    string ProfileName,
    string WaveId,
    string Status,
    string PassFailDestination,
    IReadOnlyList<ConsoleLabelSlot> Labels);

public sealed record ConsoleLabelSlot(int Slot, string LabelType, string LabelBarcode, string Lpn, string Zpl);

public enum ConsoleEntryKind
{
    Info,
    InboundFrame,
    OutboundPrint,
    StepResult,
}

/// <summary>One line in the run transcript.</summary>
public sealed record ConsoleEntry(ConsoleEntryKind Kind, string Text);

/// <summary>One captured <see cref="ILogger"/> line emitted during a run.</summary>
public sealed record ConsoleLogLine(string Level, string Category, string Message);

/// <summary>Outcome of an end-to-end console run.</summary>
public sealed record MessageRunResult(
    string CartonId,
    string LineId,
    string FinalStatus,
    bool Success,
    IReadOnlyList<ConsoleEntry> Entries,
    IReadOnlyList<ConsoleLogLine> Logs);
