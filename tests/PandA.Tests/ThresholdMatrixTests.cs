using PandA.Core.Verification;
using Xunit;

namespace PandA.Tests;

/// <summary>
/// Driver tests for <see cref="VerifyThresholdTracker"/> — the A8 → B3 rows of architecture-log 008.
/// Sequences start from a non-zero streak where relevant (senior review) and cover disabled thresholds,
/// pause persistence, threshold changes mid-stream, explicit reset, and per-line isolation.
/// </summary>
public sealed class ThresholdMatrixTests
{
    // ---- Sequence → final (count, pause) --------------------------------------------------------

    public static IEnumerable<object[]> SequenceCases()
    {
        // because, passFlags (true=pass), threshold, expectedFinalCount, expectedFinalPause
        yield return ["single fail below threshold", new[] { false }, 3, 1, false];
        yield return ["reach threshold pauses", new[] { false, false, false }, 3, 3, true];
        yield return ["fail,fail,pass resets", new[] { false, false, true }, 3, 0, false];
        yield return ["fail,fail,ignore-as-pass resets", new[] { false, false, true }, 3, 0, false];
        yield return ["pass never pauses", new[] { true, true }, 1, 0, false];
        yield return ["threshold<=0 accumulates but never pauses", new[] { false, false, false }, 0, 3, false];
        yield return ["pause stays true past threshold", new[] { false, false, false, false }, 2, 4, true];
        yield return ["recover after pause", new[] { false, false, true, false }, 2, 1, false];
    }

    [Theory]
    [MemberData(nameof(SequenceCases))]
    public void Sequence_ProducesExpectedCountAndPause(
        string because, bool[] passes, int threshold, int expectedCount, bool expectedPause)
    {
        var tracker = new VerifyThresholdTracker();
        VerifyThresholdResult last = new(0, false);

        foreach (var pass in passes)
        {
            last = tracker.Register("L1", pass, threshold);
        }

        Assert.True(expectedCount == last.ConsecutiveFailures,
            $"{because}: expected count {expectedCount} but was {last.ConsecutiveFailures}");
        Assert.True(expectedPause == last.PausePrinter,
            $"{because}: expected pause {expectedPause} but was {last.PausePrinter}");
    }

    [Fact]
    public void ThresholdChange_MidStream_ReusesExistingCount()
    {
        var tracker = new VerifyThresholdTracker();
        tracker.Register("L1", pass: false, failThreshold: 5); // count 1
        tracker.Register("L1", pass: false, failThreshold: 5); // count 2

        // Lowering the threshold to 2 on the next fail should pause using the accumulated count (3 >= 2).
        var result = tracker.Register("L1", pass: false, failThreshold: 2);

        Assert.Equal(3, result.ConsecutiveFailures);
        Assert.True(result.PausePrinter);
    }

    [Fact]
    public void Reset_ClearsCount_ButIsSeparateFromRegister()
    {
        var tracker = new VerifyThresholdTracker();
        tracker.Register("L1", pass: false, failThreshold: 3);
        tracker.Register("L1", pass: false, failThreshold: 3);
        Assert.Equal(2, tracker.CurrentCount("L1"));

        tracker.Reset("L1");

        Assert.Equal(0, tracker.CurrentCount("L1"));
    }

    [Fact]
    public void Lines_AreIsolated()
    {
        var tracker = new VerifyThresholdTracker();
        tracker.Register("L1", pass: false, failThreshold: 2);
        tracker.Register("L1", pass: false, failThreshold: 2); // L1 paused

        var l2 = tracker.Register("L2", pass: false, failThreshold: 2); // L2 fresh

        Assert.Equal(1, l2.ConsecutiveFailures);
        Assert.False(l2.PausePrinter);
        Assert.Equal(2, tracker.CurrentCount("L1"));
    }
}
