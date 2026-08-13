using PandA.Sim;
using Xunit;

namespace PandA.Tests;

public sealed class LockProviderTests
{
    [Fact]
    public async Task AcquireAsync_SerializesSameResourceUntilDisposed()
    {
        var provider = new InMemoryLockProvider();
        await using var first = await provider.AcquireAsync("PA_Status");

        var waiter = provider.AcquireAsync("PA_Status", TimeSpan.FromSeconds(5)).AsTask();
        Assert.False(waiter.IsCompleted);

        await first.DisposeAsync();
        await using var second = await waiter;

        Assert.NotNull(second);
    }

    [Fact]
    public async Task AcquireAsync_DifferentResourcesDoNotBlock()
    {
        var provider = new InMemoryLockProvider();
        await using var first = await provider.AcquireAsync("LaneEval_L1");

        await using var second = await provider.AcquireAsync("LaneEval_L2", TimeSpan.FromMilliseconds(10));

        Assert.NotNull(second);
    }

    [Fact]
    public async Task AcquireAsync_TimeoutThrows()
    {
        var provider = new InMemoryLockProvider();
        await using var first = await provider.AcquireAsync("PA_Status");

        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await provider.AcquireAsync("PA_Status", TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public async Task AcquireAsync_EmptyResourceRejected()
    {
        var provider = new InMemoryLockProvider();

        await Assert.ThrowsAsync<ArgumentException>(async () => await provider.AcquireAsync(""));
    }
}
