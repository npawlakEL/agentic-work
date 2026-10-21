using System.Diagnostics;
using System.Globalization;
using PandA.Core.Concurrency;

namespace PandA.Sim;

/// <summary>
/// In-memory <see cref="ILockProvider"/> that models the shared lease table an eController adapter keeps in
/// the platform database (decision-010 adapter guide: a row per resource with <c>Owner</c> and
/// <c>ExpiresUtc</c>). Every <see cref="ForHost"/> view shares one table, so separate "hosts" contend exactly
/// as separate processes would against the database.
/// <para>
/// Semantics: a lease is granted when the resource has no row, its row has expired (crashed holder), or —
/// never — when another live owner holds it. Leases are <b>not re-entrant</b>. Expiry is measured on the
/// injected <see cref="TimeProvider"/> (tests advance a manual clock to simulate a crashed holder); the
/// acquire timeout is measured on a real stopwatch. <see cref="ILease.RenewAsync"/> extends only while still
/// owner; disposal releases only while still owner (a taken-over lease is never released by the old holder).
/// </para>
/// </summary>
public sealed class InMemoryLeaseLockProvider : ILockProvider
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    private readonly LeaseTable _table;
    private readonly TimeProvider _time;
    private readonly TimeSpan _leaseDuration;
    private readonly string _hostId;

    public InMemoryLeaseLockProvider(TimeProvider? time = null, TimeSpan? leaseDuration = null, string hostId = "host")
        : this(new LeaseTable(), time ?? TimeProvider.System, leaseDuration ?? LockDefaults.LeaseDuration, hostId)
    {
    }

    private InMemoryLeaseLockProvider(LeaseTable table, TimeProvider time, TimeSpan leaseDuration, string hostId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        _table = table;
        _time = time;
        _leaseDuration = leaseDuration;
        _hostId = hostId;
    }

    /// <summary>A view of the same shared lease table acting as another host (distinct owner ids).</summary>
    public InMemoryLeaseLockProvider ForHost(string hostId) => new(_table, _time, _leaseDuration, hostId);

    /// <summary>The current owner of <paramref name="resourceName"/> if a live (unexpired) lease exists.</summary>
    public string? CurrentOwner(string resourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        return _table.LiveOwner(resourceName, _time.GetUtcNow());
    }

    public async ValueTask<ILease> AcquireAsync(
        string resourceName,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        var wait = timeout ?? LockDefaults.AcquireTimeout;
        var owner = string.Create(CultureInfo.InvariantCulture, $"{_hostId}:{Guid.NewGuid():N}");
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var changed = _table.ChangeSignal;
            var expires = _time.GetUtcNow() + _leaseDuration;
            if (_table.TryAcquire(resourceName, owner, _time.GetUtcNow(), expires))
            {
                return new Lease(this, resourceName, owner, expires);
            }

            var remaining = wait == Timeout.InfiniteTimeSpan ? PollInterval : wait - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                throw new LockTimeoutException(resourceName, wait);
            }

            // Wake on any release, or poll (expiry is time-driven, so a crashed holder never signals).
            var delay = remaining < PollInterval ? remaining : PollInterval;
            await Task.WhenAny(changed, Task.Delay(delay, cancellationToken)).ConfigureAwait(false);
        }
    }

    private sealed class Lease(InMemoryLeaseLockProvider provider, string resource, string owner, DateTimeOffset expires) : ILease
    {
        private int _disposed;

        public string Resource { get; } = resource;

        public string OwnerId { get; } = owner;

        public DateTimeOffset ExpiresUtc { get; private set; } = expires;

        public ValueTask<bool> RenewAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = provider._time.GetUtcNow();
            var next = now + provider._leaseDuration;
            if (Volatile.Read(ref _disposed) != 0 || !provider._table.TryRenew(Resource, OwnerId, now, next))
            {
                return ValueTask.FromResult(false);
            }

            ExpiresUtc = next;
            return ValueTask.FromResult(true);
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                provider._table.Release(Resource, OwnerId);
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>The shared "database table": resource → (owner, expiry).</summary>
    private sealed class LeaseTable
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, (string Owner, DateTimeOffset ExpiresUtc)> _rows = new(StringComparer.Ordinal);
        private TaskCompletionSource _changed = NewSignal();

        public Task ChangeSignal
        {
            get
            {
                lock (_gate)
                {
                    return _changed.Task;
                }
            }
        }

        public string? LiveOwner(string resource, DateTimeOffset now)
        {
            lock (_gate)
            {
                return _rows.TryGetValue(resource, out var row) && row.ExpiresUtc > now ? row.Owner : null;
            }
        }

        // Conditional update: take the row when absent or expired (adapter: Update ... WHERE ExpiresUtc < now,
        // else Create; affected rows == 1).
        public bool TryAcquire(string resource, string owner, DateTimeOffset now, DateTimeOffset expires)
        {
            lock (_gate)
            {
                if (_rows.TryGetValue(resource, out var row) && row.ExpiresUtc > now)
                {
                    return false;
                }

                _rows[resource] = (owner, expires);
                return true;
            }
        }

        public bool TryRenew(string resource, string owner, DateTimeOffset now, DateTimeOffset expires)
        {
            lock (_gate)
            {
                // Still owner and not yet expired (an expired lease may already have been taken over — and
                // even if not, the holder must treat it as lost: fencing).
                if (!_rows.TryGetValue(resource, out var row)
                    || !string.Equals(row.Owner, owner, StringComparison.Ordinal)
                    || row.ExpiresUtc <= now)
                {
                    return false;
                }

                _rows[resource] = (owner, expires);
                return true;
            }
        }

        public void Release(string resource, string owner)
        {
            TaskCompletionSource signal;
            lock (_gate)
            {
                if (!_rows.TryGetValue(resource, out var row) || !string.Equals(row.Owner, owner, StringComparison.Ordinal))
                {
                    return;
                }

                _rows.Remove(resource);
                signal = _changed;
                _changed = NewSignal();
            }

            signal.TrySetResult();
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
