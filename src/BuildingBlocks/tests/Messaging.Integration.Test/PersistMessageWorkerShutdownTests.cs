using System.Collections.Concurrent;
using BuildingBlocks.PersistMessageProcessor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Messaging.Integration.Test;

public class PersistMessageWorkerShutdownTests
{
    [Fact]
    public async Task Shutdown_during_a_poll_stops_the_worker_without_logging_a_delivery_failure()
    {
        var processor = Substitute.For<IPersistMessageProcessor<ProbeEvent>>();
        var polling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        processor
            .ProcessAllAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                polling.TrySetResult();
                return Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
            });
        var logger = new RecordingLogger();
        await using var provider = new ServiceCollection().AddScoped(_ => processor).BuildServiceProvider();
        using var worker = new PersistMessageBackgroundService<ProbeEvent>(
            logger,
            provider,
            Options.Create(new PersistMessageOptions { Interval = 1 })
        );

        await worker.StartAsync(CancellationToken.None);
        await polling.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(worker.ExecuteTask);
        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
        Assert.DoesNotContain(LogLevel.Error, logger.Levels);
    }

    [Fact]
    public async Task Cancellation_not_caused_by_shutdown_is_logged_and_the_next_poll_still_runs()
    {
        var processor = Substitute.For<IPersistMessageProcessor<ProbeEvent>>();
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        processor
            .ProcessAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                    return Task.FromException(new OperationCanceledException("Broker publish timed out"));
                recovered.TrySetResult();
                return Task.CompletedTask;
            });
        var logger = new RecordingLogger();
        await using var provider = new ServiceCollection().AddScoped(_ => processor).BuildServiceProvider();
        using var worker = new PersistMessageBackgroundService<ProbeEvent>(
            logger,
            provider,
            Options.Create(new PersistMessageOptions { Interval = 1 })
        );

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains(LogLevel.Error, logger.Levels);
            Assert.False(worker.ExecuteTask!.IsCompleted);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class RecordingLogger : ILogger<PersistMessageBackgroundService<ProbeEvent>>
    {
        public ConcurrentQueue<LogLevel> Levels { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            System.Exception? exception,
            Func<TState, System.Exception?, string> formatter
        ) => Levels.Enqueue(logLevel);
    }
}
