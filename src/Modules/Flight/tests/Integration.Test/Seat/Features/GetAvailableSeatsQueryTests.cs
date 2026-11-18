using BuildingBlocks.TestBase;
using Flight.Data;
using Flight.Seats.Enums;
using Flight.Seats.Exceptions;
using Flight.Seats.Features.GettingAvailableSeats.V1;
using Flight.Seats.Models;
using FluentAssertions;
using Integration.Test.Fakes;
using MassTransit;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace Integration.Test.Seat.Features;

public class GetAvailableSeatsQueryTests : FlightIntegrationTestBase
{
    public GetAvailableSeatsQueryTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_exclude_soft_deleted_seats_of_requested_flight()
    {
        var flightId = NewId.NextGuid();
        var activeSeat = CreateSeatReadModel(flightId, isDeleted: false);
        var deletedSeat = CreateSeatReadModel(flightId, isDeleted: true);

        await Fixture.ExecuteReadContextAsync(db => db.Seat.InsertManyAsync([activeSeat, deletedSeat]));

        var result = await Fixture.SendAsync(new GetAvailableSeats(flightId));

        result.SeatDtos.Should().ContainSingle().Which.Id.Should().Be(activeSeat.Id);
    }

    [Fact]
    public async Task should_throw_all_seats_full_when_requested_flight_has_only_deleted_seats()
    {
        var flightId = NewId.NextGuid();

        await Fixture.ExecuteReadContextAsync(db =>
            db.Seat.InsertManyAsync([CreateSeatReadModel(flightId, isDeleted: true)])
        );
        await Fixture.ExecuteReadContextAsync(db =>
            db.Seat.InsertManyAsync([CreateSeatReadModel(NewId.NextGuid(), isDeleted: false)])
        );

        var act = () => Fixture.SendAsync(new GetAvailableSeats(flightId));

        await act.Should().ThrowAsync<AllSeatsFullException>();
    }

    [Fact]
    public async Task should_cap_returned_seats_at_max_seats_returned()
    {
        var flightId = NewId.NextGuid();
        var seats = Enumerable
            .Range(0, GetAvailableSeatsQueryHandler.MaxSeatsReturned + 5)
            .Select(_ => CreateSeatReadModel(flightId, isDeleted: false))
            .ToList();

        await Fixture.ExecuteReadContextAsync(db => db.Seat.InsertManyAsync(seats));

        var result = await Fixture.SendAsync(new GetAvailableSeats(flightId));

        result.SeatDtos.Should().HaveCount(GetAvailableSeatsQueryHandler.MaxSeatsReturned);
        result.SeatDtos.Should().OnlyContain(x => x.FlightId == flightId);
    }

    [Fact]
    public async Task should_create_seat_index_on_flight_id_then_is_deleted_ascending()
    {
        var index = await Fixture.ExecuteReadContextAsync(async db =>
        {
            await db.EnsureIndexesAsync();
            var indexes = await (await db.Seat.Indexes.ListAsync()).ToListAsync();
            return indexes.Single(x => x["name"].AsString == "flightId_isDeleted");
        });

        var keys = index["key"].AsBsonDocument;
        keys.Names.Should().Equal("flightId", "isDeleted");
        keys.Values.Select(x => x.ToInt32()).Should().Equal(1, 1);
    }

    [Fact]
    public async Task ensure_indexes_should_be_idempotent()
    {
        var act = () => Fixture.ExecuteReadContextAsync(db => db.EnsureIndexesAsync());

        await act.Should().NotThrowAsync();
        await act.Should().NotThrowAsync();

        var indexNames = await Fixture.ExecuteReadContextAsync(async db =>
            (await (await db.Seat.Indexes.ListAsync()).ToListAsync()).Select(x => x["name"].AsString).ToList()
        );

        indexNames.Should().ContainSingle(x => x == "flightId_isDeleted");
    }

    private static SeatReadModel CreateSeatReadModel(Guid flightId, bool isDeleted) =>
        new()
        {
            Id = NewId.NextGuid(),
            SeatId = NewId.NextGuid(),
            SeatNumber = $"{Random.Shared.Next(1, 99)}A",
            Type = SeatType.Window,
            Class = SeatClass.Economy,
            FlightId = flightId,
            IsDeleted = isDeleted,
        };
}
