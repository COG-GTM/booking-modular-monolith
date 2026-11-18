using BuildingBlocks.TestBase;
using Flight.Data;
using Flight.Seats.ValueObjects;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Integration.Test.Seat;

using SeatEntity = global::Flight.Seats.Models.Seat;

public class SeatUniqueIndexTests : FlightIntegrationTestBase
{
    public SeatUniqueIndexTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task database_should_reject_duplicate_flight_and_seat_number()
    {
        await Fixture.ExecuteDbContextAsync(
            async Task (db) =>
            {
                var migrations = await db.Database.GetAppliedMigrationsAsync();
                migrations.Should().Contain("20261118154126_AddSeatFlightIdSeatNumberUniqueIndex");
                var existingSeat = await db.Seats.FirstAsync();
                var duplicate = SeatEntity.Create(
                    SeatId.Of(Guid.NewGuid()),
                    SeatNumber.Of(existingSeat.SeatNumber.Value),
                    existingSeat.Type,
                    existingSeat.Class,
                    existingSeat.FlightId
                );
                db.Seats.Add(duplicate);

                var act = async () => await db.SaveChangesAsync();

                var exception = await act.Should().ThrowAsync<DbUpdateException>();
                var postgresException = exception.Which.InnerException.Should().BeOfType<PostgresException>().Subject;
                postgresException.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
                postgresException.ConstraintName.Should().Be("ix_seat_flight_id_seat_number");
            }
        );
    }

    [Fact]
    public async Task database_should_allow_same_seat_number_on_another_flight()
    {
        var flightCommand = new FakeCreateFlightCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var seatId = SeatId.Of(Guid.NewGuid());
        await Fixture.ExecuteDbContextAsync(
            async Task (db) =>
            {
                var existingSeat = await db.Seats.FirstAsync();
                existingSeat.FlightId.Value.Should().NotBe(flightCommand.Id);
                var seat = SeatEntity.Create(
                    seatId,
                    SeatNumber.Of(existingSeat.SeatNumber.Value),
                    existingSeat.Type,
                    existingSeat.Class,
                    global::Flight.Flights.ValueObjects.FlightId.Of(flightCommand.Id)
                );
                db.Seats.Add(seat);

                await db.SaveChangesAsync();
            }
        );

        await Fixture.ExecuteDbContextAsync(
            async Task (db) =>
            {
                var seat = await db.Seats.SingleAsync(x => x.Id == seatId);
                seat.FlightId.Value.Should().Be(flightCommand.Id);
                (await db.Seats.CountAsync(x => x.SeatNumber.Value == seat.SeatNumber.Value)).Should().Be(2);
            }
        );
    }
}
