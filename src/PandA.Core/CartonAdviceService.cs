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
}

public sealed class CartonAdviceService : ICartonAdviceService
{
    private readonly ITransportOrderStore _store;
    private readonly IClock _clock;

    public CartonAdviceService(ITransportOrderStore store, IClock clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async ValueTask<TransportOrder> AdviseAsync(
        string lineId,
        string blindLabel,
        PandaLabelSet labels,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(blindLabel);
        ArgumentNullException.ThrowIfNull(labels);

        var now = _clock.UtcNow;
        var existing = await _store.FindActiveByTuIdAsync(blindLabel, cancellationToken).ConfigureAwait(false);

        TransportOrder order;
        if (existing is null)
        {
            order = new TransportOrder(blindLabel, lineId, labels, now);
        }
        else
        {
            existing.OverwriteAdvice(lineId, labels, now); // last-wins
            order = existing;
        }

        await _store.UpsertAsync(order, cancellationToken).ConfigureAwait(false);
        return order;
    }
}
