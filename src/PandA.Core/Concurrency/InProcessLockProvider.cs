namespace PandA.Core.Concurrency;

/// <summary>
/// In-process <see cref="ILockProvider"/> fallback used when no provider is injected: one
/// <see cref="SemaphoreSlim"/> per resource name, reference-counted so per-carton keys do not accumulate.
/// Serializes callers within this process only; cross-process deployments must inject a distributed provider.
/// </summary>
public sealed class InProcessLockProvider : ILockProvider
{
    /// <summary>Source <c>sdisp_PA_Lock</c> default wait.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Process-wide instance shared by services constructed without an explicit provider.</summary>
    public static InProcessLockProvider Shared { get; } = new();

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        string resourceName,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var wait = timeout ?? DefaultTimeout;
        if (wait < TimeSpan.Zero && wait != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be non-negative.");
        }

        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(resourceName, out entry!))
            {
                entry = new Entry();
                _entries[resourceName] = entry;
            }

            entry.RefCount++;
        }

        bool acquired;
        try
        {
            acquired = await entry.Semaphore.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Release(resourceName, entry, held: false);
            throw;
        }

        if (!acquired)
        {
            Release(resourceName, entry, held: false);
            throw new TimeoutException($"Timed out acquiring lock '{resourceName}'.");
        }

        return new Handle(this, resourceName, entry);
    }

    private void Release(string resourceName, Entry entry, bool held)
    {
        if (held)
        {
            entry.Semaphore.Release();
        }

        lock (_entries)
        {
            entry.RefCount--;
            if (entry.RefCount == 0)
            {
                _entries.Remove(resourceName);
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int RefCount { get; set; }
    }

    private sealed class Handle(InProcessLockProvider owner, string resourceName, Entry entry) : IAsyncDisposable
    {
        private int _disposed;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Release(resourceName, entry, held: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
