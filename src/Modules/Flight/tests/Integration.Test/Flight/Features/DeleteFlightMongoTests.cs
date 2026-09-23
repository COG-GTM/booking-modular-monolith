using System;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Integration.Test.Fakes;
using MassTransit;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Xunit;

namespace Integration.Test.Flight.Features;

using global::Flight.Flights.Exceptions;
using global::Flight.Flights.Features.GettingFlightById.V1;

public class DeleteFlightMongoTests : FlightIntegrationTestBase
{
    public DeleteFlightMongoTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_mark_flight_read_model_as_deleted_in_mongo()
    {
        // Arrange
        var createCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(createCommand);

        var deleteCommand = new FakeDeleteFlightMongoCommand(createCommand.Id).Generate();

        // Act
        await Fixture.SendAsync(deleteCommand);

        // Assert
        var flightReadModel = await Fixture.ExecuteReadContextAsync(db =>
            db.Flight.AsQueryable().SingleAsync(x => x.FlightId == createCommand.Id)
        );

        flightReadModel.IsDeleted.Should().BeTrue();
        flightReadModel.FlightNumber.Should().Be(createCommand.FlightNumber);

        Func<Task> act = async () => await Fixture.SendAsync(new GetFlightById(createCommand.Id));

        await act.Should().ThrowAsync<FlightNotFountException>();
    }

    [Fact]
    public async Task should_throw_flight_not_found_exception_when_flight_read_model_does_not_exist()
    {
        // Arrange
        var deleteCommand = new FakeDeleteFlightMongoCommand(NewId.NextGuid()).Generate();

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(deleteCommand);

        // Assert
        await act.Should().ThrowAsync<FlightNotFountException>();
    }

    [Fact]
    public async Task should_throw_flight_not_found_exception_when_flight_read_model_is_already_deleted()
    {
        // Arrange
        var createCommand = new FakeCreateFlightMongoCommand().Generate();

        await Fixture.SendAsync(createCommand);

        var deleteCommand = new FakeDeleteFlightMongoCommand(createCommand.Id).Generate();

        await Fixture.SendAsync(deleteCommand);

        // Act
        Func<Task> act = async () => await Fixture.SendAsync(deleteCommand);

        // Assert
        await act.Should().ThrowAsync<FlightNotFountException>();
    }
}
