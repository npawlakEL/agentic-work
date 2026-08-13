using PandA.Core;

namespace PandA.Sim.Line;

/// <summary>Advanceable simulation clock used by real Core services during line playback.</summary>
public sealed class SimClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = start;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
