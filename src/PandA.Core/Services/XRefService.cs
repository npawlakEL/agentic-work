using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PandA.Core.Domain;
using PandA.Core.Ports;

namespace PandA.Core.Services;

public sealed class XRefService
{
    private readonly IXRefStore _store;
    private readonly ILogger<XRefService> _logger;

    public XRefService(IXRefStore store, ILogger<XRefService>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? NullLogger<XRefService>.Instance;
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

    /// <summary>
    /// Delete the xref rows of <paramref name="barcode"/> (source <c>sdisp_TOOL_CUSTOM_LPNxRef_Disassociate</c>
    /// L37-51: DELETE + log of the row count; owner ruling 2026-10-21, R11). Returns the rows deleted.
    /// </summary>
    public async ValueTask<int> DisassociateAsync(
        string barcode,
        BarcodeType type,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);
        var removed = await _store.RemoveByBarcodeAsync(barcode, type, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Deleted {BarcodeType} {Barcode}, which was {RowCount} record(s) in xref.", type, barcode, removed);
        return removed;
    }
}
