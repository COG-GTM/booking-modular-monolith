namespace Unit.Test.Flight.Features.Handlers.DeleteFlight;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using global::Flight.Flights.Exceptions;
using global::Flight.Flights.Features.DeletingFlight.V1;
using global::Flight.Flights.ValueObjects;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class DeleteFlightCommandHandlerTests
{
    private readonly UnitTestFixture _fixture;
    private readonly DeleteFlightHandler _handler;

    public Task<DeleteFlightResult> Act(DeleteFlight command, CancellationToken cancellationToken) =>
        _handler.Handle(command, cancellationToken);

    public DeleteFlightCommandHandlerTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
        _handler = new DeleteFlightHandler(fixture.DbContext);
    }

    [Fact]
    public async Task handler_with_existing_flight_should_soft_delete_flight_and_return_its_id()
    {
        // Arrange
        var flight = FakeFlightCreate.Generate();
        await _fixture.DbContext.Flights.AddAsync(flight);
        await _fixture.DbContext.SaveChangesAsync();
        flight.ClearDomainEvents();

        var command = new DeleteFlight(flight.Id.Value);

        // Act
        var response = await Act(command, CancellationToken.None);

        // Assert
        response.Id.Should().Be(flight.Id.Value);

        var entity = await _fixture
            .DbContext.Flights.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == FlightId.Of(command.Id));

        entity.IsDeleted.Should().BeTrue();
        entity
            .DomainEvents.OfType<FlightDeletedDomainEvent>()
            .Should()
            .ContainSingle(e => e.Id == command.Id && e.IsDeleted);
    }

    [Fact]
    public async Task handler_with_unknown_flight_id_should_throw_flight_not_found_exception()
    {
        // Arrange
        var command = new DeleteFlight(NewId.NextGuid());

        // Act
        Func<Task> act = async () =>
        {
            await Act(command, CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<FlightNotFountException>();
    }

    [Fact]
    public async Task handler_with_null_command_should_throw_argument_exception()
    {
        // Arrange
        DeleteFlight command = null;

        // Act
        Func<Task> act = async () =>
        {
            await Act(command, CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
