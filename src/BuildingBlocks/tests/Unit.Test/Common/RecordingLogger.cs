using Microsoft.Extensions.Logging;

namespace Unit.Test.Common;

public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        System.Exception? exception,
        Func<TState, System.Exception?, string> formatter
    )
    {
        Entries.Add((logLevel, formatter(state, exception)));
    }
}
