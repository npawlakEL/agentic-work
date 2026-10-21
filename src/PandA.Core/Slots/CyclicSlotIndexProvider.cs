namespace PandA.Core.Slots;

/// <summary>
/// In-process <see cref="ISlotIndexProvider"/>: a lock-free 1..<c>maxSlots</c> cycle mirroring the source
/// <c>CartonAssignSeq</c> behavior (1..300, wrapping). Single-host default; a multi-host deployment swaps in a
/// shared adapter behind the same port.
/// </summary>
public sealed class CyclicSlotIndexProvider : ISlotIndexProvider
{
    public const int SourceMaxSlots = 300;

    private readonly int _maxSlots;
    private int _nextSlot;

    public CyclicSlotIndexProvider(int initialSlot = 1, int maxSlots = SourceMaxSlots)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSlots, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(initialSlot, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initialSlot, maxSlots);

        _maxSlots = maxSlots;
        _nextSlot = initialSlot;
    }

    public ValueTask<int> NextSlotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            var current = Volatile.Read(ref _nextSlot);
            var next = current == _maxSlots ? 1 : current + 1;
            if (Interlocked.CompareExchange(ref _nextSlot, next, current) == current)
            {
                return ValueTask.FromResult(current);
            }
        }
    }
}
