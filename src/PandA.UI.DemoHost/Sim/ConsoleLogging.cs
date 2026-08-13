using Microsoft.Extensions.Logging;

namespace PandA.UI.DemoHost.Sim;

/// <summary>Thread-safe collector for the log lines emitted during a single console run.</summary>
internal sealed class ConsoleLogSink
{
    private readonly List<ConsoleLogLine> _lines = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<ConsoleLogLine> Lines
    {
        get
        {
            lock (_gate)
            {
                return _lines.ToList();
            }
        }
    }

    public void Add(ConsoleLogLine line)
    {
        lock (_gate)
        {
            _lines.Add(line);
        }
    }
}

/// <summary>An <see cref="ILoggerProvider"/> that funnels every category into a shared <see cref="ConsoleLogSink"/>.</summary>
internal sealed class SinkLoggerProvider(ConsoleLogSink sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new SinkLogger(categoryName, sink);

    public void Dispose()
    {
    }
}

internal sealed class SinkLogger(string category, ConsoleLogSink sink) : ILogger
{
    private static readonly string[] LevelNames = ["TRCE", "DBUG", "INFO", "WARN", "FAIL", "CRIT", "NONE"];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(formatter);
        var message = formatter(state, exception);
        if (exception is not null)
        {
            message = $"{message} — {exception.Message}";
        }

        var shortCategory = category.Contains('.') ? category[(category.LastIndexOf('.') + 1)..] : category;
        var level = LevelNames[(int)logLevel];
        sink.Add(new ConsoleLogLine(level, shortCategory, message));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
