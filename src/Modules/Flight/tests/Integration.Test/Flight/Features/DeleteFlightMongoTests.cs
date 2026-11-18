using System;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using global::Flight.Data;
using global::Flight.Flights.Exceptions;
using global::Flight.Flights.Features.DeletingFlight.V1;
using global::Flight.Flights.Models;
using Integration.Test.Fakes;
using MediatR;
using MongoDB.Driver;
using Xunit;

namespace Integration.Test.Flight.Features;

public class DeleteFlightMongoTests : FlightIntegrationTestBase
{
    public DeleteFlightMongoTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> fixture)
        : base(fixture) { }

    [Fact]
    public async Task should_leave_already_deleted_flight_unchanged()
    {
        var create = new FakeCreateFlightMongoCommand().Generate() with { IsDeleted = true };
        await Fixture.SendAsync(create);
        var before = await FindFlight(create.Id);
        var command = new DeleteFlightMongo(
            create.Id,
            create.FlightNumber,
            create.AircraftId,
            create.DepartureDate,
            create.DepartureAirportId,
            create.ArriveDate,
            create.ArriveAirportId,
            create.DurationMinutes,
            create.FlightDate,
            create.Status,
            create.Price,
            IsDeleted: false
        );

        var result = await Fixture.SendAsync(command);

        result.Should().Be(Unit.Value);
        var after = await FindFlight(create.Id);
        after.Should().BeEquivalentTo(before);
        after.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task should_throw_when_flight_read_model_is_missing()
    {
        var create = new FakeCreateFlightMongoCommand().Generate();
        var command = new DeleteFlightMongo(
            create.Id,
            create.FlightNumber,
            create.AircraftId,
            create.DepartureDate,
            create.DepartureAirportId,
            create.ArriveDate,
            create.ArriveAirportId,
            create.DurationMinutes,
            create.FlightDate,
            create.Status,
            create.Price,
            IsDeleted: true
        );

        var act = () => Fixture.SendAsync(command);

        await act.Should().ThrowAsync<FlightNotFountException>();
        (await FindFlight(create.Id)).Should().BeNull();
    }

    [Fact]
    public async Task should_mark_existing_flight_read_model_deleted()
    {
        var create = new FakeCreateFlightMongoCommand().Generate();
        await Fixture.SendAsync(create);
        var before = await FindFlight(create.Id);
        before.IsDeleted.Should().BeFalse();
        var command = new DeleteFlightMongo(
            create.Id,
            create.FlightNumber,
            create.AircraftId,
            create.DepartureDate,
            create.DepartureAirportId,
            create.ArriveDate,
            create.ArriveAirportId,
            create.DurationMinutes,
            create.FlightDate,
            create.Status,
            create.Price,
            IsDeleted: true
        );

        var result = await Fixture.SendAsync(command);

        result.Should().Be(Unit.Value);
        var after = await FindFlight(create.Id);
        after.IsDeleted.Should().BeTrue();
        after.Should().BeEquivalentTo(before, options => options.Excluding(x => x.IsDeleted));
    }

    private Task<FlightReadModel> FindFlight(Guid id) =>
        Fixture.ExecuteReadContextAsync(db => db.Flight.Find(x => x.FlightId == id).SingleOrDefaultAsync());
}
