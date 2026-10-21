using PandA.Core.Concurrency;

namespace PandA.Core;

/// <summary>
/// Shared printer runtime state (source <c>PrinterState</c> table) — the authority for online/spare/rotation
/// state across every PandA host (decision-010). Printer ids are unique across lines (source PrinterRecID).
/// <para>
/// Contract:
/// <list type="bullet">
/// <item><see cref="GetSnapshotAsync"/> is a read-through: it returns fresh, detached copies on every call —
/// callers take one snapshot per operation and never cache it across operations. A printer with no state row
/// is absent from the result (source <c>sdivw_PrinterStatus</c> INNER JOIN: never online).</item>
/// <item>Every mutation is a single atomic row operation (one conditional/unconditional column update); no
/// read-modify-write of the whole row happens in Core.</item>
/// <item>Spare and status mutations are group decisions made by lane evaluation and are only issued while
/// holding the <see cref="LockNames.PrinterState"/> lease for the printer's line.
/// <see cref="StampLastPrintedAsync"/> is a single-column, last-writer-wins timestamp used only as a
/// load-balancing hint and needs no lease.</item>
/// </list>
/// </para>
/// </summary>
public interface IPrinterStateStore
{
    /// <summary>Fresh detached snapshots for <paramref name="printerIds"/> (missing rows omitted), keyed case-insensitively.</summary>
    ValueTask<IReadOnlyDictionary<string, PrinterState>> GetSnapshotAsync(
        IEnumerable<string> printerIds,
        CancellationToken cancellationToken = default);

    /// <summary>Set the PLC/engine status and stamp <c>LastStatusUpdate</c> (creates the row when absent).</summary>
    ValueTask SetStatusAsync(
        string printerId,
        bool plcOnline,
        bool engineOnline,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Set the spare flag and, when <paramref name="statusUpdatedAt"/> is non-null, stamp <c>LastStatusUpdate</c>
    /// (promote/demote stamps it; the offline-clears-spare normalization does not). No-op when the row is absent.
    /// </summary>
    ValueTask SetSpareAsync(
        string printerId,
        bool isSpare,
        DateTimeOffset? statusUpdatedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Stamp <c>LastPrinted</c> (load-balancing rotation). No-op when the row is absent.</summary>
    ValueTask StampLastPrintedAsync(
        string printerId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);
}

/// <summary>Convenience helpers over <see cref="IPrinterStateStore"/>.</summary>
public static class PrinterStateStoreExtensions
{
    /// <summary>A fresh snapshot of every printer configured on <paramref name="line"/>.</summary>
    public static ValueTask<IReadOnlyDictionary<string, PrinterState>> GetLineSnapshotAsync(
        this IPrinterStateStore store,
        LineConfig line,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(line);
        return store.GetSnapshotAsync(line.Printers.Select(p => p.PrinterId), cancellationToken);
    }
}
