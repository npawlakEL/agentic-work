using PandA.Core.Domain;
using PandA.Core.Ports;

namespace PandA.Sim.Stores;

public sealed class InMemoryXRefStore : IXRefStore
{
    private readonly Lock _gate = new();
    private readonly List<(string TuId, XRef XRef)> _entries = [];

    public IReadOnlyList<(string TuId, XRef XRef)> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public ValueTask AddAsync(string tuId, XRef xref, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tuId);
        ArgumentNullException.ThrowIfNull(xref);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_entries.Any(entry =>
                entry.TuId == tuId
                && entry.XRef.Barcode == xref.Barcode
                && entry.XRef.Type == xref.Type))
            {
                _entries.Add((tuId, xref));
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<string>> FindTuIdsByBarcodeAsync(
        string barcode,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            IReadOnlyList<string> tuIds = [.. _entries
                .Where(entry => entry.XRef.Barcode == barcode)
                .Select(entry => entry.TuId)
                .Distinct(StringComparer.Ordinal)];
            return ValueTask.FromResult(tuIds);
        }
    }

    public ValueTask<int> RemoveByBarcodeAsync(
        string barcode,
        BarcodeType type,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var removed = _entries.RemoveAll(entry => entry.XRef.Barcode == barcode && entry.XRef.Type == type);
            return ValueTask.FromResult(removed);
        }
    }
}
