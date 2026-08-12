using PandA.UI.Contracts.Common;

namespace PandA.UI.Contracts.Rejects;

/// <summary>A rejected carton shown in the Reject Cartons list.</summary>
/// <param name="CartonId">Carton (transport-order) identifier.</param>
/// <param name="BlindLabel">Blind/tracking label.</param>
/// <param name="RejectReason">Why the carton was rejected.</param>
/// <param name="RejectedUtc">When it was rejected.</param>
/// <param name="LineId">Owning line.</param>
/// <param name="ProfileName">Active profile/map at rejection.</param>
/// <param name="PrintedCount">Monotonic print count.</param>
public sealed record RejectCartonRow(
    string CartonId,
    string BlindLabel,
    string RejectReason,
    DateTimeOffset RejectedUtc,
    string LineId,
    string ProfileName,
    int PrintedCount);

/// <summary>Filter for the Reject Cartons list.</summary>
/// <param name="FromUtc">Rejection window start.</param>
/// <param name="ToUtc">Rejection window end.</param>
/// <param name="LineId">Restrict to a line.</param>
/// <param name="ReasonContains">Free-text match on the reject reason.</param>
/// <param name="Page">Zero-based page index.</param>
/// <param name="PageSize">Page size.</param>
public sealed record RejectCartonFilter(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? LineId = null,
    string? ReasonContains = null,
    int Page = 0,
    int PageSize = 100);

/// <summary>Filtered, paged query over rejected cartons.</summary>
public interface IRejectCartonQuery
{
    Task<PagedResult<RejectCartonRow>> QueryAsync(RejectCartonFilter filter, CancellationToken ct = default);
}
