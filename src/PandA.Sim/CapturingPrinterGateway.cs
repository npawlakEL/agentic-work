using System.Collections.Concurrent;
using PandA.Core;

namespace PandA.Sim;

/// <summary>Capturing printer gateway for the simulator/tests: records every job instead of sending TCP.</summary>
public sealed class CapturingPrinterGateway : IPrinterGateway
{
    private readonly ConcurrentQueue<PrintJob> _jobs = new();

    public IReadOnlyList<PrintJob> Jobs => _jobs.ToList();

    public ValueTask SendAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        _jobs.Enqueue(job);
        return ValueTask.CompletedTask;
    }
}
