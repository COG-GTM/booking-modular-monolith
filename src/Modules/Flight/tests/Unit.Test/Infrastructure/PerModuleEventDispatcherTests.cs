using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Unit.Test.Infrastructure;

// IEventDispatcher<TModule> must write to the outbox owned by TModule and nothing else.
public class PerModuleEventDispatcherTests
{
    public sealed class FlightModule;

    public sealed class PassengerModule;

    private sealed record FakeIntegrationEvent(string Name) : IIntegrationEvent;

    private sealed record FakeDomainEvent(string Name) : IDomainEvent;

    [Fact]
    public async Task dispatcher_for_a_module_publishes_through_that_modules_processor()
    {
        var flightProcessor = Substitute.For<IPersistMessageProcessor<FlightModule>>();
        var eventMapper = Substitute.For<IEventMapper>();
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var dispatcher = new EventDispatcher<FlightModule>(
            scopeFactory,
            eventMapper,
            NullLogger<EventDispatcher<FlightModule>>.Instance,
            flightProcessor,
            Substitute.For<IHttpContextAccessor>()
        );

        var @event = new FakeIntegrationEvent("flight-created");
        await dispatcher.SendAsync(@event);

        await flightProcessor
            .Received(1)
            .PublishMessageAsync(
                Arg.Is<MessageEnvelope>(envelope => ReferenceEquals(envelope.Message, @event)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task dispatcher_maps_domain_events_with_the_module_event_mapper()
    {
        var flightProcessor = Substitute.For<IPersistMessageProcessor<FlightModule>>();
        var integrationEvent = new FakeIntegrationEvent("mapped");
        var eventMapper = Substitute.For<IEventMapper>();
        eventMapper.MapToIntegrationEvent(Arg.Any<IDomainEvent>()).Returns(integrationEvent);

        var dispatcher = new EventDispatcher<FlightModule>(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            eventMapper,
            NullLogger<EventDispatcher<FlightModule>>.Instance,
            flightProcessor,
            Substitute.For<IHttpContextAccessor>()
        );

        await dispatcher.SendAsync(new FakeDomainEvent("domain"));

        await flightProcessor
            .Received(1)
            .PublishMessageAsync(
                Arg.Is<MessageEnvelope>(envelope => ReferenceEquals(envelope.Message, integrationEvent)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task registered_dispatchers_are_bound_to_their_own_modules_processor()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["PersistMessageOptions:ConnectionString:Flight"] = "Server=localhost;Database=flight_persist",
                ["PersistMessageOptions:ConnectionString:Passenger"] = "Server=localhost;Database=passenger_persist",
            }
        );

        builder.AddPersistMessageProcessor<FlightModule>("Flight");
        builder.AddPersistMessageProcessor<PassengerModule>("Passenger");

        var flightProcessor = Substitute.For<IPersistMessageProcessor<FlightModule>>();
        var passengerProcessor = Substitute.For<IPersistMessageProcessor<PassengerModule>>();

        // The real processors need a live Postgres store; swap them so only the wiring is exercised.
        builder.Services.AddScoped(_ => flightProcessor);
        builder.Services.AddScoped(_ => passengerProcessor);
        builder.Services.AddSingleton(Substitute.For<IEventMapper>());
        builder.Services.AddHttpContextAccessor();

        await using var provider = builder.Services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var flightDispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher<FlightModule>>();
        var passengerDispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher<PassengerModule>>();

        Assert.IsType<EventDispatcher<FlightModule>>(flightDispatcher);
        Assert.IsType<EventDispatcher<PassengerModule>>(passengerDispatcher);
        Assert.Null(scope.ServiceProvider.GetService<IEventDispatcher>());

        await flightDispatcher.SendAsync(new FakeIntegrationEvent("flight"));

        await flightProcessor.Received(1).PublishMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
        await passengerProcessor
            .DidNotReceive()
            .PublishMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());

        await passengerDispatcher.SendAsync(new FakeIntegrationEvent("passenger"));

        await passengerProcessor
            .Received(1)
            .PublishMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
        await flightProcessor.Received(1).PublishMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void persist_message_options_are_registered_once_for_many_modules()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["PersistMessageOptions:ConnectionString:Flight"] = "Server=localhost;Database=flight_persist",
                ["PersistMessageOptions:ConnectionString:Passenger"] = "Server=localhost;Database=passenger_persist",
                ["PersistMessageOptions:Interval"] = "7",
            }
        );

        builder.AddPersistMessageProcessor<FlightModule>("Flight");
        builder.AddPersistMessageProcessor<PassengerModule>("Passenger");

        Assert.Single(builder.Services, d => d.ServiceType == typeof(PersistMessageOptions));

        using var provider = builder.Services.BuildServiceProvider();
        Assert.Equal(7, provider.GetRequiredService<IOptions<PersistMessageOptions>>().Value.Interval);
        Assert.Equal(7, provider.GetRequiredService<PersistMessageOptions>().Interval);
    }

    [Fact]
    public void persist_message_connection_string_prefers_aspire_then_module_options()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:flight-persist-message"] = "Server=aspire;Database=flight",
                    ["PersistMessageOptions:ConnectionString:Flight"] = "Server=options;Database=flight",
                    ["PersistMessageOptions:ConnectionString:Passenger"] = "Server=options;Database=passenger",
                }
            )
            .Build();

        Assert.Equal(
            "Server=aspire;Database=flight",
            Extensions.GetPersistMessageConnectionString(configuration, "Flight")
        );
        Assert.Equal(
            "Server=options;Database=passenger",
            Extensions.GetPersistMessageConnectionString(configuration, "Passenger")
        );

        var exception = Assert.Throws<ArgumentException>(
            () => Extensions.GetPersistMessageConnectionString(configuration, "Booking")
        );
        Assert.Contains("PersistMessageOptions:ConnectionString:Booking", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings:booking-persist-message", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void module_name_is_required_for_a_persist_message_store()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });

        Assert.Throws<ArgumentException>(() => builder.AddPersistMessageProcessor<FlightModule>(""));
        Assert.Throws<ArgumentNullException>(() => builder.AddPersistMessageProcessor<FlightModule>(null!));
    }
}
