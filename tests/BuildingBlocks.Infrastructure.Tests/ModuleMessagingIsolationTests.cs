using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Infrastructure.Tests;

public class ModuleMessagingIsolationTests
{
    [Fact]
    public async Task ModuleEventDispatcher_forwards_batch_and_single_sends_to_the_inner_dispatcher()
    {
        var inner = Substitute.For<IEventDispatcher>();
        var dispatcher = new ModuleEventDispatcher<FlightModule>(inner);
        IReadOnlyList<IDomainEvent> events = [new SeatReserved()];
        var @event = new SeatReserved();
        using var cancellation = new CancellationTokenSource();
        var commandType = typeof(ConfirmSeat);

        await dispatcher.SendAsync<IDomainEvent>(events, commandType, cancellation.Token);
        await dispatcher.SendAsync<IDomainEvent>(@event, commandType, cancellation.Token);

        await inner.Received(1).SendAsync<IDomainEvent>(events, commandType, cancellation.Token);
        await inner.Received(1).SendAsync<IDomainEvent>(@event, commandType, cancellation.Token);
    }

    [Fact]
    public async Task Module_dispatcher_publishes_only_through_its_own_modules_processor()
    {
        var builder = CreateModuleBuilder();
        var headers = Substitute.For<IEventHeadersProvider>();
        headers.GetHeaders().Returns(new Dictionary<string, object?> { ["test-header"] = "test-value" });
        builder.Services.AddSingleton(headers);

        var flightProcessor = Substitute.For<IPersistMessageProcessor<FlightModule>>();
        var passengerProcessor = Substitute.For<IPersistMessageProcessor<PassengerModule>>();
        builder.Services.AddSingleton<IPersistMessageProcessor<FlightModule>>(flightProcessor);
        builder.Services.AddSingleton<IPersistMessageProcessor<PassengerModule>>(passengerProcessor);

        await using var provider = builder.Services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher<FlightModule>>();

        Assert.IsType<ModuleEventDispatcher<FlightModule>>(dispatcher);
        Assert.Null(provider.GetService<IEventDispatcher>());

        await dispatcher.SendAsync<IDomainEvent>(new SeatReserved(), typeof(ConfirmSeat));

        await flightProcessor
            .Received(1)
            .PublishMessageAsync(
                Arg.Is<MessageEnvelope>(message =>
                    message.Message is SeatReservedIntegration
                    && message.Headers.ContainsKey("test-header")
                    && (string?)message.Headers["test-header"] == "test-value"
                ),
                Arg.Any<CancellationToken>()
            );
        await flightProcessor
            .Received(1)
            .AddInternalMessageAsync<IInternalCommand>(Arg.Is<ConfirmSeat>(_ => true), Arg.Any<CancellationToken>());
        Assert.Empty(passengerProcessor.ReceivedCalls());
    }

    [Fact]
    public void Each_module_registration_resolves_its_own_processor_and_store()
    {
        var builder = CreateModuleBuilder();
        var flightProcessor = Substitute.For<IPersistMessageProcessor<FlightModule>>();
        var passengerProcessor = Substitute.For<IPersistMessageProcessor<PassengerModule>>();
        builder.Services.AddSingleton<IPersistMessageProcessor<FlightModule>>(flightProcessor);
        builder.Services.AddSingleton<IPersistMessageProcessor<PassengerModule>>(passengerProcessor);

        using var provider = builder.Services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var registrations = provider.GetServices<ModulePersistenceRegistration>().ToArray();

        Assert.Equal(2, registrations.Length);
        Assert.Contains(
            registrations,
            registration => registration.ModuleName == "Flight" && registration.ModuleType == typeof(FlightModule)
        );
        Assert.Contains(
            registrations,
            registration => registration.ModuleName == "Passenger" && registration.ModuleType == typeof(PassengerModule)
        );

        var flightRegistration = registrations.Single(registration => registration.ModuleType == typeof(FlightModule));
        var passengerRegistration = registrations.Single(registration =>
            registration.ModuleType == typeof(PassengerModule)
        );
        Assert.Same(flightProcessor, flightRegistration.ResolveProcessor(scope.ServiceProvider));
        Assert.Same(passengerProcessor, passengerRegistration.ResolveProcessor(scope.ServiceProvider));

        var flightContext = Assert.IsType<PersistMessageDbContext<FlightModule>>(
            flightRegistration.ResolveDbContext(scope.ServiceProvider)
        );
        var passengerContext = Assert.IsType<PersistMessageDbContext<PassengerModule>>(
            passengerRegistration.ResolveDbContext(scope.ServiceProvider)
        );
        Assert.Equal("Host=localhost;Database=flight", flightContext.Database.GetConnectionString());
        Assert.Equal("Host=localhost;Database=passenger", passengerContext.Database.GetConnectionString());
    }

    [Fact]
    public async Task Background_worker_polls_only_its_own_modules_processor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<PersistMessageOptions>(options => options.Interval = 1);
        var processed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var flightProcessor = Substitute.For<IPersistMessageProcessor<FlightModule>>();
        flightProcessor
            .ProcessAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                processed.TrySetResult(true);
                return Task.CompletedTask;
            });
        var passengerProcessor = Substitute.For<IPersistMessageProcessor<PassengerModule>>();
        services.AddSingleton<IPersistMessageProcessor<FlightModule>>(flightProcessor);
        services.AddSingleton<IPersistMessageProcessor<PassengerModule>>(passengerProcessor);

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        using var worker = ActivatorUtilities.CreateInstance<PersistMessageBackgroundService<FlightModule>>(provider);
        await worker.StartAsync(CancellationToken.None);
        await processed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);

        Assert.NotEmpty(flightProcessor.ReceivedCalls());
        Assert.Empty(passengerProcessor.ReceivedCalls());
    }

    private static WebApplicationBuilder CreateModuleBuilder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["PersistMessageOptions:ConnectionString:Flight"] = "Host=localhost;Database=flight";
        builder.Configuration["PersistMessageOptions:ConnectionString:Passenger"] = "Host=localhost;Database=passenger";
        builder.Configuration["Modules:Flight:BackgroundProcessingEnabled"] = "false";
        builder.Configuration["Modules:Passenger:BackgroundProcessingEnabled"] = "false";
        builder.AddPersistMessageProcessor<FlightModule>("Flight");
        builder.AddPersistMessageProcessor<PassengerModule>("Passenger");
        builder.Services.AddEventMapper<SeatMapper>();
        return builder;
    }

    public sealed class FlightModule { }

    public sealed class PassengerModule { }
}
