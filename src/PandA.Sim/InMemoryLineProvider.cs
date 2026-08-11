using PandA.Core;

namespace PandA.Sim;

/// <summary>In-memory line provider for the simulator/tests, holding line config + mutable printer state.</summary>
public sealed class InMemoryLineProvider : ILineProvider
{
    private readonly Dictionary<string, LineContext> _lines = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Register a line. If <paramref name="states"/> is omitted, a default available state is created for
    /// every configured printer.
    /// </summary>
    public InMemoryLineProvider Add(LineConfig config, IEnumerable<PrinterState>? states = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var stateMap = (states ?? config.Printers.Select(p => new PrinterState(p.PrinterId)))
            .ToDictionary(s => s.PrinterId, StringComparer.OrdinalIgnoreCase);

        _lines[config.LineId] = new LineContext(config, stateMap);
        return this;
    }

    public ValueTask<LineContext?> GetLineAsync(string lineId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineId);
        _lines.TryGetValue(lineId, out var context);
        return ValueTask.FromResult(context);
    }
}
