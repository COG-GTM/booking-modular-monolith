using Api.Extensions;
using Booking;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using Flight;
using Flight.Flights.Features.CreatingFlight.V1;
using FluentAssertions;
using Identity;
using Microsoft.Extensions.DependencyInjection;
using Passenger;
using Xunit;

namespace ServiceHost.Unit.Test;

public class ApiEventMapperExtensionsTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<FlightEventMapper>();
        services.AddScoped<IdentityEventMapper>();
        services.AddScoped<PassengerEventMapper>();
        services.AddScoped<BookingEventMapper>();

        services.AddApiEventMappers();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void add_api_event_mappers_should_resolve_composite_event_mapper()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var mapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        mapper.Should().BeOfType<CompositeEventMapper>();
    }

    [Fact]
    public void composite_mapper_should_delegate_to_module_mapper_for_known_event()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        var flightId = Guid.NewGuid();
        var domainEvent = new FlightCreatedDomainEvent(
            flightId,
            "BA123",
            Guid.NewGuid(),
            DateTime.UtcNow,
            Guid.NewGuid(),
            DateTime.UtcNow.AddHours(2),
            Guid.NewGuid(),
            120m,
            DateTime.UtcNow,
            global::Flight.Flights.Enums.FlightStatus.Flying,
            100m,
            false
        );

        var integrationEvent = mapper.MapToIntegrationEvent(domainEvent);

        integrationEvent.Should().BeOfType<FlightCreated>().Which.Id.Should().Be(flightId);
    }

    [Fact]
    public void composite_mapper_should_return_null_for_unmapped_event()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var mapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        var integrationEvent = mapper.MapToIntegrationEvent(new UnknownDomainEvent());
        var internalCommand = mapper.MapToInternalCommand(new UnknownDomainEvent());

        integrationEvent.Should().BeNull();
        internalCommand.Should().BeNull();
    }

    private sealed record UnknownDomainEvent : IDomainEvent;
}
