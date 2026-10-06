using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Unit.Test.Infrastructure;

using global::Flight;

// One polling loop per module: the Flight loop only ever processes the Flight outbox/inbox.
public class PerModulePersistMessageBackgroundServiceTests
{
    public sealed class OtherModuleRoot;

    [Fact]
    public async Task background_service_should_process_only_its_own_module_store()
    {
        var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var flightProcessor = Substitute.For<IPersistMessageProcessor<FlightRoot>>();
        flightProcessor
            .ProcessAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                processed.TrySetResult();
                return Task.CompletedTask;
            });
        var otherProcessor = Substitute.For<IPersistMessageProcessor<OtherModuleRoot>>();

        var services = new ServiceCollection();
        services.AddScoped<IPersistMessageProcessor<FlightRoot>>(_ => flightProcessor);
        services.AddScoped<IPersistMessageProcessor<OtherModuleRoot>>(_ => otherProcessor);
        using var provider = services.BuildServiceProvider();

        var service = new PersistMessageBackgroundService<FlightRoot>(
            NullLogger<PersistMessageBackgroundService<FlightRoot>>.Instance,
            provider,
            Options.Create(new PersistMessageOptions { Interval = 60 })
        );

        await service.StartAsync(CancellationToken.None);
        var completed = await Task.WhenAny(processed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        await service.StopAsync(CancellationToken.None);

        completed.Should().BeSameAs(processed.Task);
        await flightProcessor.Received().ProcessAllAsync(Arg.Any<CancellationToken>());
        await otherProcessor.DidNotReceive().ProcessAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task background_service_should_stop_when_cancelled()
    {
        var flightProcessor = Substitute.For<IPersistMessageProcessor<FlightRoot>>();

        var services = new ServiceCollection();
        services.AddScoped<IPersistMessageProcessor<FlightRoot>>(_ => flightProcessor);
        using var provider = services.BuildServiceProvider();

        var service = new PersistMessageBackgroundService<FlightRoot>(
            NullLogger<PersistMessageBackgroundService<FlightRoot>>.Instance,
            provider,
            Options.Create(new PersistMessageOptions { Interval = 60 })
        );

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        var stopped = await Task.WhenAny(service.ExecuteTask!, Task.Delay(TimeSpan.FromSeconds(10)));

        stopped.Should().BeSameAs(service.ExecuteTask);
    }
}
