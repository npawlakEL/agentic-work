using PandA.Core;

namespace PandA.Sim;

/// <summary>
/// Capturing printer gateway for the simulator/tests: records jobs instead of sending TCP. History is
/// bounded to the most recent <see cref="MaxRetainedJobs"/> jobs; <see cref="Count"/> is the absolute
/// number of jobs ever sent so <see cref="JobsSince"/> range reads stay correct after old jobs are dropped.
/// </summary>
public sealed class CapturingPrinterGateway : IPrinterGateway
{
    public const int DefaultMaxRetainedJobs = 500;

    private readonly object _sync = new();
    private readonly Queue<PrintJob> _jobs = new();
    private int _count;

    public CapturingPrinterGateway(int maxRetainedJobs = DefaultMaxRetainedJobs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRetainedJobs, 1);
        MaxRetainedJobs = maxRetainedJobs;
    }

    public int MaxRetainedJobs { get; }

    /// <summary>The retained (most recent) jobs, oldest first.</summary>
    public IReadOnlyList<PrintJob> Jobs
    {
        get
        {
            lock (_sync)
            {
                return [.. _jobs];
            }
        }
    }

    /// <summary>Absolute number of jobs ever sent (a monotonically increasing cursor for <see cref="JobsSince"/>).</summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _count;
            }
        }
    }

    /// <summary>Raised for each dispatched job so a host can reflect real prints (e.g. dashboard "last printed").</summary>
    public event Action<PrintJob>? JobSent;

    /// <summary>Jobs sent at or after absolute index <paramref name="index"/> (a prior <see cref="Count"/>) that are still retained.</summary>
    public IReadOnlyList<PrintJob> JobsSince(int index)
    {
        lock (_sync)
        {
            var firstRetained = _count - _jobs.Count;
            var skip = Math.Max(0, index - firstRetained);
            return [.. _jobs.Skip(skip)];
        }
    }

    public ValueTask SendAsync(PrintJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_sync)
        {
            _jobs.Enqueue(job);
            _count++;
            while (_jobs.Count > MaxRetainedJobs)
            {
                _jobs.Dequeue();
            }
        }

        JobSent?.Invoke(job);
        return ValueTask.CompletedTask;
    }
}
