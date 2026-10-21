using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PandA.Core.Concurrency;
using PandA.Core.Ports;

namespace PandA.Core.Services;

/// <summary>
/// Carton / wave purge (owner ruling 2026-10-21, R11). Deleting a carton's transport order also deletes its
/// barcode cross-reference rows, matching source <c>sdisp_TOOL_CUSTOM_Wave_Delete</c> L69 (which deletes the
/// <c>PandaDataXRef</c> rows of the wave's cartons before the <c>PandaData</c> rows) so no orphaned xref can
/// later resolve an induct scan to a carton that no longer exists. Each carton is purged under its
/// <see cref="LockNames.Carton"/> lease; every deletion is logged at Information.
/// </summary>
public sealed class CartonPurgeService
{
    private readonly ITransportOrderStore _orders;
    private readonly IXRefStore _xref;
    private readonly ILockProvider _locks;
    private readonly ILogger<CartonPurgeService> _logger;

    public CartonPurgeService(
        ITransportOrderStore orders,
        IXRefStore xref,
        ILockProvider locks,
        ILogger<CartonPurgeService>? logger = null)
    {
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        _xref = xref ?? throw new ArgumentNullException(nameof(xref));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _logger = logger ?? NullLogger<CartonPurgeService>.Instance;
    }

    /// <summary>Delete one carton's transport order and all of its xref rows.</summary>
    public async ValueTask<CartonPurgeResult> PurgeCartonAsync(string tuId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tuId);

        var lease = await _locks.AcquireAsync(LockNames.Carton(tuId), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await using (lease.ConfigureAwait(false))
        {
            var orderDeleted = await _orders.DeleteAsync(tuId, cancellationToken).ConfigureAwait(false);
            var xrefRows = await _xref.RemoveByTuIdAsync(tuId, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Purged transport order {TuId} (order deleted: {OrderDeleted}); deleted {XRefRows} xref record(s).",
                tuId, orderDeleted, xrefRows);
            return new CartonPurgeResult(orderDeleted ? 1 : 0, xrefRows);
        }
    }

    /// <summary>Delete every carton advised under <paramref name="waveId"/> together with their xref rows.</summary>
    public async ValueTask<CartonPurgeResult> PurgeWaveAsync(string waveId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(waveId);

        var tuIds = await _orders.FindTuIdsByWaveAsync(waveId, cancellationToken).ConfigureAwait(false);
        var orders = 0;
        var xrefRows = 0;
        foreach (var tuId in tuIds)
        {
            var result = await PurgeCartonAsync(tuId, cancellationToken).ConfigureAwait(false);
            orders += result.OrdersDeleted;
            xrefRows += result.XRefRowsDeleted;
        }

        _logger.LogInformation(
            "Deleted wave {WaveId}: {Orders} transport order(s) and {XRefRows} xref record(s).",
            waveId, orders, xrefRows);
        return new CartonPurgeResult(orders, xrefRows);
    }
}

/// <summary>Rows removed by a <see cref="CartonPurgeService"/> purge.</summary>
public readonly record struct CartonPurgeResult(int OrdersDeleted, int XRefRowsDeleted);
