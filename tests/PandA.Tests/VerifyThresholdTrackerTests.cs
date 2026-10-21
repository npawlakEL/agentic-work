using PandA.Core.Verification;
using Xunit;

namespace PandA.Tests;

public sealed class VerifyThresholdTrackerTests
{
    private const string Line = "LINE1";
    private readonly VerifyThresholdTracker _sut = TestServices.Threshold();

    [Fact]
    public async Task ConsecutiveFails_ReachingThreshold_SignalsPause()
    {
        Assert.False((await _sut.RegisterAsync(Line, pass: false, failThreshold: 3)).PausePrinter);
        Assert.False((await _sut.RegisterAsync(Line, pass: false, failThreshold: 3)).PausePrinter);

        var third = (await _sut.RegisterAsync(Line, pass: false, failThreshold: 3));

        Assert.Equal(3, third.ConsecutiveFailures);
        Assert.True(third.PausePrinter);
    }

    [Fact]
    public async Task Pass_ResetsStreak()
    {
        await _sut.RegisterAsync(Line, pass: false, failThreshold: 3);
        await _sut.RegisterAsync(Line, pass: false, failThreshold: 3);

        var afterPass = (await _sut.RegisterAsync(Line, pass: true, failThreshold: 3));

        Assert.Equal(0, afterPass.ConsecutiveFailures);
        Assert.False(afterPass.PausePrinter);
        Assert.Equal(0, (await _sut.CurrentCountAsync(Line)));
    }

    [Fact]
    public async Task PassBeforeThreshold_PreventsPause()
    {
        await _sut.RegisterAsync(Line, pass: false, failThreshold: 3);
        await _sut.RegisterAsync(Line, pass: false, failThreshold: 3);
        await _sut.RegisterAsync(Line, pass: true, failThreshold: 3);

        // Streak restarts; two more fails must not pause yet.
        await _sut.RegisterAsync(Line, pass: false, failThreshold: 3);
        var second = (await _sut.RegisterAsync(Line, pass: false, failThreshold: 3));

        Assert.False(second.PausePrinter);
        Assert.Equal(2, second.ConsecutiveFailures);
    }

    [Fact]
    public async Task ThresholdZero_NeverPauses()
    {
        for (var i = 0; i < 10; i++)
        {
            Assert.False((await _sut.RegisterAsync(Line, pass: false, failThreshold: 0)).PausePrinter);
        }
    }

    [Fact]
    public async Task CountsArePerLine()
    {
        await _sut.RegisterAsync("LINE_A", pass: false, failThreshold: 2);
        var otherLine = (await _sut.RegisterAsync("LINE_B", pass: false, failThreshold: 2));

        Assert.Equal(1, otherLine.ConsecutiveFailures);
        Assert.False(otherLine.PausePrinter);
        Assert.Equal(1, (await _sut.CurrentCountAsync("LINE_A")));
    }

    [Fact]
    public async Task ResetsOnTrip_ThenReTripsAfterThresholdMoreFails()
    {
        // Source VerifyThreshold_Update: ~PP then Refresh resets the count (owner ruling 2026-10-21).
        await _sut.RegisterAsync(Line, pass: false, failThreshold: 2);
        Assert.True((await _sut.RegisterAsync(Line, pass: false, failThreshold: 2)).PausePrinter);
        Assert.False((await _sut.RegisterAsync(Line, pass: false, failThreshold: 2)).PausePrinter);
        Assert.True((await _sut.RegisterAsync(Line, pass: false, failThreshold: 2)).PausePrinter);
    }

    [Fact]
    public async Task WithoutResetOnTrip_StaysPaused_WhileFailsContinue()
    {
        var sut = TestServices.Threshold(resetOnTrip: false);
        await sut.RegisterAsync(Line, pass: false, failThreshold: 2);
        Assert.True((await sut.RegisterAsync(Line, pass: false, failThreshold: 2)).PausePrinter);
        Assert.True((await sut.RegisterAsync(Line, pass: false, failThreshold: 2)).PausePrinter);
    }

    [Fact]
    public async Task Reset_ClearsCount()
    {
        await _sut.RegisterAsync(Line, pass: false, failThreshold: 3);
        await _sut.ResetAsync(Line);

        Assert.Equal(0, (await _sut.CurrentCountAsync(Line)));
    }

    [Fact]
    public async Task ResetOnTrip_RefreshesCounterToZeroWhenPausing()
    {
        var tracker = TestServices.Threshold(resetOnTrip: true);

        Assert.False((await tracker.RegisterAsync(Line, pass: false, failThreshold: 2)).PausePrinter); // count 1
        var trip = (await tracker.RegisterAsync(Line, pass: false, failThreshold: 2));                 // count 2 → pause + refresh

        Assert.True(trip.PausePrinter);
        Assert.Equal(2, trip.ConsecutiveFailures);   // reported streak that tripped it
        Assert.Equal(0, (await tracker.CurrentCountAsync(Line)));  // internal counter refreshed
        // Next single fail starts a fresh window and does NOT immediately re-pause.
        Assert.False((await tracker.RegisterAsync(Line, pass: false, failThreshold: 2)).PausePrinter);
    }
}
