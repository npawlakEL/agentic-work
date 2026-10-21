using PandA.Core.Domain;

namespace PandA.Core.Ports;

public interface IXRefStore
{
    ValueTask AddAsync(string tuId, XRef xref, CancellationToken ct = default);

    ValueTask<IReadOnlyList<string>> FindTuIdsByBarcodeAsync(string barcode, CancellationToken ct = default);

    /// <summary>Delete every row of <paramref name="barcode"/> with <paramref name="type"/>; returns the rows deleted.</summary>
    ValueTask<int> RemoveByBarcodeAsync(string barcode, BarcodeType type, CancellationToken ct = default);

    /// <summary>
    /// Delete every cross-reference row of a carton (source <c>sdisp_TOOL_CUSTOM_Wave_Delete</c> L69 deletes the
    /// PandaDataXRef rows of the purged cartons); returns the rows deleted. Owner ruling 2026-10-21 (R11).
    /// </summary>
    ValueTask<int> RemoveByTuIdAsync(string tuId, CancellationToken ct = default);

    /// <summary>
    /// Every cross-reference barcode recorded for a carton (matches source <c>sdisp_TOOL_PA_VerifyLabel</c>
    /// L191-195, which reads the carton's full xref set as its expected 'BlindLabel' values).
    /// </summary>
    ValueTask<IReadOnlyList<XRef>> FindByTuIdAsync(string tuId, CancellationToken ct = default) =>
        ValueTask.FromResult<IReadOnlyList<XRef>>([]);
}
