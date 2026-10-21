namespace PandA.Core.Concurrency;

/// <summary>A lease could not be acquired within the requested timeout (another host holds it).</summary>
public sealed class LockTimeoutException : TimeoutException
{
    public LockTimeoutException()
    {
    }

    public LockTimeoutException(string message)
        : base(message)
    {
    }

    public LockTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public LockTimeoutException(string resource, TimeSpan timeout)
        : base($"Timed out after {timeout} acquiring lease '{resource}'.")
    {
        Resource = resource;
    }

    public string? Resource { get; }
}

/// <summary>
/// An optimistic-concurrency check failed: the row changed (or appeared/disappeared) since it was read.
/// Adapters raise it when a conditional update affects 0 rows (decision-010 version CAS).
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
    {
    }

    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ConcurrencyConflictException(string key, long expectedVersion, long? actualVersion)
        : base(actualVersion is { } actual
            ? $"Concurrency conflict on '{key}': expected version {expectedVersion}, found {actual}."
            : $"Concurrency conflict on '{key}': expected version {expectedVersion}, but no row exists.")
    {
        Key = key;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    public string? Key { get; }

    public long ExpectedVersion { get; }

    public long? ActualVersion { get; }
}

/// <summary>An insert violated the unique-active-TuId rule: an active transport order already exists.</summary>
public sealed class DuplicateTransportOrderException : ConcurrencyConflictException
{
    public DuplicateTransportOrderException()
    {
    }

    public DuplicateTransportOrderException(string tuId)
        : base($"An active transport order for TuId '{tuId}' already exists.")
    {
        TuId = tuId;
    }

    public DuplicateTransportOrderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? TuId { get; }
}
