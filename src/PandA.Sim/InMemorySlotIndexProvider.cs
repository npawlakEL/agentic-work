using PandA.Core.Slots;

namespace PandA.Sim;

/// <summary>
/// Sim slot provider: the source <c>CartonAssignSeq</c> 1..300 CYCLE as one atomic counter. Share a single
/// instance across simulated hosts — like the shared database sequence (decision-010), every host draws from
/// the same counter.
/// </summary>
public sealed class InMemorySlotIndexProvider : ISlotIndexProvider
{
    public const int SourceMaxSlots = SlotIndex.SourceMaxSlots;

    private readonly int _maxSlots;
    private int _next;

    public InMemorySlotIndexProvider(int initialSlot = 1, int maxSlots = SourceMaxSlots)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSlots, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(initialSlot, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initialSlot, maxSlots);
        _maxSlots = maxSlots;
        _next = initialSlot;
    }

    public ValueTask<int> NextSlotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // CAS loop — the same conditional-update pattern the eController adapter uses on its counter row.
        while (true)
        {
            var current = Volatile.Read(ref _next);
            var following = current >= _maxSlots ? 1 : current + 1;
            if (Interlocked.CompareExchange(ref _next, following, current) == current)
            {
                return ValueTask.FromResult(current);
            }
        }
    }
}
