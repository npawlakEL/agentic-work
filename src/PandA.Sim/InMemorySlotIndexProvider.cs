using PandA.Core.Slots;

namespace PandA.Sim;

public sealed class InMemorySlotIndexProvider : ISlotIndexProvider
{
    public const int SourceMaxSlots = 300;

    private readonly int _maxSlots;
    private int _nextSlot;

    public InMemorySlotIndexProvider(int initialSlot = 1, int maxSlots = SourceMaxSlots)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSlots, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(initialSlot, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initialSlot, maxSlots);

        _maxSlots = maxSlots;
        _nextSlot = initialSlot;
    }

    public int NextSlot()
    {
        while (true)
        {
            var current = Volatile.Read(ref _nextSlot);
            var next = current == _maxSlots ? 1 : current + 1;
            if (Interlocked.CompareExchange(ref _nextSlot, next, current) == current)
            {
                return current;
            }
        }
    }
}
