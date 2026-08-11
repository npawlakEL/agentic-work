using System.Text;
using PandA.Core;
using PandA.Core.Verification;
using PandA.Sim.Messaging;

namespace PandA.Sim;

/// <summary>
/// Bare-bones message simulation host: seeds a line + advised cartons on construction, then drives the
/// current end-to-end slice for one carton entirely through the message layer — inbound 281 (induct scan)
/// → print → inbound 286 (verify scan, auto-generated matching read) → Verified. Composes the real Core
/// services over the in-memory Sim adapters and returns a human-readable transcript.
/// <para>
/// Out of scope for this first pass (bookmarked): advanced data creation/editing, BluePaw tag writes,
/// fire points / tracking devices / apply-vs-print split, lane destination, sorter modes, and codes
/// 282–285. See the backlog.
/// </para>
/// </summary>
public sealed class SimHost
{
    private const string SourceLine = "L1";
    private const int SourceMode = 1;
    private const int SorterNumber = 1;
    private const int SorterMode = 1; // normal
    private const int InductDeviceId = 1;
    private const int VerifyDeviceId = 2;

    private static readonly DateTimeOffset SeedTime = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemoryTransportOrderStore _store = new();
    private readonly InMemoryLineProvider _lines = new();
    private readonly CapturingPrinterGateway _gateway = new();
    private readonly VerifyThresholdTracker _threshold = new();
    private readonly TestClock _clock = new(SeedTime);
    private readonly CartonAdviceService _advice;
    private readonly InductService _induct;
    private readonly VerifyStationService _verify;
    private readonly List<string> _blindLabels = [];

    public SimHost()
    {
        _advice = new CartonAdviceService(_store, _clock);
        _induct = new InductService(_store, _lines, new PrinterSelectionService(), _gateway, _clock);
        _verify = new VerifyStationService(_store, new VerificationService(), _threshold, _clock);
        Seed();
    }

    /// <summary>Blind labels of the seeded cartons, in seed order.</summary>
    public IReadOnlyList<string> BlindLabels => _blindLabels;

    /// <summary>Describe a seeded carton's advised data and current lifecycle state.</summary>
    public string Describe(string blindLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blindLabel);
        var order = _store.FindActiveByTuIdAsync(blindLabel).GetAwaiter().GetResult()
            ?? throw new ArgumentException($"No carton for blind label '{blindLabel}'.", nameof(blindLabel));

