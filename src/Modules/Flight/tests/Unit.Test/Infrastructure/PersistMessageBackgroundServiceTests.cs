using BuildingBlocks.PersistMessageProcessor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Unit.Test.Infrastructure;

// Each module's poller must only drain the outbox/inbox store owned by its own TModule.
public class PersistMessageBackgroundServiceTests
{
    public sealed class FlightModule;

    public sealed class PassengerModule;

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Messages.Add(formatter(state, exception));
    }

    private static IServiceProvider CreateProvider(
        IPersistMessageProcessor<FlightModule> flightProcessor,
        IPersistMessageProcessor<PassengerModule> passengerProcessor
    )
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => flightProcessor);
        services.AddScoped(_ => passengerProcessor);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task worker_for_a_module_processes_only_that_modules_store()
    {
        var flightProcessor = Substitute.For<IPersistMessageProcessor<FlightModule>>();
        var passengerProcessor = Substitute.For<IPersistMessageProcessor<PassengerModule>>();

        var processed = new TaskCompletionSource();
        flightProcessor
            .ProcessAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                processed.TrySetResult();
                return Task.CompletedTask;
            });

        var logger = new CapturingLogger<PersistMessageBackgroundService<FlightModule>>();
        var worker = new PersistMessageBackgroundService<FlightModule>(
            logger,
            CreateProvider(flightProcessor, passengerProcessor),
            Options.Create(new PersistMessageOptions { Interval = 30 })
        );

        await worker.StartAsync(CancellationToken.None);
        await processed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);

        await flightProcessor.Received().ProcessAllAsync(Arg.Any<CancellationToken>());
        await passengerProcessor.DidNotReceive().ProcessAllAsync(Arg.Any<CancellationToken>());

        Assert.Contains(
            logger.Messages,
            m => m.Contains("FlightModule", StringComparison.Ordinal) && m.Contains("Start", StringComparison.Ordinal)
        );
        Assert.Contains(
            logger.Messages,
            m => m.Contains("FlightModule", StringComparison.Ordinal) && m.Contains("Stop", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(logger.Messages, m => m.Contains("PassengerModule", StringComparison.Ordinal));
    }

    [Fact]
    public async Task worker_resolves_the_processor_from_a_fresh_scope_on_every_tick()
    {
        var flightProcessor = Substitute.For<IPersistMessageProcessor<FlightModule>>();
        var passengerProcessor = Substitute.For<IPersistMessageProcessor<PassengerModule>>();

        var ticks = 0;
        var secondTick = new TaskCompletionSource();
        flightProcessor
            .ProcessAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref ticks) >= 2)
                {
                    secondTick.TrySetResult();
                }

                return Task.CompletedTask;
            });

        var worker = new PersistMessageBackgroundService<FlightModule>(
            new CapturingLogger<PersistMessageBackgroundService<FlightModule>>(),
            CreateProvider(flightProcessor, passengerProcessor),
            Options.Create(new PersistMessageOptions { Interval = 1 })
        );

        await worker.StartAsync(CancellationToken.None);
        await secondTick.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);

        Assert.True(ticks >= 2);
        await passengerProcessor.DidNotReceive().ProcessAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task worker_fails_when_its_modules_processor_is_not_registered()
    {
        var passengerProcessor = Substitute.For<IPersistMessageProcessor<PassengerModule>>();

        var services = new ServiceCollection();
        services.AddScoped(_ => passengerProcessor);

        var worker = new PersistMessageBackgroundService<FlightModule>(
            new CapturingLogger<PersistMessageBackgroundService<FlightModule>>(),
            services.BuildServiceProvider(),
            Options.Create(new PersistMessageOptions { Interval = 1 })
        );

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        Assert.NotNull(worker.ExecuteTask);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ExecuteTask!);
        Assert.Contains(nameof(IPersistMessageProcessor<FlightModule>), exception.Message, StringComparison.Ordinal);
        await passengerProcessor.DidNotReceive().ProcessAllAsync(Arg.Any<CancellationToken>());
    }
}
