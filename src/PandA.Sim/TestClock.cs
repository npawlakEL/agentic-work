using PandA.Core;

namespace PandA.Sim;

/// <summary>Deterministic clock for the simulator/tests; advanceable by callers.</summary>
public sealed class TestClock : IClock
{
    public TestClock(DateTimeOffset start) => UtcNow = start;

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
