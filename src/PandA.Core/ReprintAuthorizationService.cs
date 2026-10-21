using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PandA.Core.Concurrency;

namespace PandA.Core;

/// <summary>
/// Operator reprint authorization (decision-003): marks an active, printed carton as authorized for one
/// reprint. Like every <see cref="TransportOrder"/> writer it holds the shared <see cref="LockNames.Carton"/>
/// lease and persists with a version-checked update (decision-010), so an authorization can never be lost
/// to — or clobber — a concurrent induct/verify on another host.
/// </summary>
public sealed class ReprintAuthorizationService
{
    private readonly ITransportOrderStore _store;
    private readonly ILockProvider _locks;
    private readonly ILogger<ReprintAuthorizationService> _logger;

    public ReprintAuthorizationService(
        ITransportOrderStore store,
        ILockProvider locks,
        ILogger<ReprintAuthorizationService>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _logger = logger ?? NullLogger<ReprintAuthorizationService>.Instance;
    }

    /// <summary>
    /// Authorize one reprint of <paramref name="tuId"/>. Returns false when there is no active order or when
    /// the carton lease could not be acquired (another host is processing it — the operator retries).
    /// </summary>
    public async ValueTask<bool> AuthorizeAsync(string tuId, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tuId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        ILease lease;
        try
        {
            lease = await _locks.AcquireAsync(LockNames.Carton(tuId), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (LockTimeoutException ex)
        {
            _logger.LogWarning(ex, "Reprint authorization for {TuId} not applied: carton lease is held by another host.", tuId);
            return false;
        }

        await using (lease.ConfigureAwait(false))
        {
            var order = await _store.FindActiveByTuIdAsync(tuId, cancellationToken).ConfigureAwait(false);
            if (order is null)
            {
                return false;
            }

            var saved = await _store.SaveWithRetryAsync(order, o => o.AuthorizeReprint(reason), cancellationToken)
                .ConfigureAwait(false);
            if (saved is null)
            {
                return false;
            }

            _logger.LogInformation("Reprint authorized for transport order {TuId}: {Reason}.", tuId, reason);
            return true;
        }
    }
}
