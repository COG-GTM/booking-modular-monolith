using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using global::Flight;
using global::Flight.Flights.Features.CreatingFlight.V1;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Unit.Test.Flight;

public class FlightEventMapperRegistrationTests
{
    private sealed record UnknownDomainEvent : IDomainEvent;

    private static FlightCreatedDomainEvent CreateFlightCreatedDomainEvent()
    {
        return new FlightCreatedDomainEvent(
            Guid.NewGuid(),
            "BD467",
            Guid.NewGuid(),
            DateTime.UtcNow,
            Guid.NewGuid(),
            DateTime.UtcNow.AddHours(2),
            Guid.NewGuid(),
            120m,
            DateTime.UtcNow,
            global::Flight.Flights.Enums.FlightStatus.Flying,
            100m,
            false);
    }

    [Fact]
    public void add_module_event_mapper_should_expose_flight_event_mapper_through_registration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddModuleEventMapper<FlightEventMapper>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Act
        var registration = scope.ServiceProvider.GetRequiredService<IEventMapperRegistration>();

        // Assert
        registration.Mapper.Should().BeOfType<FlightEventMapper>();
        registration.Mapper.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<FlightEventMapper>());
    }

    [Fact]
    public void composite_event_mapper_should_map_flight_domain_event_via_module_registration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddModuleEventMapper<FlightEventMapper>();
        services.AddCompositeEventMapper();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var eventMapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();
        var domainEvent = CreateFlightCreatedDomainEvent();

        // Act
        var integrationEvent = eventMapper.MapToIntegrationEvent(domainEvent);
        var internalCommand = eventMapper.MapToInternalCommand(domainEvent);

        // Assert
        eventMapper.Should().BeOfType<CompositeEventMapper>();
        integrationEvent.Should().BeOfType<FlightCreated>()
            .Which.Id.Should().Be(domainEvent.Id);
        internalCommand.Should().BeOfType<CreateFlightMongo>()
            .Which.Id.Should().Be(domainEvent.Id);
    }

    [Fact]
    public void composite_event_mapper_should_return_null_for_unknown_domain_event()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddModuleEventMapper<FlightEventMapper>();
        services.AddCompositeEventMapper();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var eventMapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        // Act
        var integrationEvent = eventMapper.MapToIntegrationEvent(new UnknownDomainEvent());
        var internalCommand = eventMapper.MapToInternalCommand(new UnknownDomainEvent());

        // Assert
        integrationEvent.Should().BeNull();
        internalCommand.Should().BeNull();
    }
}
