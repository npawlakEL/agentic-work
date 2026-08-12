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

        if (!string.IsNullOrWhiteSpace(filter.ReasonContains))
        {
            q = q.Where(c => c.RejectReason.Contains(filter.ReasonContains, StringComparison.OrdinalIgnoreCase));
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
}
