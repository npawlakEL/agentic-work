using PandA.Core.Verification;
using Xunit;

namespace PandA.Tests;

public sealed class VerifyThresholdTrackerTests
{
    private const string Line = "LINE1";
    private readonly VerifyThresholdTracker _sut = new();

    [Fact]
    public void ConsecutiveFails_ReachingThreshold_SignalsPause()
    {
        Assert.False(_sut.Register(Line, pass: false, failThreshold: 3).PausePrinter);
        Assert.False(_sut.Register(Line, pass: false, failThreshold: 3).PausePrinter);

        var third = _sut.Register(Line, pass: false, failThreshold: 3);

        Assert.Equal(3, third.ConsecutiveFailures);
        Assert.True(third.PausePrinter);
    }

    [Fact]
    public void Pass_ResetsStreak()
    {
        _sut.Register(Line, pass: false, failThreshold: 3);
        _sut.Register(Line, pass: false, failThreshold: 3);

        var afterPass = _sut.Register(Line, pass: true, failThreshold: 3);

        Assert.Equal(0, afterPass.ConsecutiveFailures);
        Assert.False(afterPass.PausePrinter);
        Assert.Equal(0, _sut.CurrentCount(Line));
    }

    [Fact]
    public void PassBeforeThreshold_PreventsPause()
    {
        _sut.Register(Line, pass: false, failThreshold: 3);
        _sut.Register(Line, pass: false, failThreshold: 3);
        _sut.Register(Line, pass: true, failThreshold: 3);

        // Streak restarts; two more fails must not pause yet.
        _sut.Register(Line, pass: false, failThreshold: 3);
        var second = _sut.Register(Line, pass: false, failThreshold: 3);

        Assert.False(second.PausePrinter);
        Assert.Equal(2, second.ConsecutiveFailures);
    }

    [Fact]
    public void ThresholdZero_NeverPauses()
    {
        for (var i = 0; i < 10; i++)
        {
            Assert.False(_sut.Register(Line, pass: false, failThreshold: 0).PausePrinter);
        }
    }

    [Fact]
    public void CountsArePerLine()
    {
        _sut.Register("LINE_A", pass: false, failThreshold: 2);
        var otherLine = _sut.Register("LINE_B", pass: false, failThreshold: 2);

        Assert.Equal(1, otherLine.ConsecutiveFailures);
        Assert.False(otherLine.PausePrinter);
        Assert.Equal(1, _sut.CurrentCount("LINE_A"));
    }

    [Fact]
    public void StaysPaused_WhileFailsContinue()
    {
        _sut.Register(Line, pass: false, failThreshold: 2);
        Assert.True(_sut.Register(Line, pass: false, failThreshold: 2).PausePrinter);
        Assert.True(_sut.Register(Line, pass: false, failThreshold: 2).PausePrinter);
    }

    [Fact]
    public void Reset_ClearsCount()
    {
        _sut.Register(Line, pass: false, failThreshold: 3);
        _sut.Reset(Line);

        Assert.Equal(0, _sut.CurrentCount(Line));
    }

    [Fact]
    public void ResetOnTrip_RefreshesCounterToZeroWhenPausing()
    {
        var tracker = new VerifyThresholdTracker(resetOnTrip: true);

        Assert.False(tracker.Register(Line, pass: false, failThreshold: 2).PausePrinter); // count 1
        var trip = tracker.Register(Line, pass: false, failThreshold: 2);                 // count 2 → pause + refresh

        Assert.True(trip.PausePrinter);
        Assert.Equal(2, trip.ConsecutiveFailures);   // reported streak that tripped it
        Assert.Equal(0, tracker.CurrentCount(Line));  // internal counter refreshed
        // Next single fail starts a fresh window and does NOT immediately re-pause.
        Assert.False(tracker.Register(Line, pass: false, failThreshold: 2).PausePrinter);
    }
}
