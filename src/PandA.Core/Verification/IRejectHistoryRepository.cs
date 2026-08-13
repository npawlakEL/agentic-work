namespace PandA.Core.Verification;

public interface IRejectHistoryRepository
{
    ValueTask AddAsync(RejectRecord record, CancellationToken cancellationToken = default);
}
