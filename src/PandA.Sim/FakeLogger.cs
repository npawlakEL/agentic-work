using Microsoft.Extensions.Logging;

namespace PandA.Sim;

public sealed class FakeLogger<T> : ILogger<T>
{
    private readonly List<FakeLogEntry> _entries = [];

    public IReadOnlyList<FakeLogEntry> Entries => _entries;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull =>
        NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        var properties = state as IReadOnlyList<KeyValuePair<string, object?>>;
        _entries.Add(new FakeLogEntry(
            logLevel,
            eventId,
            formatter(state, exception),
            exception,
            properties is null ? [] : [.. properties]));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

public sealed record FakeLogEntry(
    LogLevel Level,
    EventId EventId,
    string Message,
    Exception? Exception,
    IReadOnlyList<KeyValuePair<string, object?>> Properties);
