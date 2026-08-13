using PandA.Core.Domain;

namespace PandA.Core.Ports;

public interface IXRefStore
{
    ValueTask AddAsync(string tuId, XRef xref, CancellationToken ct = default);

    ValueTask<IReadOnlyList<string>> FindTuIdsByBarcodeAsync(string barcode, CancellationToken ct = default);

    ValueTask<int> RemoveByBarcodeAsync(string barcode, BarcodeType type, CancellationToken ct = default);
}
