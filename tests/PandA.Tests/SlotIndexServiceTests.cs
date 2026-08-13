using System.Collections.Concurrent;
using PandA.Core.Slots;
using PandA.Sim;
using Xunit;

namespace PandA.Tests;

public sealed class SlotIndexServiceTests
{
    [Fact]
    public void NextSlot_CyclesAtThreeHundred()
    {
        var provider = new InMemorySlotIndexProvider(initialSlot: 299);
        var service = new SlotIndexService(provider);

        Assert.Equal(299, service.NextSlot());
        Assert.Equal(300, service.NextSlot());
        Assert.Equal(1, service.NextSlot());
        Assert.Equal(2, service.NextSlot());
    }

    [Fact]
    public void NextSlot_RejectsInvalidConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemorySlotIndexProvider(initialSlot: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemorySlotIndexProvider(maxSlots: 0));
    }

    [Fact]
    public async Task NextSlot_ConcurrentCallsDoNotDuplicateWithinCycle()
    {
        var provider = new InMemorySlotIndexProvider();
        var values = new ConcurrentBag<int>();

        await Parallel.ForEachAsync(Enumerable.Range(0, 300), (_, _) =>
        {
            values.Add(provider.NextSlot());
            return ValueTask.CompletedTask;
        });

        Assert.Equal(300, values.Distinct().Count());
        Assert.All(values, value => Assert.InRange(value, 1, 300));
    }
}
