using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Unit.Test.Support;

public sealed record LogEntry(LogLevel Level, string Message);

public class RecordingLogger : ILogger
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        System.Exception? exception,
        Func<TState, System.Exception?, string> formatter
    ) => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
}

public sealed class RecordingLogger<T> : RecordingLogger, ILogger<T>;
