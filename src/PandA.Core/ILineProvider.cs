namespace PandA.Core;

/// <summary>Runtime context for a line: its config plus mutable per-printer state.</summary>
public sealed class LineContext
{
    public LineContext(LineConfig config, IReadOnlyDictionary<string, PrinterState> states)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        States = states ?? throw new ArgumentNullException(nameof(states));
    }

    public LineConfig Config { get; }

    public IReadOnlyDictionary<string, PrinterState> States { get; }
}

/// <summary>
/// Supplies line config + printer runtime state. In the Sim these come from PandaLine.json + in-memory
/// state; the econtroller adapter reads config via EffortlessConfiguration.
/// </summary>
public interface ILineProvider
{
    ValueTask<LineContext?> GetLineAsync(string lineId, CancellationToken cancellationToken = default);
}
