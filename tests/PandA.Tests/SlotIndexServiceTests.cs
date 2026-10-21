using System.Collections.Concurrent;
using PandA.Core.Slots;
using PandA.Sim;
using Xunit;

namespace PandA.Tests;

public sealed class SlotIndexServiceTests
{
    [Fact]
    public async Task NextSlot_CyclesAtThreeHundred()
    {
        var provider = new InMemorySlotIndexProvider(initialSlot: 299);
        var service = new SlotIndexService(provider);

        Assert.Equal(299, await service.NextSlotAsync());
        Assert.Equal(300, await service.NextSlotAsync());
        Assert.Equal(1, await service.NextSlotAsync());
        Assert.Equal(2, await service.NextSlotAsync());
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

        await Parallel.ForEachAsync(Enumerable.Range(0, 300), async (_, ct) =>
            values.Add(await provider.NextSlotAsync(ct)));

        Assert.Equal(300, values.Distinct().Count());
        Assert.All(values, value => Assert.InRange(value, 1, 300));
    }
}
