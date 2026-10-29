using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests;

public class PersistMessageBackgroundServiceTests
{
    [Fact]
    public async Task background_service_processes_only_its_module_processor()
    {
        var services = new ServiceCollection();
        var first = Substitute.For<IPersistMessageProcessor<First>>();
        var second = Substitute.For<IPersistMessageProcessor<Second>>();
        services.AddScoped<IPersistMessageProcessor<First>>(_ => first);
        services.AddScoped<IPersistMessageProcessor<Second>>(_ => second);
        await using var provider = services.BuildServiceProvider();
        var service = new PersistMessageBackgroundService<First>(
            NullLogger<PersistMessageBackgroundService<First>>.Instance,
            provider,
            Options.Create(new PersistMessageOptions { Interval = 1 })
        );

        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (
                !first
                    .ReceivedCalls()
                    .Any(call => call.GetMethodInfo().Name == nameof(IPersistMessageProcessor.ProcessAllAsync))
                && DateTime.UtcNow < deadline
            )
                await Task.Delay(10);

            first
                .ReceivedCalls()
                .Should()
                .Contain(call => call.GetMethodInfo().Name == nameof(IPersistMessageProcessor.ProcessAllAsync));
            second.ReceivedCalls().Should().BeEmpty();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    public sealed class First;

    public sealed class Second;
}
