namespace Unit.Test.Flight.Features.Handlers.UpdateFlight;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using global::Flight.Data;
using global::Flight.Flights.Enums;
using global::Flight.Flights.Exceptions;
using global::Flight.Flights.Features.UpdatingFlight.V1;
using global::Flight.Flights.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class UpdateFlightHandlerTests
{
    [Fact]
    public async Task handler_updates_flight_fields_and_queues_updated_event()
    {
        using var context = DbContextFactory.Create();
        var flight = await context.Flights.FirstAsync();
        flight.ClearDomainEvents();
        var handler = new UpdateFlightHandler(context);
        var command = CreateCommand(flight.Id.Value);

        var result = await handler.Handle(command, CancellationToken.None);

        result.Id.Should().Be(flight.Id.Value);
        flight.FlightNumber.Value.Should().Be("UP123");
        flight.Price.Value.Should().Be(725.5m);
        flight.Status.Should().Be(FlightStatus.Delay);
        flight.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<FlightUpdatedDomainEvent>();
    }

    [Fact]
    public async Task handler_with_unknown_id_should_throw_flight_not_found()
    {
        using var context = DbContextFactory.Create();
        var handler = new UpdateFlightHandler(context);

        var act = () => handler.Handle(CreateCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<FlightNotFountException>();
    }

    [Fact]
    public async Task handler_with_null_command_should_throw_argument_null()
    {
        using var context = DbContextFactory.Create();
        var handler = new UpdateFlightHandler(context);

        var act = () => handler.Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task handler_with_empty_flight_number_should_throw_domain_exception()
    {
        using var context = DbContextFactory.Create();
        var flight = await context.Flights.FirstAsync();
        var handler = new UpdateFlightHandler(context);

        var act = () =>
            handler.Handle(CreateCommand(flight.Id.Value) with { FlightNumber = "" }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidFlightNumberException>();
    }

    [Fact]
    public async Task handler_with_negative_price_should_throw_domain_exception()
    {
        using var context = DbContextFactory.Create();
        var flight = await context.Flights.FirstAsync();
        var handler = new UpdateFlightHandler(context);

        var act = () => handler.Handle(CreateCommand(flight.Id.Value) with { Price = -1 }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidPriceException>();
    }

    private static UpdateFlight CreateCommand(Guid id) =>
        new(
            id,
            "UP123",
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2035, 4, 5, 10, 0, 0),
            new DateTime(2035, 4, 5, 12, 0, 0),
            Guid.NewGuid(),
            120,
            new DateTime(2035, 4, 5),
            FlightStatus.Delay,
            false,
            725.5m
        );
}
