using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PandA.Core.Advice;
using PandA.Core.Concurrency;
using PandA.Core.Services;
using PandA.Core.Settings;

namespace PandA.Core;

/// <summary>
/// MP1 — host label-advice. Creates the transport-order shell for a blind label and stores its typed
/// label set. Phase-1 duplicate-advice semantics = overwrite / last-wins (spec §6a; toggle deferred).
/// <para>
/// Multi-host (decision-010): advice runs under the shared <see cref="LockNames.Carton"/> lease (the same
/// lease induct holds while printing, so a re-advice can never interleave with a print and clobber its print
/// count). The order is inserted (unique active TuId) or version-checked updated; on a conflict the advice is
/// re-applied to the freshly loaded row once. A lease timeout propagates as <see cref="LockTimeoutException"/>
/// so the host message is not acknowledged and is redelivered.
/// </para>
/// </summary>
public interface ICartonAdviceService
{
    ValueTask<TransportOrder> AdviseAsync(
        string lineId,
        string blindLabel,
        PandaLabelSet labels,
        CancellationToken cancellationToken = default);

    ValueTask<TransportOrder> AdviseAsync(
        AdviceMessage advice,
        CancellationToken cancellationToken = default);
}

public sealed class CartonAdviceService : ICartonAdviceService
{
    private readonly ITransportOrderStore _store;
    private readonly IClock _clock;
    private readonly ISettingsProvider _settings;
    private readonly ILockProvider _locks;
    private readonly XRefService? _xref;
    private readonly ILogger<CartonAdviceService> _logger;

    public CartonAdviceService(
        ITransportOrderStore store,
        IClock clock,
        ISettingsProvider settings,
        ILockProvider locks,
        XRefService? xref = null,
        ILogger<CartonAdviceService>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _xref = xref;
        _logger = logger ?? NullLogger<CartonAdviceService>.Instance;
    }

    public async ValueTask<TransportOrder> AdviseAsync(
        string lineId,
        string blindLabel,
        PandaLabelSet labels,
        CancellationToken cancellationToken = default) =>
        await AdviseAsync(
            new AdviceMessage(lineId, blindLabel, labels),
            cancellationToken).ConfigureAwait(false);

    public async ValueTask<TransportOrder> AdviseAsync(
        AdviceMessage advice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(advice);
        ArgumentException.ThrowIfNullOrWhiteSpace(advice.LineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(advice.BlindLabel);
        ArgumentNullException.ThrowIfNull(advice.Labels);

        var lease = await _locks.AcquireAsync(LockNames.Carton(advice.BlindLabel), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await using (lease.ConfigureAwait(false))
        {
            var now = _clock.UtcNow;
            TransportOrder order;
            try
            {
                order = await ApplyAndSaveAsync(advice, now, cancellationToken).ConfigureAwait(false);
            }
            catch (ConcurrencyConflictException ex)
            {
                // A writer bypassed the lease (or it expired): re-apply the advice to the latest row once.
                _logger.LogWarning(ex, "Concurrency conflict advising {TuId}; re-applying advice to the latest row.", advice.BlindLabel);
                order = await ApplyAndSaveAsync(advice, now, cancellationToken).ConfigureAwait(false);
            }

            // F18: populate the barcode cross-reference so a later non-BL induct scan (oLPN/UPC/…) resolves
            // back to this carton's blind label.
            if (_xref is { } xref && advice.Barcodes is { Count: > 0 } barcodes)
            {
                foreach (var bc in barcodes)
                {
                    await xref.AssociateAsync(advice.BlindLabel, bc.Barcode, bc.Type, cancellationToken).ConfigureAwait(false);
                }
            }

            return order;
        }
    }

    private async ValueTask<TransportOrder> ApplyAndSaveAsync(
        AdviceMessage advice,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await _store.FindActiveByTuIdAsync(advice.BlindLabel, cancellationToken).ConfigureAwait(false);

        TransportOrder order;
        if (existing is null)
        {
            order = new TransportOrder(advice.BlindLabel, advice.LineId, advice.Labels, now);
            order.SetAdviceMetadata(
                advice.WaveId,
                advice.ProfileName,
                advice.Bypass,
                advice.VerifyEnabled,
                advice.VerifyPassDest,
                advice.VerifyFailDest);
            _logger.LogInformation(
                "Advised new transport order {TuId} on line {LineId} with {LabelCount} labels for wave {WaveId} and profile {ProfileName}.",
                advice.BlindLabel, advice.LineId, advice.Labels.Labels.Count, advice.WaveId, advice.ProfileName);
        }
        else
        {
            order = existing;
            // Owner ruling 2026-10-21 (R8): new advice/label data ALWAYS overwrites (replaces) the existing label
            // data — the former OverwriteLabelData=0 "oldest unprinted advice wins" path is removed and the setting
            // is superseded (ignored).
            var reprintLabels = await _settings.GetAsync(KnownSettings.ReprintLabels, cancellationToken).ConfigureAwait(false);
            Overwrite(existing, advice, now, reprintLabels);
        }

        // F23: wave create/update here.
        await _store.SaveAsync(order, cancellationToken).ConfigureAwait(false);
        return order;
    }

    private void Overwrite(TransportOrder existing, AdviceMessage advice, DateTimeOffset now, bool reprintLabels)
    {
        // New advice replaces the label data (last wins — owner ruling 2026-10-21, R8).
        // Re-arming is governed by "Reprint Labels" (decision-013 D4 / owner ruling 2026-10-21): off →
        // update the label data but keep a printed carton locked; on → re-arm a new generation.
        var rearm = reprintLabels || !existing.HasPrinted;
        existing.OverwriteAdvice(
            advice.LineId,
            advice.Labels,
            now,
            advice.WaveId,
            advice.ProfileName,
            advice.Bypass,
            advice.VerifyEnabled,
            advice.VerifyPassDest,
            advice.VerifyFailDest,
            rearm);
        if (rearm)
        {
            _logger.LogInformation(
                "Overwrote advice for transport order {TuId} on line {LineId} with {LabelCount} labels for wave {WaveId} and profile {ProfileName}.",
                advice.BlindLabel, advice.LineId, advice.Labels.Labels.Count, advice.WaveId, advice.ProfileName);
        }
        else
        {
            _logger.LogWarning(
                "Overwrote label data for locked transport order {TuId} on line {LineId} (Reprint Labels off); carton not re-armed, status {Status}.",
                advice.BlindLabel, advice.LineId, existing.Status);
        }
    }
}
