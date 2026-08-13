using System.Collections.Concurrent;
using PandA.UI.Contracts.Config;
using PandA.UI.Contracts.Lookup;
using PandA.UI.Contracts.Manda;
using PandA.UI.Contracts.Rejects;

namespace PandA.UI.DemoHost.Sim;

/// <summary>
/// Single shared in-memory data store seeded with representative PandA data, backing every
/// Sim implementation of the UI contracts. Registered as a singleton so edits on the Config
/// Explorer are visible to the other screens within a running demo host.
/// </summary>
public sealed class DemoDataStore
{
    public SettingsDto Settings { get; set; } = new(
        EncoderResolutionInchesPerPulse: 0.25,
        ReprintLabelsEnabled: true,
        LoadBalanceEnabled: true,
        TwoPrinterRuleEnabled: false,
        VerifyFailThreshold: 3,
        PostTripResetCount: 5);

    public ConcurrentDictionary<string, LabelDefDto> LabelDefs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, MandaStationDto> Stations { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, LineDto> Lines { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, PrinterDto> Printers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, FirePointDto> FirePoints { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, MapDto> Maps { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentDictionary<string, DemoCarton> Cartons { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, DemoPrinterRuntime> PrinterRuntime { get; } = new(StringComparer.OrdinalIgnoreCase);

    private int _idSeq = 1000;

    public string NextId(string prefix) => $"{prefix}-{Interlocked.Increment(ref _idSeq)}";

    public DemoDataStore()
    {
        Seed();
    }

    private void Seed()
    {
        // Label definitions
        foreach (var (name, orient, pos) in new[]
                 {
                     ("Shipping", "Side", 1),
                     ("Content", "Top", 0),
                     ("Return", "Side", 2),
                 })
        {
            var id = NextId("label");
            LabelDefs[id] = new LabelDefDto(id, name, orient, pos);
        }

        // MandA stations
        foreach (var (name, ip, port) in new[]
                 {
                     ("MandA Pack-1", "10.20.0.51", 9100),
                     ("MandA Pack-2", "10.20.0.52", 9100),
                 })
        {
            var id = NextId("manda");
            Stations[id] = new MandaStationDto(id, name, ip, port);
        }

        // Lines + printers + fire points + maps
        SeedLine("Line 1", "L1", ["Ship1", "Ship2", "Cont1"]);
        SeedLine("Line 2", "L2", ["Ship3", "Cont2"]);

        // Transport orders (cartons)
        SeedCartons();
    }

    private void SeedLine(string name, string tag, string[] printerNames)
    {
        var lineId = NextId("line");
        var mapId = NextId("map");

        var printerIds = new List<string>();
        var firePointIds = new List<string>();

        for (var i = 0; i < printerNames.Length; i++)
        {
            var pName = printerNames[i];
            var orientation = pName.StartsWith("Cont", StringComparison.OrdinalIgnoreCase) ? "Top" : "Side";
            var printerId = NextId("printer");
            printerIds.Add(printerId);

            Printers[printerId] = new PrinterDto(
                PrinterId: printerId,
                LineId: lineId,
                Name: $"{tag}-{pName}",
                Ip: $"10.10.{(tag == "L1" ? 1 : 2)}.{10 + i}",
                Port: 9100,
                Orientation: orientation,
                LabelTypes: orientation == "Top" ? ["Content"] : ["Shipping", "Return"],
                ConfigOrder: i,
                EncoderResolutionInchesPerPulse: 0.25,
                LabelWidthInches: 4,
                SpareEligible: i == printerNames.Length - 1,
                TampMountHeightInches: 12,
                BeltSpeedInchesPerSecond: 24,
                TampSpeedInchesPerSecond: 30);

            var labelType = orientation == "Top" ? "Content" : "Shipping";
            var fpId = NextId("fp");
            firePointIds.Add(fpId);
            FirePoints[fpId] = new FirePointDto(
                FirePointId: fpId,
                PrinterId: printerId,
                LabelType: labelType,
                PrintTrackingDevice: "TD1",
                PrintPoint: orientation == "Top" ? 60 : 40,
                ApplyTrackingDevice: orientation == "Top" ? "TD3" : "TD2",
                ApplyPointNotation: orientation == "Top" ? "0M" : "1T",
                DynamicApply: orientation == "Top");

            PrinterRuntime[printerId] = new DemoPrinterRuntime
            {
                Online = true,
                IsSpare = i == printerNames.Length - 1,
                VerifyFailCount = 0,
                LastPrintedUtc = DateTimeOffset.UtcNow.AddMinutes(-i),
            };
        }

        Maps[mapId] = new MapDto(mapId, lineId, $"{tag} Default Map", firePointIds);

        Lines[lineId] = new LineDto(
            LineId: lineId,
            Name: name,
            Zones: ["Z1"],
            TrackingDevices: ["TD1", "TD2", "TD3"],
            BufferOrder: ["Shipping", "Content", "Return"],
            ActiveMapId: mapId,
            ControlPolicy: LineControlPolicy.AllowDegraded,
            OnlineMinimum: 1);
    }

    private void SeedCartons()
    {
        var lineId = Lines.Values.First().LineId!;
        var mapName = Maps.Values.First().Name;
        var statuses = new[] { "Inducted", "Printed", "Verified", "Held", "Rejected" };
        var reasons = new[] { "", "", "", "Verify failed", "No read at apply" };

        for (var i = 0; i < 12; i++)
        {
            var id = $"CTN{1000 + i}";
            var status = statuses[i % statuses.Length];
            var held = string.Equals(status, "Held", StringComparison.Ordinal);
            var rejected = string.Equals(status, "Rejected", StringComparison.Ordinal);

            var slots = new List<CartonLabelSlot>
            {
                new(1, "Shipping", $"SHIP{9000 + i}", $"LPN{5000 + i}", $"^XA^FO50,50^A0N,40,40^FDShipping {i}^FS^XZ", i % 5 != 0),
                new(2, "Content", $"CONT{9000 + i}", $"LPN{5000 + i}", $"^XA^FO50,50^A0N,40,40^FDContent {i}^FS^XZ", i % 5 != 0),
            };

            Cartons[id] = new DemoCarton
            {
                CartonId = id,
                BlindLabel = $"BLIND{7000 + i}",
                Upc = $"01234500{i:D4}",
                Gtin = $"1001234500{i:D4}",
                Ean = $"400123450{i:D4}",
                CartonStatus = status,
                VerifyResult = string.Equals(status, "Verified", StringComparison.Ordinal) ? "Pass"
                    : (held || rejected ? "Fail" : ""),
                VerifiedUtc = string.Equals(status, "Inducted", StringComparison.Ordinal) ? null : DateTimeOffset.UtcNow.AddMinutes(-i * 3),
                PrintedCount = i % 5 == 0 ? 0 : 1,
                ProfileName = mapName,
                PassFailDestination = rejected ? "Reject Lane" : "Ship Lane",
                WaveId = $"W{100 + (i % 3)}",
                IsHeld = held,
                RejectReason = reasons[i % reasons.Length],
                RejectedUtc = rejected ? DateTimeOffset.UtcNow.AddMinutes(-i * 3) : null,
                LineId = lineId,
                Slots = slots,
            };
        }

        // Two Side-only cartons (Shipping + Return): every label maps to a Side printer, so they fully
        // print and verify clean — the console's green end-to-end sample alongside the Top-label cartons
        // above, which print partially (Content is a Top apply the Phase-1 induct path can't print yet).
        for (var j = 0; j < 2; j++)
        {
            var id = $"CTN{2000 + j}";
            Cartons[id] = new DemoCarton
            {
                CartonId = id,
                BlindLabel = $"BLIND{7200 + j}",
                Upc = $"09876500{j:D4}",
                Gtin = $"1009876500{j:D4}",
                Ean = $"400987650{j:D4}",
                CartonStatus = "Inducted",
                VerifyResult = "",
                VerifiedUtc = null,
                PrintedCount = 0,
                ProfileName = mapName,
                PassFailDestination = "Ship Lane",
                WaveId = $"W{200 + j}",
                IsHeld = false,
                RejectReason = "",
                RejectedUtc = null,
                LineId = lineId,
                Slots =
                [
                    new(1, "Shipping", $"SHIP{9200 + j}", $"LPN{5200 + j}", $"^XA^FO50,50^A0N,40,40^FDShipping {j}^FS^XZ", false),
                    new(2, "Return", $"RTRN{9200 + j}", $"LPN{5220 + j}", $"^XA^FO50,50^A0N,40,40^FDReturn {j}^FS^XZ", false),
                ],
            };
        }
    }
}

/// <summary>Mutable demo carton aggregate spanning the lookup + reject + manda screens.</summary>
public sealed class DemoCarton
{
    public required string CartonId { get; init; }
    public required string BlindLabel { get; init; }
    public required string Upc { get; init; }
    public required string Gtin { get; init; }
    public required string Ean { get; init; }
    public required string CartonStatus { get; set; }
    public required string VerifyResult { get; set; }
    public DateTimeOffset? VerifiedUtc { get; set; }
    public int PrintedCount { get; set; }
    public required string ProfileName { get; init; }
    public required string PassFailDestination { get; init; }
    public required string WaveId { get; init; }
    public bool IsHeld { get; set; }
    public required string RejectReason { get; set; }
    public DateTimeOffset? RejectedUtc { get; set; }
    public required string LineId { get; init; }
    public required List<CartonLabelSlot> Slots { get; init; }
}

/// <summary>Mutable per-printer runtime state derived onto <see cref="Contracts.Status.PrinterStatus"/>.</summary>
public sealed class DemoPrinterRuntime
{
    public bool Online { get; set; }
    public bool IsSpare { get; set; }
    public int VerifyFailCount { get; set; }
    public DateTimeOffset? LastPrintedUtc { get; set; }
}
