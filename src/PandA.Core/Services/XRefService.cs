using PandA.Core.Domain;
using PandA.Core.Ports;

namespace PandA.Core.Services;

public sealed class XRefService
{
    private readonly IXRefStore _store;

    public XRefService(IXRefStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async ValueTask<XRefAssociateResult> AssociateAsync(
        string tuId,
        string barcode,
        BarcodeType type,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tuId);
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);

        var existingTuIds = await _store.FindTuIdsByBarcodeAsync(barcode, ct).ConfigureAwait(false);
        if (existingTuIds.Contains(tuId, StringComparer.Ordinal))
        {
            return XRefAssociateResult.AlreadyAssociated;
        }

        await _store.AddAsync(tuId, new XRef(barcode, type), ct).ConfigureAwait(false);
        return XRefAssociateResult.Success;
    }

    public ValueTask<int> DisassociateAsync(
        string barcode,
        BarcodeType type,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);
        return _store.RemoveByBarcodeAsync(barcode, type, ct);
    }
}
