using PandA.Core.Verification;

namespace PandA.Sim;

/// <summary>
/// The simulated shared eController database (decision-010): one instance of every shared table/counter.
/// Every simulated host and line composed over the same <see cref="SimSharedDatabase"/> sees the same rows,
/// leases, run ids, slot sequence, printer state and verify-fail counters — exactly as separate PandA
/// processes would against the real database.
/// </summary>
public sealed class SimSharedDatabase
{
    public SimSharedDatabase(TimeProvider? time = null, TimeSpan? leaseDuration = null)
    {
        Locks = new InMemoryLeaseLockProvider(time, leaseDuration, hostId: "sim");
    }

    public InMemoryTransportOrderStore Orders { get; } = new();

    public InMemoryCartonRunRepository Runs { get; } = new();

    /// <summary>The shared lease table; use <see cref="InMemoryLeaseLockProvider.ForHost"/> for a per-host view.</summary>
    public InMemoryLeaseLockProvider Locks { get; }

    public InMemorySlotIndexProvider Slots { get; } = new();

    public InMemoryPrinterStateStore PrinterStates { get; } = new();

    public InMemoryVerifyFailCounterStore VerifyFailCounters { get; } = new();

    /// <summary>A verify-threshold tracker over the shared counters.</summary>
    public IVerifyThresholdTracker CreateThresholdTracker(bool resetOnTrip = true) =>
        new VerifyThresholdTracker(VerifyFailCounters, resetOnTrip);
}
