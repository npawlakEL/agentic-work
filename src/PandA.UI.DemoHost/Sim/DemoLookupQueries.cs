using PandA.UI.Contracts.Common;
using PandA.UI.Contracts.Lookup;

namespace PandA.UI.DemoHost.Sim;

/// <summary>Sim implementation of the Label Data Lookup queries.</summary>
public sealed class DemoLookupQueries(DemoDataStore store) : ITransportOrderQuery, ICartonLabelDetailQuery
{
    public Task<PagedResult<TransportOrderRow>> QueryAsync(TransportOrderFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IEnumerable<DemoCarton> q = store.Cartons.Values;

        if (!string.IsNullOrWhiteSpace(filter.FuzzyText))
        {
            var text = filter.FuzzyText.Trim();
            q = q.Where(c =>
                Contains(c.BlindLabel, text) ||
                Contains(c.Upc, text) ||
                Contains(c.Gtin, text) ||
                Contains(c.Ean, text) ||
                c.Slots.Any(s => Contains(s.LabelBarcode, text)));
        }

        if (!string.IsNullOrWhiteSpace(filter.WaveId))
        {
            q = q.Where(c => string.Equals(c.WaveId, filter.WaveId, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.CreatedFromUtc is { } from)
        {
            q = q.Where(c => (c.VerifiedUtc ?? DateTimeOffset.MaxValue) >= from);
        }

        if (filter.CreatedToUtc is { } to)
        {
            q = q.Where(c => (c.VerifiedUtc ?? DateTimeOffset.MinValue) <= to);
        }

        if (!string.IsNullOrWhiteSpace(filter.CartonStatus))
        {
            q = q.Where(c => string.Equals(c.CartonStatus, filter.CartonStatus, StringComparison.OrdinalIgnoreCase));
        }

        q = filter.VerifyState switch
        {
            VerifyStateFilter.Passed => q.Where(c => string.Equals(c.VerifyResult, "Pass", StringComparison.OrdinalIgnoreCase)),
            VerifyStateFilter.Failed => q.Where(c => string.Equals(c.VerifyResult, "Fail", StringComparison.OrdinalIgnoreCase)),
            VerifyStateFilter.Enabled => q.Where(c => !string.IsNullOrEmpty(c.VerifyResult)),
            _ => q,
        };

        if (filter.PrintedOnly)
        {
            q = q.Where(c => c.PrintedCount > 0);
        }

        if (!string.IsNullOrWhiteSpace(filter.ProfileName))
        {
            q = q.Where(c => string.Equals(c.ProfileName, filter.ProfileName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.LineId))
        {
            q = q.Where(c => string.Equals(c.LineId, filter.LineId, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = q.OrderBy(c => c.CartonId, StringComparer.Ordinal).ToList();
        var total = ordered.Count;
        var page = ordered.Skip(filter.Page * filter.PageSize).Take(filter.PageSize)
            .Select(ToRow).ToList();

        return Task.FromResult(new PagedResult<TransportOrderRow>(page, total, filter.Page, filter.PageSize));
    }

    public Task<LookupFilterOptions> GetFilterOptionsAsync(CancellationToken ct = default)
    {
        var cartons = store.Cartons.Values.ToList();
        var options = new LookupFilterOptions(
            CartonStatuses: cartons.Select(c => c.CartonStatus).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Ordinal).ToList(),
            WaveIds: cartons.Select(c => c.WaveId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Ordinal).ToList(),
            ProfileNames: cartons.Select(c => c.ProfileName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.Ordinal).ToList(),
            LineIds: store.Lines.Values.Select(l => l.LineId!).OrderBy(s => s, StringComparer.Ordinal).ToList(),
            PrinterIds: store.Printers.Values.Select(p => p.PrinterId!).OrderBy(s => s, StringComparer.Ordinal).ToList());

        return Task.FromResult(options);
    }

    public Task<IReadOnlyList<CartonLabelSlot>> GetLabelSlotsAsync(string cartonId, CancellationToken ct = default)
    {
        IReadOnlyList<CartonLabelSlot> slots = store.Cartons.TryGetValue(cartonId, out var carton)
            ? carton.Slots
            : [];
        return Task.FromResult(slots);
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static TransportOrderRow ToRow(DemoCarton c) => new(
        c.CartonId,
        c.BlindLabel,
        c.CartonStatus,
        c.VerifyResult,
        c.VerifiedUtc,
        c.PrintedCount,
        c.ProfileName,
        c.PassFailDestination,
        c.WaveId,
        c.IsHeld,
        c.LineId);
}
