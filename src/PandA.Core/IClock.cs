namespace PandA.Core;

/// <summary>Abstraction over the wall clock, so services stay deterministic under test.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
