namespace PandA.UI.Contracts.Status;

/// <summary>
/// Live line-status feed for the Status Dashboard. Returns the current snapshot and an
/// <see cref="IAsyncEnumerable{T}"/> of updates the host raises from real (or simulated) events.
/// </summary>
public interface ILineStatusStream
{
    /// <summary>Current status of every line.</summary>
    Task<IReadOnlyList<LineStatus>> GetSnapshotAsync(CancellationToken ct = default);

    /// <summary>Pushes an updated <see cref="LineStatus"/> whenever a line's health changes.</summary>
    IAsyncEnumerable<LineStatus> SubscribeAsync(CancellationToken ct = default);
}

/// <summary>
/// Live printer-status feed for the Status Dashboard.
/// </summary>
public interface IPrinterStatusStream
{
    /// <summary>Current status of every printer.</summary>
    Task<IReadOnlyList<PrinterStatus>> GetSnapshotAsync(CancellationToken ct = default);

    /// <summary>Pushes an updated <see cref="PrinterStatus"/> whenever a printer's health changes.</summary>
    IAsyncEnumerable<PrinterStatus> SubscribeAsync(CancellationToken ct = default);
}
