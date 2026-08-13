using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PandA.Core.Advice;
using PandA.Core.Settings;

namespace PandA.Core;

/// <summary>
/// MP1 — host label-advice. Creates the transport-order shell for a blind label and stores its typed
/// label set. Phase-1 duplicate-advice semantics = overwrite / last-wins (spec §6a; toggle deferred).
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
    private readonly ILogger<CartonAdviceService> _logger;

    public CartonAdviceService(
        ITransportOrderStore store,
        IClock clock,
        ISettingsProvider settings,
        ILogger<CartonAdviceService>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
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

        _ = _settings;

        var now = _clock.UtcNow;
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
            existing.OverwriteAdvice(
                advice.LineId,
                advice.Labels,
                now,
                advice.WaveId,
                advice.ProfileName,
                advice.Bypass,
                advice.VerifyEnabled,
                advice.VerifyPassDest,
                advice.VerifyFailDest); // last-wins
            order = existing;
            _logger.LogInformation(
                "Overwrote advice for transport order {TuId} on line {LineId} with {LabelCount} labels for wave {WaveId} and profile {ProfileName}.",
                advice.BlindLabel, advice.LineId, advice.Labels.Labels.Count, advice.WaveId, advice.ProfileName);
        }

        // F18/F24: xref population here.
        // F23: wave create/update here.
        await _store.UpsertAsync(order, cancellationToken).ConfigureAwait(false);
        return order;
    }
}
