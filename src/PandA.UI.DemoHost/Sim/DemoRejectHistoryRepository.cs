using System.Globalization;
using PandA.Core.Verification;

namespace PandA.UI.DemoHost.Sim;

/// <summary>
/// Demo adapter for the Core reject-history port: every non-pass verify from the simulator (matching source
/// <c>sdisp_PA_Scan_Verify</c> L117-132 → <c>sdisp_CUSTOM_RejectHistory_Insert</c>; owner ruling 2026-10-21)
/// lands on the shared <see cref="DemoDataStore"/> carton so the Reject Cartons page shows real sim rejects.
/// </summary>
public sealed class DemoRejectHistoryRepository(DemoDataStore store, string lineId, string profileName) : IRejectHistoryRepository
{
    public ValueTask AddAsync(RejectRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var tuId = record.TuId ?? record.PandaDataId.ToString(CultureInfo.InvariantCulture);
        var reason = string.Create(CultureInfo.InvariantCulture, $"{(int)record.Code} - {record.Reason}");

        var existing = store.Cartons.Values.FirstOrDefault(c =>
            string.Equals(c.BlindLabel, tuId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.CartonId, tuId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.RejectReason = reason;
            existing.RejectedUtc = record.RejectedAt;
            existing.VerifyResult = "Fail";
            existing.VerifiedUtc = record.RejectedAt;
            return ValueTask.CompletedTask;
        }

        var id = $"SIM-{tuId}";
        store.Cartons[id] = new DemoCarton
        {
            CartonId = id,
            BlindLabel = tuId,
            Upc = string.Empty,
            Gtin = string.Empty,
            Ean = string.Empty,
            CartonStatus = "Rejected",
            VerifyResult = "Fail",
            VerifiedUtc = record.RejectedAt,
            PrintedCount = 0,
            ProfileName = profileName,
            PassFailDestination = "Verify - FAIL",
            WaveId = string.Empty,
            RejectReason = reason,
            RejectedUtc = record.RejectedAt,
            LineId = record.LineId ?? lineId,
            Slots = [],
        };
        return ValueTask.CompletedTask;
    }
}
