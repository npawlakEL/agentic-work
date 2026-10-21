namespace PandA.Core;

/// <summary>
/// Runtime context for a line: its configuration only. Printer runtime state is <b>not</b> part of the
/// context (decision-010) — it lives in the shared <see cref="IPrinterStateStore"/> and is read as a fresh
/// snapshot per operation, so no host ever holds live mutable printer state.
/// </summary>
public sealed class LineContext
{
    public LineContext(LineConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public LineConfig Config { get; }
}

/// <summary>
/// Supplies line configuration. In the Sim this comes from PandaLine.json; the econtroller adapter reads
/// config via EffortlessConfiguration.
/// <para>
/// Multi-host (decision-010): implementations are read-through — every call reflects the current shared
/// configuration (or a cache bounded by a short TTL / config version), never an unbounded per-process cache.
/// </para>
/// </summary>
public interface ILineProvider
{
    ValueTask<LineContext?> GetLineAsync(string lineId, CancellationToken cancellationToken = default);
}
