using PandA.Core;

namespace PandA.Sim;

/// <summary>
/// In-memory shared printer-state table (decision-010). Snapshots are detached copies; every mutation is a
/// single atomic row operation. Share one instance across simulated hosts/lines to model the shared database.
/// </summary>
public sealed class InMemoryPrinterStateStore : IPrinterStateStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PrinterState> _rows = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Seed or replace a printer's row (a copy of <paramref name="state"/> is stored).</summary>
    public InMemoryPrinterStateStore Put(PrinterState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            _rows[state.PrinterId] = state.Clone();
        }

        return this;
    }

    /// <summary>Seed a row only when the printer has none yet.</summary>
    public void PutIfAbsent(PrinterState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            _rows.TryAdd(state.PrinterId, state.Clone());
        }
    }

    /// <summary>A detached copy of one printer's row, or null.</summary>
    public PrinterState? Get(string printerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        lock (_gate)
        {
            return _rows.TryGetValue(printerId, out var row) ? row.Clone() : null;
        }
    }

    public ValueTask<IReadOnlyDictionary<string, PrinterState>> GetSnapshotAsync(
        IEnumerable<string> printerIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(printerIds);
        var result = new Dictionary<string, PrinterState>(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            foreach (var id in printerIds)
            {
                if (_rows.TryGetValue(id, out var row))
                {
                    result[id] = row.Clone();
                }
            }
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, PrinterState>>(result);
    }

    public ValueTask SetStatusAsync(
        string printerId,
        bool plcOnline,
        bool engineOnline,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        lock (_gate)
        {
            if (!_rows.TryGetValue(printerId, out var row))
            {
                row = new PrinterState(printerId);
                _rows[printerId] = row;
            }

            row.PlcOnline = plcOnline;
            row.EngineOnline = engineOnline;
            row.LastStatusUpdate = at;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask SetSpareAsync(
        string printerId,
        bool isSpare,
        DateTimeOffset? statusUpdatedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        lock (_gate)
        {
            if (_rows.TryGetValue(printerId, out var row))
            {
                row.IsSpare = isSpare;
                if (statusUpdatedAt is { } at)
                {
                    row.LastStatusUpdate = at;
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask StampLastPrintedAsync(string printerId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        lock (_gate)
        {
            if (_rows.TryGetValue(printerId, out var row))
            {
                row.LastPrinted = at;
            }
        }

        return ValueTask.CompletedTask;
    }
}
