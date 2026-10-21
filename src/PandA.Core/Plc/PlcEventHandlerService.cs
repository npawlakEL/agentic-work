using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PandA.Core.Concurrency;
using PandA.Core.Settings;

namespace PandA.Core.Plc;

/// <summary>
/// F15 / decision-009 — ingests PLC tracking events and applies pre-verify carton recovery. Silently drops
/// ignored codes (217/218); logs every other code at Warning; then, only for a specifically-identified carton
/// reported <b>before</b> the verify scanner, re-arms the carton (subject to the <c>Reprint Labels</c> gate).
/// <para>
/// Multi-host (decision-010): the recovery mutation runs under the shared <see cref="LockNames.Carton"/> lease
/// and is persisted with a version-checked update (reload + re-apply + retry once). On lease timeout the
/// recovery is skipped and logged at Warning — another host is actively processing the carton, so its state
/// is about to change anyway; the PLC re-reports persistent faults.
/// </para>
/// </summary>
public sealed class PlcEventHandlerService : IPlcEventHandler
{
    private readonly ITransportOrderStore _store;
    private readonly ICartonRunRepository _runs;
    private readonly IVerifyDeviceProvider _verifyDevices;
    private readonly ISettingsProvider _settings;
    private readonly ILockProvider _locks;
    private readonly ILogger<PlcEventHandlerService> _logger;

    public PlcEventHandlerService(
        ITransportOrderStore store,
        ICartonRunRepository runs,
        IVerifyDeviceProvider verifyDevices,
        ISettingsProvider settings,
        ILockProvider locks,
        ILogger<PlcEventHandlerService>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _verifyDevices = verifyDevices ?? throw new ArgumentNullException(nameof(verifyDevices));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _logger = logger ?? NullLogger<PlcEventHandlerService>.Instance;
    }

    public async ValueTask HandleAsync(PlcEvent evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        // Step 1 — gate: codes 217/218 are silently ignored (no log, no recovery).
        if (PlcEventCodes.IsIgnored(evt.EventCode))
        {
            return;
        }

        // Steps 2–3 — describe + log at Warning for every processed code (mapped or not).
        var description = PlcEventCodes.Describe(evt.EventCode);
        _logger.LogWarning(
            "PLC Error Detected: {Error} for CartonListID: {CartonListId} (Device {DeviceId}) on line {LineId}.",
            description, evt.CartonListId, evt.DeviceId, evt.LineId);

        // Step 4 — recovery condition: a specific carton, a configured verify scanner, and a pre-verify device.
        if (evt.CartonListId is not > 0)
        {
            return;
        }

        var verifyDeviceId = await _verifyDevices.GetVerifyDeviceIdAsync(evt.LineId, cancellationToken).ConfigureAwait(false);
        if (verifyDeviceId <= 0 || evt.DeviceId >= verifyDeviceId)
        {
            return;
        }

        var run = await _runs.FindByRunIdAsync(evt.CartonListId.Value, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return;
        }

        ILease lease;
        try
        {
            lease = await _locks.AcquireAsync(LockNames.Carton(run.TuId), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (LockTimeoutException ex)
        {
            _logger.LogWarning(
                ex,
                "Carton {TuId}: tracking recovery for {Error} skipped; lease {Resource} is held by another host.",
                run.TuId, description, ex.Resource);
            return;
        }

        await using (lease.ConfigureAwait(false))
        {
            var order = await _store.FindActiveByTuIdAsync(run.TuId, cancellationToken).ConfigureAwait(false);
            if (order is null)
            {
                return;
            }

            var reprintAllowed = await _settings.GetAsync(KnownSettings.ReprintLabels, cancellationToken).ConfigureAwait(false);
            var outcome = order.ResetForTrackingEvent(evt.EventCode, reprintAllowed);
            if (outcome == TrackingRecovery.NotApplicable)
            {
                return;
            }

            try
            {
                await _store.UpdateAsync(order, cancellationToken).ConfigureAwait(false);
            }
            catch (ConcurrencyConflictException)
            {
                // A writer bypassed the lease (or it expired): re-apply the recovery to the latest row once.
                var fresh = await _store.FindActiveByTuIdAsync(run.TuId, cancellationToken).ConfigureAwait(false);
                if (fresh is null)
                {
                    return;
                }

                outcome = fresh.ResetForTrackingEvent(evt.EventCode, reprintAllowed);
                if (outcome == TrackingRecovery.NotApplicable)
                {
                    return;
                }

                await _store.UpdateAsync(fresh, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogWarning(
                "Carton {TuId} on line {LineId}: {Outcome} after {Error} (device {DeviceId} < verify {VerifyDeviceId}).",
                run.TuId, evt.LineId, outcome, description, evt.DeviceId, verifyDeviceId);
        }
    }
}
