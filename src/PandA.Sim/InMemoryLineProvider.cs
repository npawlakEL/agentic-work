using System.Collections.Concurrent;
using PandA.Core;

namespace PandA.Sim;

/// <summary>
/// In-memory line-config provider for the simulator/tests. Printer runtime state is not held here: it lives in
/// the shared <see cref="InMemoryPrinterStateStore"/> (<see cref="PrinterStates"/>), which <see cref="Add"/>
/// seeds. Pass one shared store to every provider to model the shared database (decision-010).
/// </summary>
public sealed class InMemoryLineProvider : ILineProvider
{
    private readonly ConcurrentDictionary<string, LineContext> _lines = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryLineProvider(InMemoryPrinterStateStore? printerStates = null)
    {
        PrinterStates = printerStates ?? new InMemoryPrinterStateStore();
    }

    /// <summary>The printer-state table this provider seeds.</summary>
    public InMemoryPrinterStateStore PrinterStates { get; }

    /// <summary>
    /// Register a line and seed its printers' state rows. If <paramref name="states"/> is omitted, a default
    /// available state is created for every configured printer that has no row yet; supplied states replace
    /// any existing rows (copies are stored).
    /// </summary>
    public InMemoryLineProvider Add(LineConfig config, IEnumerable<PrinterState>? states = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (states is null)
        {
            foreach (var printer in config.Printers)
            {
                PrinterStates.PutIfAbsent(new PrinterState(printer.PrinterId));
            }
        }
        else
        {
            foreach (var state in states)
            {
                PrinterStates.Put(state);
            }
        }

        _lines[config.LineId] = new LineContext(config);
        return this;
    }

    public ValueTask<LineContext?> GetLineAsync(string lineId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        _lines.TryGetValue(lineId, out var context);
        return ValueTask.FromResult(context);
    }
}
