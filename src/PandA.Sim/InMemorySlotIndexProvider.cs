using PandA.Core.Slots;

namespace PandA.Sim;

/// <summary>Sim slot provider: the source <c>CartonAssignSeq</c> 1..300 CYCLE, in memory.</summary>
public sealed class InMemorySlotIndexProvider : ISlotIndexProvider
{
    public const int SourceMaxSlots = CyclicSlotIndexProvider.SourceMaxSlots;

    private readonly CyclicSlotIndexProvider _inner;

    public InMemorySlotIndexProvider(int initialSlot = 1, int maxSlots = SourceMaxSlots)
    {
        _inner = new CyclicSlotIndexProvider(initialSlot, maxSlots);
    }

    public ValueTask<int> NextSlotAsync(CancellationToken cancellationToken = default) =>
        _inner.NextSlotAsync(cancellationToken);
}
