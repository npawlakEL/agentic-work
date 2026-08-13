namespace PandA.Core.Advice;

public interface IHostAdviceAcknowledger
{
    ValueTask AckAsync(long recId, string blindLabel, bool success, CancellationToken cancellationToken = default);
}
