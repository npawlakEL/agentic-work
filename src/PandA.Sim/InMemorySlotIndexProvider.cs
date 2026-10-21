using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using PandA.Core.Slots;

namespace PandA.Sim;

/// <summary>
/// Sim slot provider: the source <c>CartonAssignSeq</c> 1..300 CYCLE as one atomic counter <b>per controller
/// key</b> (owner ruling 2026-10-21, R2). Share a single instance across simulated hosts — like the shared
/// database counter rows (decision-010), every host draws from the same per-controller counter.
/// </summary>
public sealed class InMemorySlotIndexProvider : ISlotIndexProvider
{
    public const int SourceMaxSlots = SlotIndex.SourceMaxSlots;

    private readonly int _maxSlots;
    private readonly int _initialSlot;
    private readonly ConcurrentDictionary<string, StrongBox<int>> _counters = new(StringComparer.OrdinalIgnoreCase);

    public InMemorySlotIndexProvider(int initialSlot = 1, int maxSlots = SourceMaxSlots)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSlots, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(initialSlot, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initialSlot, maxSlots);
        _maxSlots = maxSlots;
        _initialSlot = initialSlot;
    }

    public ValueTask<int> NextSlotAsync(string controllerKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controllerKey);
        cancellationToken.ThrowIfCancellationRequested();

        var counter = _counters.GetOrAdd(controllerKey, static (_, initial) => new StrongBox<int>(initial), _initialSlot);

        // CAS loop — the same conditional-update pattern the eController adapter uses on its counter row.
        while (true)
        {
            var current = Volatile.Read(ref counter.Value);
            var following = current >= _maxSlots ? 1 : current + 1;
            if (Interlocked.CompareExchange(ref counter.Value, following, current) == current)
            {
                return ValueTask.FromResult(current);
            }
        }
    }
}
