namespace Unit.Test.Flight.Features.Handlers.DeleteFlight;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using global::Flight.Flights.Exceptions;
using global::Flight.Flights.Features.DeletingFlight.V1;
using Microsoft.EntityFrameworkCore;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class DeleteFlightHandlerTests
{
    [Fact]
    public async Task handler_soft_deletes_flight_and_queues_deleted_event()
    {
        using var context = DbContextFactory.Create();
        var flight = await context.Flights.FirstAsync();
        var originalNumber = flight.FlightNumber.Value;
        var originalPrice = flight.Price.Value;
        var originalAircraftId = flight.AircraftId.Value;
        flight.ClearDomainEvents();
        var handler = new DeleteFlightHandler(context);

        var result = await handler.Handle(new DeleteFlight(flight.Id.Value), CancellationToken.None);

        result.Id.Should().Be(flight.Id.Value);
        flight.IsDeleted.Should().BeTrue();
        flight.FlightNumber.Value.Should().Be(originalNumber);
        flight.Price.Value.Should().Be(originalPrice);
        flight.AircraftId.Value.Should().Be(originalAircraftId);
        var deletedEvent = flight
            .DomainEvents.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<FlightDeletedDomainEvent>()
            .Subject;
        deletedEvent.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task handler_with_unknown_id_should_throw_flight_not_found()
    {
        using var context = DbContextFactory.Create();
        var handler = new DeleteFlightHandler(context);

        var act = () => handler.Handle(new DeleteFlight(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<FlightNotFountException>();
    }

    [Fact]
    public async Task handler_with_null_command_should_throw_argument_null()
    {
        using var context = DbContextFactory.Create();
        var handler = new DeleteFlightHandler(context);

        var act = () => handler.Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void validator_rejects_empty_id_and_accepts_nonempty_id()
    {
        var validator = new DeleteFlightValidator();

        validator.Validate(new DeleteFlight(Guid.Empty)).IsValid.Should().BeFalse();
        validator.Validate(new DeleteFlight(Guid.NewGuid())).IsValid.Should().BeTrue();
    }
}
