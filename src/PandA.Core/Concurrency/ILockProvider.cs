namespace PandA.Core.Concurrency;

public interface ILockProvider
{
    ValueTask<IAsyncDisposable> AcquireAsync(
        string resourceName,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