        var sb = new StringBuilder();
        sb.AppendLine($"Carton {order.TuId}  (line {order.LineId})");
        sb.AppendLine($"  status     : {order.Status}");
        sb.AppendLine($"  printCount : {order.PrintCount}");
        sb.AppendLine("  labels     :");
        foreach (var label in order.Labels.Labels)
        {
            sb.AppendLine($"    - {label.LabelType,-10} {label.Lpn}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Run the current slice for one carton: emit the 281 induct frame, print, emit the 286 verify frame
    /// (auto-generated to match the advised labels so it passes), and report the outcome.
    /// </summary>
    public async Task<IReadOnlyList<string>> RunAsync(string blindLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blindLabel);
        var order = await _store.FindActiveByTuIdAsync(blindLabel)
            ?? throw new ArgumentException($"No carton for blind label '{blindLabel}'.", nameof(blindLabel));

        var log = new List<string> { $"=== Running carton {blindLabel} ===" };

        // 1) Inbound 281 induct scan → print.
        var scan = new InductScanMessage(
            SourceMode, SorterNumber, SorterMode, InductDeviceId,
            SeqNum: order.PrintCount + 1, LabelStatus: 0, BlindLabel: blindLabel);
        var scanFrame = scan.ToFrame();
        log.Add($"  >> IN  281 PANDA_SCAN_INBOUND  {scanFrame.ToWire()}");

        // Re-parse the wire to prove the framing round-trips, then act on the parsed message.
        var parsedScan = InductScanMessage.FromFrame(PaFrame.Parse(scanFrame.ToWire()));
        var before = _gateway.Jobs.Count;
        var induct = await _induct.InductAsync(SourceLine, parsedScan.BlindLabel);
        log.Add($"     induct → {induct.Status}");

        foreach (var job in _gateway.Jobs.Skip(before))
        {
            log.Add($"  << OUT PRINT  {job.LabelType,-10} lpn={job.Lpn} → printer {job.PrinterId} ({job.Ip}:{job.Port})");
        }

        if (induct.Status != InductStatus.Printed)
        {
            log.Add($"  !! stopped: induct did not fully print ({induct.Status}).");
            return log;
        }

        // 2) Inbound 286 verify scan — auto-generate a matching read from the advised labels (happy path).
        var reloaded = await _store.FindActiveByTuIdAsync(blindLabel);
        var line = await _lines.GetLineAsync(SourceLine);
        var bufferOrder = line!.Config.BufferOrder;
        var rawBuffer = BuildMatchingBuffer(reloaded!, bufferOrder);
        var verifyMsg = new VerifyScanMessage(
            SourceMode, SorterNumber, SorterMode, VerifyDeviceId,
            SeqNum: parsedScan.SeqNum, LabelBuffer: rawBuffer);
        var verifyFrame = verifyMsg.ToFrame();
        log.Add($"  >> IN  286 PANDA_SCAN_VERIFY   {verifyFrame.ToWire()}");

        var parsedVerify = VerifyScanMessage.FromFrame(PaFrame.Parse(verifyFrame.ToWire()));
        var typed = bufferOrder.Type(parsedVerify.LabelBuffer);
        var verify = await _verify.VerifyAsync(blindLabel, typed, new VerifyOptions(), failThreshold: 3);
        log.Add($"     verify → {verify.Verify!.Outcome} ⇒ station {verify.Status}");

        var final = await _store.FindActiveByTuIdAsync(blindLabel);
        log.Add($"  == carton {blindLabel}: {final!.Status}, printCount={final.PrintCount} ==");
        return log;
    }

    /// <summary>
    /// Build a raw scanner buffer aligned to the line's <see cref="LabelBufferOrder"/>: each advised label
    /// is placed at its type's configured position; positions with no matching advised label are left empty
    /// (a scanner slot with nothing physically read). This mirrors a fixed-slot multi-head scanner.
    /// </summary>
    private static string[] BuildMatchingBuffer(TransportOrder order, LabelBufferOrder bufferOrder)
    {
        var buffer = new string[bufferOrder.MaxPosition];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = string.Empty;
        }

        foreach (var pos in bufferOrder.Positions)
        {
            var label = order.Labels.Labels.FirstOrDefault(
                l => string.Equals(l.LabelType, pos.LabelType, StringComparison.OrdinalIgnoreCase));
            if (label is not null)
            {
                buffer[pos.Position - 1] = label.Lpn;
            }
        }

        return buffer;
    }

    private void Seed()
    {
        _lines.Add(new LineConfig(SourceLine,
        [
            new PrinterConfig("Ship1", "10.0.0.11", 9100, ["Shipping"], ApplyOrientation.Side, 0),
            new PrinterConfig("Cont1", "10.0.0.12", 9100, ["Content"], ApplyOrientation.Side, 1),
            new PrinterConfig("Par1", "10.0.0.13", 9100, ["Parcel"], ApplyOrientation.Side, 2),
        ]));

        Advise("0154006001", ("Shipping", "SHIP-0154006001"), ("Content", "CONT-0154006001"));
        Advise("0154006002", ("Shipping", "SHIP-0154006002"), ("Parcel", "PARC-0154006002"));
        Advise("0154006003",
            ("Shipping", "SHIP-0154006003"), ("Content", "CONT-0154006003"), ("Parcel", "PARC-0154006003"));
    }

    private void Advise(string blindLabel, params (string Type, string Lpn)[] labels)
    {
        var set = new PandaLabelSet(labels.Select(l => new Label(l.Type, l.Lpn, $"^XA{l.Type}:{l.Lpn}^XZ")));
        _advice.AdviseAsync(SourceLine, blindLabel, set).GetAwaiter().GetResult();
        _blindLabels.Add(blindLabel);
    }
}
