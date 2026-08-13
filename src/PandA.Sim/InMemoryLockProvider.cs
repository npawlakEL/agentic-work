using System.Collections.Concurrent;
using PandA.Core.Concurrency;

namespace PandA.Sim;

public sealed class InMemoryLockProvider : ILockProvider
{
    public static readonly TimeSpan SourceDefaultTimeout = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string resourceName,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var waitTimeout = timeout ?? SourceDefaultTimeout;
        if (waitTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be non-negative.");
        }

        var semaphore = _locks.GetOrAdd(resourceName, static _ => new SemaphoreSlim(1, 1));
        var acquired = await semaphore.WaitAsync(waitTimeout, cancellationToken).ConfigureAwait(false);
        if (!acquired)
        {
            // Source sdisp_PA_Lock.sql logs and proceeds; the Core port makes timeout explicit for callers.
            throw new TimeoutException($"Timed out acquiring lock '{resourceName}'.");
        }

        return new LockHandle(semaphore);
    }

    private sealed class LockHandle : IAsyncDisposable
    {
        private SemaphoreSlim? _semaphore;

        public LockHandle(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public ValueTask DisposeAsync()
        {
            var semaphore = Interlocked.Exchange(ref _semaphore, null);
            semaphore?.Release();
            return ValueTask.CompletedTask;
        }
    }
}
