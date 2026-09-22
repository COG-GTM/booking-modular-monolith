using System.Linq;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.TestBase;
using BuildingBlocks.Web;
using Flight;
using Flight.Api;
using Flight.Data;
using FluentAssertions;
using Integration.Test.Fakes;
using MassTransit;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test;

using global::Flight.Flights.Features.CreatingFlight.V1;

public class FlightApiHostTests : FlightIntegrationTestBase
{
    public FlightApiHostTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public void should_load_flight_service_app_options()
    {
        // Act
        var appOptions = Fixture.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        // Assert
        appOptions.Name.Should().Be("Flight-Service");
    }

    [Fact]
    public void should_register_composite_event_mapper_backed_by_flight_event_mapper()
    {
        // Arrange
        using var scope = Fixture.ServiceProvider.CreateScope();
        var command = new FakeCreateFlightCommand().Generate();
        var domainEvent = new FlightCreatedDomainEvent(
            command.Id,
            command.FlightNumber,
            command.AircraftId,
            command.DepartureDate,
            command.DepartureAirportId,
            command.ArriveDate,
            command.ArriveAirportId,
            command.DurationMinutes,
            command.FlightDate,
            command.Status,
            command.Price,
            false
        );

        // Act
        var eventMapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();
        var integrationEvent = eventMapper.MapToIntegrationEvent(domainEvent);
        var internalCommand = eventMapper.MapToInternalCommand(domainEvent);

        // Assert
        eventMapper.Should().BeOfType<CompositeEventMapper>();
        scope.ServiceProvider.GetRequiredService<FlightEventMapper>().Should().NotBeNull();
        integrationEvent.Should().BeOfType<FlightCreated>();
        ((FlightCreated)integrationEvent!).Id.Should().Be(command.Id);
        internalCommand.Should().BeOfType<CreateFlightMongo>();
        ((CreateFlightMongo)internalCommand!).Id.Should().Be(command.Id);
    }

    [Fact]
    public void should_register_flight_module_services()
    {
        // Arrange
        using var scope = Fixture.ServiceProvider.CreateScope();

        // Act + Assert
        scope.ServiceProvider.GetRequiredService<FlightDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<FlightReadDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IMediator>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IEventDispatcher>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().Should().NotBeNull();
    }

    [Fact]
    public void should_register_masstransit_bus()
    {
        // Act
        var bus = Fixture.ServiceProvider.GetService<IBus>();

        // Assert
        bus.Should().NotBeNull();
    }

    [Fact]
    public void should_not_reference_other_monolith_modules()
    {
        // Act
        var referencedAssemblies = typeof(Program).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToList();

        // Assert
        referencedAssemblies.Should().Contain("Flight");
        referencedAssemblies.Should().NotContain(["Identity", "Passenger", "Booking", "Api"]);
    }
}
