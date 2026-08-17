using PandA.UI.Contracts.Common;
using PandA.UI.Contracts.Rejects;

namespace PandA.UI.DemoHost.Sim;

/// <summary>Sim implementation of the Reject Cartons query.</summary>
public sealed class DemoRejectCartonQuery(DemoDataStore store) : IRejectCartonQuery
{
    public Task<PagedResult<RejectCartonRow>> QueryAsync(RejectCartonFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IEnumerable<DemoCarton> q = store.Cartons.Values.Where(c => c.RejectedUtc is not null);

        if (filter.FromUtc is { } from)
        {
            q = q.Where(c => c.RejectedUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            q = q.Where(c => c.RejectedUtc <= to);
        }

        if (!string.IsNullOrWhiteSpace(filter.LineId))
        {
            q = q.Where(c => string.Equals(c.LineId, filter.LineId, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Reasons is { Count: > 0 } reasons)
        {
            var set = new HashSet<string>(reasons, StringComparer.OrdinalIgnoreCase);
            q = q.Where(c => set.Contains(c.RejectReason));
        }

        var ordered = q.OrderByDescending(c => c.RejectedUtc).ToList();
        var total = ordered.Count;
        var page = ordered.Skip(filter.Page * filter.PageSize).Take(filter.PageSize)
            .Select(c => new RejectCartonRow(
                c.CartonId,
                c.BlindLabel,
                c.RejectReason,
                c.RejectedUtc!.Value,
                c.LineId,
                c.ProfileName,
                c.PrintedCount))
            .ToList();

        return Task.FromResult(new PagedResult<RejectCartonRow>(page, total, filter.Page, filter.PageSize));
    }

    public Task<IReadOnlyList<string>> GetReasonsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(store.Cartons.Values
            .Where(c => c.RejectedUtc is not null && !string.IsNullOrWhiteSpace(c.RejectReason))
            .Select(c => c.RejectReason)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList());
}
