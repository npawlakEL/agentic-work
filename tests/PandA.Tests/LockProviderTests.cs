using PandA.Core.Concurrency;
using PandA.Sim;
using Xunit;

namespace PandA.Tests;

/// <summary>Contract tests for the shared lease lock (<see cref="InMemoryLeaseLockProvider"/>, decision-010).</summary>
public sealed class LockProviderTests
{
    [Fact]
    public async Task AcquireAsync_SerializesSameResourceUntilDisposed()
    {
        var provider = new InMemoryLeaseLockProvider();
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
        var provider = new InMemoryLeaseLockProvider();
        await using var first = await provider.AcquireAsync("LaneEval_L1");

        await using var second = await provider.AcquireAsync("LaneEval_L2", TimeSpan.FromMilliseconds(10));

        Assert.NotNull(second);
    }

    [Fact]
    public async Task AcquireAsync_TimeoutThrows_LockTimeoutException()
    {
        var provider = new InMemoryLeaseLockProvider();
        await using var first = await provider.AcquireAsync("PA_Status");

        var ex = await Assert.ThrowsAsync<LockTimeoutException>(async () =>
            await provider.AcquireAsync("PA_Status", TimeSpan.FromMilliseconds(20)));
        Assert.Equal("PA_Status", ex.Resource);
    }

    [Fact]
    public async Task AcquireAsync_EmptyResourceRejected()
    {
        var provider = new InMemoryLeaseLockProvider();

        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await provider.AcquireAsync(""));
    }

    [Fact]
    public async Task Lease_IsNotReentrant_EvenForTheSameHost()
    {
        var provider = new InMemoryLeaseLockProvider();
        await using var first = await provider.AcquireAsync("Carton_X");

        await Assert.ThrowsAsync<LockTimeoutException>(async () =>
            await provider.AcquireAsync("Carton_X", TimeSpan.Zero));
    }

    [Fact]
    public async Task Hosts_ShareTheLeaseTable_AndOwnersAreDistinct()
    {
        var hostA = new InMemoryLeaseLockProvider(hostId: "A");
        var hostB = hostA.ForHost("B");

        await using (var lease = await hostA.AcquireAsync("Carton_X"))
        {
            Assert.StartsWith("A:", lease.OwnerId, StringComparison.Ordinal);
            Assert.Equal(lease.OwnerId, hostB.CurrentOwner("Carton_X"));
            await Assert.ThrowsAsync<LockTimeoutException>(async () =>
                await hostB.AcquireAsync("Carton_X", TimeSpan.FromMilliseconds(20)));
        }

        await using var taken = await hostB.AcquireAsync("Carton_X", TimeSpan.FromSeconds(1));
        Assert.StartsWith("B:", taken.OwnerId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpiredLease_CanBeTakenOver_AndStaleHolderCannotRenewOrRelease()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var hostA = new InMemoryLeaseLockProvider(time, TimeSpan.FromSeconds(30), "A");
        var hostB = hostA.ForHost("B");

        var crashed = await hostA.AcquireAsync("Carton_X");
        time.Advance(TimeSpan.FromSeconds(31));

        await using var takeover = await hostB.AcquireAsync("Carton_X", TimeSpan.Zero);
        Assert.False(await crashed.RenewAsync());

        await crashed.DisposeAsync(); // must not release B's lease
        Assert.Equal(takeover.OwnerId, hostB.CurrentOwner("Carton_X"));
    }

    [Fact]
    public async Task Renew_ExtendsExpiry_WhileStillOwner()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var provider = new InMemoryLeaseLockProvider(time, TimeSpan.FromSeconds(30));
        await using var lease = await provider.AcquireAsync("Carton_X");
        var firstExpiry = lease.ExpiresUtc;

        time.Advance(TimeSpan.FromSeconds(20));
        Assert.True(await lease.RenewAsync());
        time.Advance(TimeSpan.FromSeconds(20)); // past the original expiry, inside the renewed one

        Assert.True(lease.ExpiresUtc > firstExpiry);
        await Assert.ThrowsAsync<LockTimeoutException>(async () =>
            await provider.ForHost("B").AcquireAsync("Carton_X", TimeSpan.Zero));
    }
}